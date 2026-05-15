namespace UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage.Models;

/// <summary>
/// Result envelope for the Synonym Coverage analyzer. Surfaces synonym rules
/// that haven't fired in the recent log window (prune candidates). The
/// former "suggested adds" pass — zero-result phrases that look like missing
/// rules — moves to the per-profile insights pipeline.
/// </summary>
public sealed record SynonymCoverageResult
{
    /// <summary>UTC stamp of when the analyzer ran.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>UTC start of the rolling window the log scan covers (inclusive).</summary>
    public DateTime WindowStart { get; init; }

    /// <summary>Total log rows scanned in the window. Surfaced in the UI's "no logs" guard.</summary>
    public int LogsScanned { get; init; }

    /// <summary>Total synonym rules parsed across every language + Global blob.</summary>
    public int TotalRules { get; init; }

    /// <summary>Synonym entries (one per language + Global) that never matched a logged query.</summary>
    public IReadOnlyList<UnusedSynonymEntry> UnusedEntries { get; init; } =
        Array.Empty<UnusedSynonymEntry>();

    /// <summary>
    /// Per-rule query-hit totals for the rolling window. One row per parsed
    /// synonym entry; <see cref="RuleActivityRow.Hits"/> is the sum of
    /// trigger-term hits and 0 means "no recorded activity". Powers the
    /// Synonyms grid's Activity (30d) column.
    /// </summary>
    public IReadOnlyList<RuleActivityRow> RuleActivity { get; init; } =
        Array.Empty<RuleActivityRow>();
}

/// <param name="Language">BCP-47 locale, or <c>"Global"</c> for the tenant-wide blob.</param>
/// <param name="Entry">The original rule line verbatim.</param>
/// <param name="Hits">Sum of trigger-term hits in the analyzer's window.</param>
public sealed record RuleActivityRow(string Language, string Entry, long Hits);

/// <summary>
/// One synonym entry that didn't fire against any logged query in the window.
/// </summary>
/// <param name="Language">BCP-47 locale of the synonym slot, or <c>"Global"</c>
/// for the tenant-wide blob. Used to deep-link back into the Synonyms editor.</param>
/// <param name="Entry">The original rule line as it appears in the synonym
/// blob — preserved verbatim so the user can match it against their editor.</param>
/// <param name="Reason">Short, human-readable explanation. v1 always surfaces
/// "no logged query in last 30 days" but the field is open-ended so future
/// reasons (orphaned target content, language drift) drop in cleanly.</param>
public sealed record UnusedSynonymEntry(string Language, string Entry, string Reason);
