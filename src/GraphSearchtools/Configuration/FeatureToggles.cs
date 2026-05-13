namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Feature toggles for enabling/disabling individual tools.
/// Each toggle defaults to enabled so a fresh install lights up every tool
/// the package version supports.
/// </summary>
public class FeatureToggles
{
    public bool Overview { get; set; } = true;
    public bool Pinned { get; set; } = true;
    public bool Synonyms { get; set; } = true;
    public bool Health { get; set; } = true;
    public bool Autocomplete { get; set; } = true;
    public bool Profiles { get; set; } = true;

    /// <summary>
    /// Aurora refactor — curated marketer-facing dashboard surfacing top
    /// phrases, zero-result candidates, synonym coverage signals, and a
    /// recent-activity strip. Read-only; reuses SearchLogsService +
    /// SynonymCoverageService + SearchProfileEditService data.
    /// </summary>
    public bool Insights { get; set; } = true;

    /// <summary>
    /// Gates the public ingest beacon (<c>POST /api/telemetry/searchlog</c>).
    /// When false the endpoint returns 404 and zero events reach the sink —
    /// the analytics UIs render their empty state without further wiring.
    /// </summary>
    public bool Telemetry { get; set; } = true;

    /// <summary>
    /// Search Logs UI. Read-only surface over <c>ITelemetryReader</c>: top
    /// phrases, zero-result phrases, low-CTR phrases, and a forensic-ring
    /// recent-events tail. Synonym-mining starts here.
    /// </summary>
    public bool SearchLogs { get; set; } = true;

    /// <summary>
    /// Phase 4 Wave 5 — Synonym Coverage analyzer. Joins the saved synonym
    /// blobs against the search-log table to surface (a) unused synonym
    /// entries that never matched a logged query and (b) zero-result phrases
    /// that look like missing synonyms. Read-only.
    /// </summary>
    public bool SynonymCoverage { get; set; } = true;

    /// <summary>
    /// Phase 4 Wave 5 — Pinned Result Coverage audit. Read-only audit that
    /// surfaces unpublished/deleted pin targets, expired pins, low-CTR pins,
    /// pins with no recent search activity, and phrase overlap across
    /// collections.
    /// </summary>
    public bool PinnedCoverage { get; set; } = true;
}
