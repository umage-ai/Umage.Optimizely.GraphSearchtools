using EPiServer.Data.Dynamic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Reads aggregates straight from the DDS bucket store. Cross-instance sums
/// are done at query time — every reader collapses per-node minute buckets
/// down to one row per (phrase, channel, locale).
/// </summary>
internal sealed class LocalTelemetryReader : ITelemetryReader
{
    /// <summary>
    /// How long an aggregate result stays cached. Long enough that the three
    /// concurrent lanes (Top / ZeroResult / LowCtr) on a typical Channel
    /// Insights page render share one underlying read; short enough that a
    /// click on the refresh button after a few seconds returns fresh data.
    /// Also masks the cost of cold long-window queries (30d) for adjacent
    /// repeat reads.
    /// </summary>
    private static readonly TimeSpan AggregateCacheTtl = TimeSpan.FromSeconds(30);

    private readonly LocalTelemetryOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LocalTelemetryReader> _logger;
    private readonly object _cacheLock = new();
    private CacheEntry? _cache;

    private readonly object _columnMapLock = new();
    private BucketColumnMap? _columnMap;
    private bool _columnMapResolved;

    public LocalTelemetryReader(
        IOptions<GraphSearchtoolsOptions> options,
        IConfiguration configuration,
        ILogger<LocalTelemetryReader> logger)
    {
        _options = options.Value.Telemetry;
        _configuration = configuration;
        _logger = logger;
    }

    private readonly record struct CacheKey(DateTime SinceUtc, DateTime UntilUtc, string? ChannelKey, string? Locale);
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

