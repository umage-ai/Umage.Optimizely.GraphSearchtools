using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Tools.Insights.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights;

/// <summary>
/// Aggregation surface for the Insights dashboard. Three lanes — top phrases,
/// zero-result phrases, low-CTR phrases — all sourced from
/// <see cref="SearchLogsService"/>. Read-only; everything here is a projection
/// over the search-log telemetry the host SDK posts.
/// </summary>
internal sealed class InsightsService
{
    /// <summary>
    /// Default window for all three lanes (7 days). The JS toggle exposes
    /// 30 days too.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Fixed window for the search-activity KPI card. Independent of the
    /// page-level 7d/30d toggle; the sparkline needs 30 buckets to be useful.
    /// </summary>
    public const int SearchKpisWindowDays = 30;

    private readonly SearchLogsService _logs;
    private readonly ITelemetryReader _reader;

    public InsightsService(SearchLogsService logs, ITelemetryReader reader)
    {
        _logs = logs;
        _reader = reader;
    }

    /// <summary>
    /// Top phrases in the window, with a derived <c>zeroResults</c> count so
    /// the same row can show "people search for this" and "and this fraction
    /// found nothing" at once. <paramref name="days"/> is clamped to
    /// <c>[1, 90]</c>; the JS picker only sends 7 or 30.
    /// </summary>
    public async Task<IReadOnlyList<InsightsPhraseRow>> TopPhrasesAsync(
        int days,
        int take,
        string? channelKey = null,
        string? locale = null,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (since, until) = ResolveWindow(days, dateUtc);
        var rows = await _logs.TopPhrasesAsync(since, take, channelKey, locale, until, cancellationToken);
        return rows.Select(r => new InsightsPhraseRow
        {
            Phrase = r.Phrase,
            Count = r.Hits,
            // ZeroResultRate is a fraction in [0,1]; the wire count is
            // a long so big windows don't overflow Int32.
            ZeroResults = (long)Math.Round(r.Hits * r.ZeroResultRate),
            Locale = r.Locale,
            ChannelKey = r.ChannelKey
        }).ToList();
    }

    /// <summary>
    /// Zero-result phrases in the window — the pin-candidate list.
    /// </summary>
    public async Task<IReadOnlyList<InsightsZeroResultRow>> ZeroResultPhrasesAsync(
        int days,
        int take,
        string? channelKey = null,
        string? locale = null,
        DateTime? dateUtc = null,
        CancellationToken cancellationToken = default)
    {
        var (since, until) = ResolveWindow(days, dateUtc);
        var rows = await _logs.ZeroResultPhrasesAsync(since, take, channelKey, locale, until, cancellationToken);
        return rows.Select(r => new InsightsZeroResultRow
        {
            Phrase = r.Phrase,
            Count = r.Hits,
            Locale = r.Locale,
            ChannelKey = r.ChannelKey
        }).ToList();
    }

    /// Resolve a phrase-lane window. When <paramref name="dateUtc"/> is set,
    /// returns a 24h window over that UTC day (overrides <paramref name="days"/>).
    /// Otherwise returns <c>[now - days, now]</c>.
    /// </summary>
    private static (DateTime since, DateTime until) ResolveWindow(int days, DateTime? dateUtc)
    {
        if (dateUtc.HasValue)
        {
            var d = dateUtc.Value.Kind == DateTimeKind.Utc ? dateUtc.Value.Date : dateUtc.Value.ToUniversalTime().Date;
            return (d, d.AddDays(1));
        }
        var now = DateTime.UtcNow;
        return (now - TimeSpan.FromDays(Math.Clamp(days, 1, 90)), now);
    }

    /// <summary>
    /// Low-CTR phrases in the window. The reader's threshold (half the window
    /// mean) keeps the surface actionable; <paramref name="days"/> is clamped
    /// to <c>[1, 90]</c>.
    /// </summary>
    public async Task<IReadOnlyList<InsightsPhraseRow>> LowCtrPhrasesAsync(
        int days,
        int take,
        string? channelKey = null,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(Math.Clamp(days, 1, 90));
        var rows = await _logs.LowCtrPhrasesAsync(since, take, channelKey, locale, cancellationToken: cancellationToken);
        return rows.Select(r => new InsightsPhraseRow
        {
            Phrase = r.Phrase,
            Count = r.Hits,
            ZeroResults = (long)Math.Round(r.Hits * r.ZeroResultRate),
            Ctr = r.Ctr,
            Locale = r.Locale,
            ChannelKey = r.ChannelKey
        }).ToList();
    }

    /// <summary>
    /// 30-day search-activity KPIs — totals + per-day sparkline series for
    /// searches, CTR, and zero-result searches. The window is fixed (30 days
    /// ending now-UTC) regardless of the page-level toggle, and the spark
    /// arrays are always 30 entries with missing days zero-filled so callers
    /// can render a fixed-width chart without conditional plumbing.
    /// When <paramref name="channelKey"/> is supplied the read is scoped to
    /// one channel (used by the Channel detail surface); omitted means the
    /// global aggregate (used by the standalone Insights tool).
    /// </summary>
    public async Task<InsightsSearchKpis> SearchKpisAsync(
        string? channelKey = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        // Today (UTC) + 29 prior days = 30 calendar days. Anchoring on the
        // day boundary keeps the sparkline consistent across refreshes during
        // a day, instead of sliding by the second.
        var since = now.Date.AddDays(-(SearchKpisWindowDays - 1));

        var channel = string.IsNullOrWhiteSpace(channelKey) ? null : channelKey;
        var query = new TelemetryQuery(since, now, Take: 0, ChannelKey: channel);
        var daily = await _reader.DailyTotalsAsync(query, cancellationToken);

        var byDate = daily.ToDictionary(d => d.DateUtc.Date);
        var sparkSearches = new int[SearchKpisWindowDays];
        var sparkZero = new int[SearchKpisWindowDays];
        var sparkCtr = new double[SearchKpisWindowDays];
        long totalSearches = 0, totalZero = 0, totalClicks = 0;

        for (int i = 0; i < SearchKpisWindowDays; i++)
        {
            var day = since.AddDays(i);
            if (!byDate.TryGetValue(day, out var d)) continue;

            sparkSearches[i] = d.Searches;
            sparkZero[i]     = d.Zeroes;
            sparkCtr[i]      = d.Searches > 0 ? (double)d.Clicks / d.Searches * 100.0 : 0.0;
            totalSearches += d.Searches;
            totalZero     += d.Zeroes;
            totalClicks   += d.Clicks;
        }

        var ctrPct = totalSearches > 0 ? (double)totalClicks / totalSearches * 100.0 : 0.0;

        return new InsightsSearchKpis
        {
            TotalSearches = totalSearches,
            TotalZero     = totalZero,
            CtrPct        = ctrPct,
            WindowDays    = SearchKpisWindowDays,
            WindowEndUtc  = now,
            SparkSearches = sparkSearches,
            SparkZero     = sparkZero,
            SparkCtr      = sparkCtr
        };
    }
}
