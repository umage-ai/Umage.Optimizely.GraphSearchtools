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
    public string? ProfileKey { get; init; }
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
    public string? ProfileKey { get; init; }
}

/// <summary>
/// Recent edit row for the activity strip. One per <c>SearchProfileEdit</c>
/// DDS entry, cross-profile, newest-first.
/// </summary>
public sealed class InsightsActivityRow
{
    public DateTime At { get; init; }
    public string ProfileKey { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
}

/// <summary>
/// Search-activity KPIs over a fixed 30-day window. Drives the three-tile
/// card at the top of the Insights view: total searches, daily CTR, total
/// zero-result searches — each paired with a 30-day sparkline. The window
/// is intentionally not affected by the page-level 7d/30d toggle (which
/// scopes the lane tables); these KPIs are always 30-day so the sparkline
/// has enough resolution to be useful.
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

/// <summary>
/// Synonym coverage rollup — the panel that tells marketers whether their
/// synonym pool is keeping up with the search log. Surfaces the unused-rule
/// count from <see cref="SynonymCoverageService"/>; the missing-rules signal
/// (zero-result phrases that look like new rule candidates) is owned by the
/// per-profile insights pipeline now.
/// </summary>
public sealed class InsightsSynonymCoverage
{
    /// <summary>
    /// Total rules in the tenant's synonym pool (Global + per-language).
    /// </summary>
    public int TotalRules { get; init; }

    /// <summary>
    /// Rules whose triggers never matched a query in the window.
    /// </summary>
    public int UnusedRules { get; init; }

    /// <summary>
    /// Number of telemetry events scanned to produce these signals.
    /// </summary>
    public int LogsScanned { get; init; }

    public DateTime GeneratedAt { get; init; }
    public DateTime WindowStart { get; init; }
}