    /// <summary>
    /// Daily roll-up — one row per UTC day with at least one event. The fast
    /// SQL path groups by <c>CAST(BucketUtc AS date)</c>; the LINQ fallback
    /// projects through <see cref="DateTime.Date"/>. Caching is intentionally
    /// not shared with the phrase-aggregate cache because the result shape is
    /// different and the call pattern is once-per-render, not three-concurrent.
    /// </summary>
    public Task<IReadOnlyList<DailyAggregate>> DailyTotalsAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        var rows = LoadDailyFast(query) ?? LoadDailySlow(query);
        return Task.FromResult<IReadOnlyList<DailyAggregate>>(rows);
    }

    private List<DailyAggregate>? LoadDailyFast(TelemetryQuery query)
    {
        var map = ResolveColumnMap();
        if (map == null) return null;

        var connectionString = _configuration.GetConnectionString("EPiServerDB");
        if (string.IsNullOrEmpty(connectionString)) return null;

        // Column names are whitelist-validated by BucketColumnMap; the store
        // name literal is constant. Direct interpolation is safe here.
        var channelFilter = !string.IsNullOrEmpty(query.ChannelKey) ? $" AND {map.ChannelKey} = @channel" : string.Empty;
        var localeFilter  = !string.IsNullOrEmpty(query.Locale)     ? $" AND {map.Locale} = @locale"      : string.Empty;
        var sql = $@"
SELECT
    CAST({map.BucketUtc} AS date)                AS DayUtc,
    SUM({map.Hits})                              AS Hits,
    SUM({map.Zeroes})                            AS Zeroes,
    SUM({map.Clicks1} + {map.Clicks2} + {map.Clicks3}) AS Clicks
FROM tblBigTable
WHERE StoreName = 'GraphSearchtools_SearchLogBucket'
  AND {map.BucketUtc} >= @since
  AND {map.BucketUtc} <  @until{channelFilter}{localeFilter}
GROUP BY CAST({map.BucketUtc} AS date)
ORDER BY DayUtc ASC";

        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@since", System.Data.SqlDbType.DateTime) { Value = query.SinceUtc });
            cmd.Parameters.Add(new SqlParameter("@until", System.Data.SqlDbType.DateTime) { Value = query.UntilUtc });
            if (!string.IsNullOrEmpty(query.ChannelKey))
                cmd.Parameters.Add(new SqlParameter("@channel", query.ChannelKey));
            if (!string.IsNullOrEmpty(query.Locale))
                cmd.Parameters.Add(new SqlParameter("@locale", query.Locale));

            using var reader = cmd.ExecuteReader();
            var rows = new List<DailyAggregate>(capacity: 32);
            while (reader.Read())
            {
                // CAST AS date comes back as DateTime with Kind=Unspecified; we
                // re-stamp UTC so downstream serialization round-trips cleanly.
                var day = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
                var hits   = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var zeroes = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var clicks = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                rows.Add(new DailyAggregate(day, hits, zeroes, clicks));
            }
            return rows;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry daily-totals fast-path SQL query failed; falling back to DDS-LINQ.");
            return null;
        }
    }

    private List<DailyAggregate> LoadDailySlow(TelemetryQuery query)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var q = store.Items<SearchLogBucket>()
            .Where(b => b.BucketUtc >= query.SinceUtc && b.BucketUtc < query.UntilUtc);

        if (!string.IsNullOrEmpty(query.ChannelKey))
            q = q.Where(b => b.ChannelKey == query.ChannelKey);
        if (!string.IsNullOrEmpty(query.Locale))
            q = q.Where(b => b.Locale == query.Locale);

        return q
            .ToList()
            .GroupBy(b => b.BucketUtc.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyAggregate(
                DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                g.Sum(b => b.Hits),
                g.Sum(b => b.Zeroes),
                g.Sum(b => b.Clicks1 + b.Clicks2 + b.Clicks3)))
            .ToList();
    }

    public Task<IReadOnlyList<RawEvent>> RecentRawAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogRing));
        var rows = store.Items<SearchLogRing>()
            .Where(r => r.TimestampUtc >= query.SinceUtc && r.TimestampUtc < query.UntilUtc)
            .ToList();

        if (!string.IsNullOrEmpty(query.ChannelKey))
            rows = rows.Where(r => r.ChannelKey == query.ChannelKey).ToList();
        if (!string.IsNullOrEmpty(query.Locale))
            rows = rows.Where(r => r.Locale == query.Locale).ToList();

        IReadOnlyList<RawEvent> result = rows
            .OrderByDescending(r => r.TimestampUtc)
            .Take(query.Take)
            .Select(r => new RawEvent(
                r.TimestampUtc,
                r.Kind,
                r.Phrase,
                r.ChannelKey,
                r.Locale,
                r.ResultCount,
                r.ClickRank,
                r.NodeId))
            .ToList();
        return Task.FromResult(result);
    }

    /// <summary>
    /// <see cref="LoadAggregated"/> with a short TTL cache keyed on
    /// (window, channel, locale). The three lanes
    /// (<see cref="TopPhrasesAsync"/>, <see cref="ZeroResultPhrasesAsync"/>,
    /// <see cref="LowCtrPhrasesAsync"/>) all run the same window query but
    /// post-process differently — caching the aggregate means a Channel
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
            query.ChannelKey,
            query.Locale);

        lock (_cacheLock)
        {
            if (_cache is { } existing && existing.Key == key && existing.ExpiresAt > DateTime.UtcNow)
            {
                return existing.Rows;
            }
        }

        var rows = LoadAggregatedFast(query) ?? LoadAggregated(query);

        lock (_cacheLock)
        {
            _cache = new CacheEntry(key, DateTime.UtcNow.Add(AggregateCacheTtl), rows);
        }

        return rows;
    }

    /// <summary>
    /// Fast path: a single SQL query that lets SQL Server do the GROUP BY
    /// server-side instead of round-tripping every bucket row through DDS'
    /// reflection-based projection. Returns null when the column map can't
    /// be resolved (fresh install before first write, or DB unavailable);
    /// the caller falls back to the LINQ-over-DDS path. With ~30k rows in
    /// the window, raw SQL is ~10–20 ms vs. ~10 s for the LINQ path.
    /// </summary>
    private List<PhraseAggregate>? LoadAggregatedFast(TelemetryQuery query)
    {
        var map = ResolveColumnMap();
        if (map == null) return null;

        var connectionString = _configuration.GetConnectionString("EPiServerDB");
        if (string.IsNullOrEmpty(connectionString)) return null;

        var sql = BuildAggregateSql(map, includeChannel: !string.IsNullOrEmpty(query.ChannelKey), includeLocale: !string.IsNullOrEmpty(query.Locale));

        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqlParameter("@since", System.Data.SqlDbType.DateTime) { Value = query.SinceUtc });
            cmd.Parameters.Add(new SqlParameter("@until", System.Data.SqlDbType.DateTime) { Value = query.UntilUtc });
            if (!string.IsNullOrEmpty(query.ChannelKey))
                cmd.Parameters.Add(new SqlParameter("@channel", query.ChannelKey));
            if (!string.IsNullOrEmpty(query.Locale))
                cmd.Parameters.Add(new SqlParameter("@locale", query.Locale));

            using var reader = cmd.ExecuteReader();
            var rows = new List<PhraseAggregate>(capacity: 256);
            while (reader.Read())
            {
                var phraseNorm = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var channelKey = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var locale     = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var hits       = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var zeroes     = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                var clicks     = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                var display    = reader.IsDBNull(6) ? phraseNorm : reader.GetString(6);

                var zeroRate = hits == 0 ? (zeroes > 0 ? 1.0 : 0.0) : (double)zeroes / hits;
                var ctr = hits == 0 ? 0.0 : (double)clicks / hits;
                rows.Add(new PhraseAggregate(display, hits, zeroRate, ctr, locale, channelKey));
            }
            return rows;
        }
        catch (Exception ex)
        {
            // SQL fast path failed for any reason — log once at warning, then
            // disable for this read so the caller falls back to DDS-LINQ.
            // Common causes: connection drift, DDS schema bump that obsoleted
            // a column we cached the old name for. The fallback always works.
            _logger.LogWarning(ex, "Telemetry fast-path SQL query failed; falling back to DDS-LINQ.");
            return null;
        }
    }

    private static string BuildAggregateSql(BucketColumnMap m, bool includeChannel, bool includeLocale)
    {
        // Column names come from a whitelist-validated source (see
        // BucketColumnMap.ResolveAsync), so direct interpolation is safe.
        // The literal store name is constant, also safe.
        var channelFilter = includeChannel ? $" AND {m.ChannelKey} = @channel" : string.Empty;
        var localeFilter = includeLocale ? $" AND {m.Locale} = @locale" : string.Empty;
        return $@"
SELECT
    {m.PhraseNorm}    AS PhraseNorm,
    {m.ChannelKey}    AS ChannelKey,
    {m.Locale}        AS Locale,
    SUM({m.Hits})     AS Hits,
    SUM({m.Zeroes})   AS Zeroes,
    SUM({m.Clicks1} + {m.Clicks2} + {m.Clicks3}) AS Clicks,
    MIN(NULLIF({m.DisplayPhrase}, ''))           AS DisplayPhrase
FROM tblBigTable
WHERE StoreName = 'GraphSearchtools_SearchLogBucket'
  AND {m.BucketUtc} >= @since
  AND {m.BucketUtc} <  @until{channelFilter}{localeFilter}
GROUP BY {m.PhraseNorm}, {m.ChannelKey}, {m.Locale}";
    }

    /// <summary>
    /// Lazy-initialised, thread-safe column map cache. First reader pays the
    /// resolve cost (~5 ms); every subsequent read is a field load.
    /// </summary>
    private BucketColumnMap? ResolveColumnMap()
    {
        if (_columnMapResolved) return _columnMap;
        lock (_columnMapLock)
        {
            if (_columnMapResolved) return _columnMap;
            var connectionString = _configuration.GetConnectionString("EPiServerDB");
            if (string.IsNullOrEmpty(connectionString))
            {
                _columnMapResolved = true;
                return null;
            }
            try
            {
                _columnMap = BucketColumnMap.ResolveAsync(connectionString, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telemetry column-map resolve failed; reader will use DDS-LINQ path until next process start.");
                _columnMap = null;
            }
            _columnMapResolved = true;
            return _columnMap;
        }
    }

    /// <summary>
    /// Pulls bucket rows for the window, optionally filtered, and collapses
    /// them across NodeId so a phrase appears once even when it was seen on
    /// many instances. Filters are chained into the LINQ expression so DDS
    /// picks the most selective index (BucketUtc range, then ChannelKey or
    /// Locale equality) instead of materialising every row in the window.
    /// </summary>
    private List<PhraseAggregate> LoadAggregated(TelemetryQuery query)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var q = store.Items<SearchLogBucket>()
            .Where(b => b.BucketUtc >= query.SinceUtc && b.BucketUtc < query.UntilUtc);

        if (!string.IsNullOrEmpty(query.ChannelKey))
            q = q.Where(b => b.ChannelKey == query.ChannelKey);
        if (!string.IsNullOrEmpty(query.Locale))
            q = q.Where(b => b.Locale == query.Locale);

        return q
            .ToList()
            .GroupBy(b => new { b.PhraseNorm, b.ChannelKey, b.Locale })
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
                return new PhraseAggregate(display, hits, zeroRate, ctr, g.Key.Locale, g.Key.ChannelKey);
            })
            .ToList();
    }
}
