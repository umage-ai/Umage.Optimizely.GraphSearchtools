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
    private readonly LocalTelemetryOptions _options;

    public LocalTelemetryReader(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options.Value.Telemetry;
    }

    public Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        var rows = LoadAggregated(query);
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
        var rows = LoadAggregated(query);
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
        var rows = LoadAggregated(query);
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
    /// Pulls bucket rows for the window, optionally filtered, and collapses
    /// them across NodeId so a phrase appears once even when it was seen on
    /// many instances.
    /// </summary>
    private List<PhraseAggregate> LoadAggregated(TelemetryQuery query)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var buckets = store.Items<SearchLogBucket>()
            .Where(b => b.BucketUtc >= query.SinceUtc && b.BucketUtc < query.UntilUtc)
            .ToList();

        if (!string.IsNullOrEmpty(query.ProfileKey))
            buckets = buckets.Where(b => b.ProfileKey == query.ProfileKey).ToList();
        if (!string.IsNullOrEmpty(query.Locale))
            buckets = buckets.Where(b => b.Locale == query.Locale).ToList();

        return buckets
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
