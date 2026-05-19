namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights.Models;

/// <summary>
/// Combined row returned by <c>GET InsightsApi/TopPhrases</c>. Joins hit-count
/// with the zero-result count derived from the telemetry reader's
/// <c>ZeroResultRate</c>, so a marketer sees both "popular" and "needs
/// attention" signals on one row.
/// </summary>
public sealed class InsightsPhraseRow
{
    public string Phrase { get; init; } = string.Empty;
    public long Count { get; init; }
    public long ZeroResults { get; init; }
    public string? Locale { get; init; }
    public string? ChannelKey { get; init; }

    /// <summary>
    /// Click-through rate as a fraction in <c>[0, 1]</c> — only populated by
    /// <c>LowCtrPhrasesAsync</c> where it's the row's load-bearing signal.
    /// Top-phrase rows leave it at the default <c>0</c>; the UI column that
    /// reads it is gated to the Low-CTR lane.
    /// </summary>
    public double Ctr { get; init; }
}

/// <summary>
/// Zero-result phrase variant — same shape as a regular phrase row but with
/// the count interpreted as "sessions returning zero hits".
/// </summary>
public sealed class InsightsZeroResultRow
{
    public string Phrase { get; init; } = string.Empty;
    public long Count { get; init; }
    public string? Locale { get; init; }
    public string? ChannelKey { get; init; }
}

/// <summary>
/// Search-activity KPIs over a fixed 30-day window. Drives the three-tile
/// card above the tab strip on the Insights view: total searches, daily CTR,
/// total zero-result searches — each paired with a 30-day sparkline. The
/// window is intentionally not affected by the page-level 7d/30d toggle
/// (which scopes the lane tables); these KPIs are always 30-day so the
/// sparkline has enough resolution to be useful.
/// </summary>
public sealed class InsightsSearchKpis
{
    /// <summary>Total search hits across the window.</summary>
    public long TotalSearches { get; init; }

    /// <summary>Total searches that returned no results.</summary>
    public long TotalZero { get; init; }

    /// <summary>
    /// Click-through rate as a percentage in <c>[0, 100]</c>. Computed as
    /// <c>SUM(clicks) / SUM(searches) × 100</c> across the whole window —
    /// total ÷ total, not the mean of daily ratios, so volume-weighted.
    /// </summary>
    public double CtrPct { get; init; }

    public int WindowDays { get; init; }
    public DateTime WindowEndUtc { get; init; }

    /// <summary>
    /// 30 entries, oldest-first, zero-filled for days with no events.
    /// </summary>
    public IReadOnlyList<int> SparkSearches { get; init; } = Array.Empty<int>();

    /// <summary>30 entries, oldest-first, zero-filled.</summary>
    public IReadOnlyList<int> SparkZero { get; init; } = Array.Empty<int>();

    /// <summary>
    /// 30 entries, oldest-first, each a percentage in <c>[0, 100]</c>. Days
    /// with zero searches report 0 (the alternative — null — would force the
    /// sparkline to choose between drop-to-zero and skip-with-gap; zero is
    /// the less surprising default).
    /// </summary>
    public IReadOnlyList<double> SparkCtr { get; init; } = Array.Empty<double>();
}
