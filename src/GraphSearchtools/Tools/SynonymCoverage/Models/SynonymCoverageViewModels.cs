namespace UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage.Models;

/// <summary>
/// Result envelope for the Synonym Coverage analyzer. Two parallel
/// suggestions: prune <see cref="UnusedEntries"/> (synonym terms that haven't
/// fired in the recent log window) and review <see cref="SuggestedAdds"/>
/// (zero-result phrases that look like missing synonyms).
/// </summary>
public sealed record SynonymCoverageResult
{
    /// <summary>UTC stamp of when the analyzer ran.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>UTC start of the rolling window the log scan covers (inclusive).</summary>
    public DateTime WindowStart { get; init; }

    /// <summary>Total log rows scanned in the window. Surfaced in the UI's "no logs" guard.</summary>
    public int LogsScanned { get; init; }

    /// <summary>Synonym entries (one per language + Global) that never matched a logged query.</summary>
    public IReadOnlyList<UnusedSynonymEntry> UnusedEntries { get; init; } =
        Array.Empty<UnusedSynonymEntry>();

    /// <summary>Top zero-result phrases the analyzer flags as candidates for new synonym rules.</summary>
    public IReadOnlyList<SuggestedSynonym> SuggestedAdds { get; init; } =
        Array.Empty<SuggestedSynonym>();
}

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

/// <summary>
/// One zero-result phrase the analyzer suggests as a candidate for adding to
/// the synonym set. Optional similarity diagnostics attached when fuzzy
/// matching against an indexed term succeeds.
/// </summary>
/// <param name="Phrase">The zero-result phrase the visitor typed.</param>
/// <param name="Locale">Most-common locale for the phrase in the window;
/// empty when unknown.</param>
/// <param name="Hits">How many sessions hit the phrase with zero results.</param>
/// <param name="ClosestIndexedTerm">Best-guess indexed term the phrase looks
/// like a typo or synonym of. <c>null</c> when no candidate cleared the
/// similarity threshold (the row still surfaces — fall-back mode just lists
/// top zero-result phrases that aren't already covered).</param>
/// <param name="Similarity">Levenshtein distance to <see cref="ClosestIndexedTerm"/>;
/// <c>null</c> when no closest term was found. <c>0</c> means an exact match
/// (which we filter out), <c>1..2</c> a typo, higher numbers a softer match.</param>
public sealed record SuggestedSynonym(
    string Phrase,
    string Locale,
    int Hits,
    string? ClosestIndexedTerm,
    int? Similarity);
