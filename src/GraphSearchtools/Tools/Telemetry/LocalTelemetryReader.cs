using EPiServer.Data.Dynamic;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Reads aggregates straight from the DDS bucket store. Cross-instance sums
/// are done at query time — every reader collapses per-node minute buckets
/// down to one row per (phrase, profile, locale).
/// </summary>
internal sealed class LocalTelemetryReader : ITelemetryReader
{
    /// <summary>
    /// How long an aggregate result stays cached. Long enough that the three
    /// concurrent lanes (Top / ZeroResult / LowCtr) on a typical Profile
    /// Insights page render share one underlying read; short enough that a
    /// click on the refresh button after a few seconds returns fresh data.
    /// Also masks the cost of cold long-window queries (30d) for adjacent
    /// repeat reads.
    /// </summary>
    private static readonly TimeSpan AggregateCacheTtl = TimeSpan.FromSeconds(30);

    private readonly LocalTelemetryOptions _options;
    private readonly object _cacheLock = new();
    private CacheEntry? _cache;

    public LocalTelemetryReader(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options.Value.Telemetry;
    }

    private readonly record struct CacheKey(DateTime SinceUtc, DateTime UntilUtc, string? ProfileKey, string? Locale);
    private sealed record CacheEntry(CacheKey Key, DateTime ExpiresAt, IReadOnlyList<PhraseAggregate> Rows);

    private static DateTime BucketTimestamp(DateTime utc)
    {
        var ticks = AggregateCacheTtl.Ticks;
        return new DateTime((utc.Ticks / ticks) * ticks, DateTimeKind.Utc);
    }

    public Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        var rows = LoadAggregatedCached(query);
        IReadOnlyList<PhraseAggregate> result = rows
            .OrderByDescending(r => r.Hits)
            .ThenBy(r => r.Phrase, StringComparer.Ordinal)
            .Take(query.Take)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        // Order by absolute zero count, not rate — a phrase with 5/5 zeroes is
        // less actionable than 200/300, and rate-only ranking surfaces noise.
        var rows = LoadAggregatedCached(query);
        IReadOnlyList<PhraseAggregate> result = rows
            .Where(r => r.ZeroResultRate > 0)
            .OrderByDescending(r => r.ZeroResultRate)
            .ThenByDescending(r => r.Hits)
            .Take(query.Take)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        // "Low CTR" relative to the window's own average. Threshold = 0.5×
        // window mean — phrases noticeably underperforming, not the long
        // tail of "fewer hits than the head".
        var rows = LoadAggregatedCached(query);
        var withClicks = rows.Where(r => r.Hits > 0).ToList();
        if (withClicks.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());
        }

        var meanCtr = withClicks.Average(r => r.Ctr);
        var threshold = meanCtr * 0.5;

        IReadOnlyList<PhraseAggregate> result = withClicks
            .Where(r => r.Ctr < threshold)
            .OrderByDescending(r => r.Hits)
            .Take(query.Take)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<RawEvent>> RecentRawAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogRing));
        var rows = store.Items<SearchLogRing>()
            .Where(r => r.TimestampUtc >= query.SinceUtc && r.TimestampUtc < query.UntilUtc)
            .ToList();

        if (!string.IsNullOrEmpty(query.ProfileKey))
            rows = rows.Where(r => r.ProfileKey == query.ProfileKey).ToList();
        if (!string.IsNullOrEmpty(query.Locale))
            rows = rows.Where(r => r.Locale == query.Locale).ToList();

        IReadOnlyList<RawEvent> result = rows
            .OrderByDescending(r => r.TimestampUtc)
            .Take(query.Take)
            .Select(r => new RawEvent(
                r.TimestampUtc,
                r.Kind,
                r.Phrase,
                r.ProfileKey,
                r.Locale,
                r.ResultCount,
                r.ClickRank,
                r.NodeId))
            .ToList();
        return Task.FromResult(result);
    }

    /// <summary>
    /// <see cref="LoadAggregated"/> with a short TTL cache keyed on
    /// (window, profile, locale). The three lanes
    /// (<see cref="TopPhrasesAsync"/>, <see cref="ZeroResultPhrasesAsync"/>,
    /// <see cref="LowCtrPhrasesAsync"/>) all run the same window query but
    /// post-process differently — caching the aggregate means a Profile
    /// Insights page render fans out to one read, not three.
    /// </summary>
    private IReadOnlyList<PhraseAggregate> LoadAggregatedCached(TelemetryQuery query)
    {
        // Round window endpoints to the cache TTL so concurrent lanes + quick
        // refresh clicks share a single entry. Without this, UntilUtc is a
        // fresh DateTime.UtcNow per request and the cache never hits.
        var key = new CacheKey(
            BucketTimestamp(query.SinceUtc),
            BucketTimestamp(query.UntilUtc),
            query.ProfileKey,
            query.Locale);

        lock (_cacheLock)
        {
            if (_cache is { } existing && existing.Key == key && existing.ExpiresAt > DateTime.UtcNow)
            {
                return existing.Rows;
            }
        }

        var rows = LoadAggregated(query);

        lock (_cacheLock)
        {
            _cache = new CacheEntry(key, DateTime.UtcNow.Add(AggregateCacheTtl), rows);
        }

        return rows;
    }

    /// <summary>
    /// Pulls bucket rows for the window, optionally filtered, and collapses
    /// them across NodeId so a phrase appears once even when it was seen on
    /// many instances. Filters are chained into the LINQ expression so DDS
    /// picks the most selective index (BucketUtc range, then ProfileKey or
    /// Locale equality) instead of materialising every row in the window.
    /// </summary>
    private List<PhraseAggregate> LoadAggregated(TelemetryQuery query)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var q = store.Items<SearchLogBucket>()
            .Where(b => b.BucketUtc >= query.SinceUtc && b.BucketUtc < query.UntilUtc);

        if (!string.IsNullOrEmpty(query.ProfileKey))
            q = q.Where(b => b.ProfileKey == query.ProfileKey);
        if (!string.IsNullOrEmpty(query.Locale))
            q = q.Where(b => b.Locale == query.Locale);

        return q
            .ToList()
            .GroupBy(b => new { b.PhraseNorm, b.ProfileKey, b.Locale })
            .Select(g =>
            {
                var hits = g.Sum(b => b.Hits);
                var zeroes = g.Sum(b => b.Zeroes);
                var clicks = g.Sum(b => b.Clicks1 + b.Clicks2 + b.Clicks3);
                var display = g.Where(b => !string.IsNullOrEmpty(b.DisplayPhrase))
                    .Select(b => b.DisplayPhrase)
                    .FirstOrDefault() ?? g.Key.PhraseNorm;
                var zeroRate = hits == 0 ? (zeroes > 0 ? 1.0 : 0.0) : (double)zeroes / hits;
                var ctr = hits == 0 ? 0.0 : (double)clicks / hits;
                return new PhraseAggregate(display, hits, zeroRate, ctr, g.Key.Locale, g.Key.ProfileKey);
            })
            .ToList();
    }
}
