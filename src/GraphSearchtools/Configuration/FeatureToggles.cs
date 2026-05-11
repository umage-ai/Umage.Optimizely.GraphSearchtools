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
    public bool SemanticTuner { get; set; } = true;
    public bool Webhooks { get; set; } = true;

    /// <summary>
    /// Phase 4 foundation — gates the telemetry ingest endpoints
    /// (<c>POST /TelemetryApi/SearchLog</c>, <c>POST /TelemetryApi/SearchLogBatch</c>).
    /// Phase 4 Wave 5 analytics tools (Search Logs UI, Pinned Result Coverage,
    /// Synonym Coverage) consume the data this captures.
    /// </summary>
    public bool Telemetry { get; set; } = true;

    /// <summary>
    /// Phase 4 Wave 5 — Search Logs UI. Read-only surface over the
    /// <see cref="UmageAI.Optimizely.GraphSearchTools.Services.SearchLogService"/>
    /// table: top phrases, zero-result phrases, low-CTR phrases, and a
    /// recent-events live tail. Synonym-mining starts here.
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

    /// <summary>
    /// Phase 4 Wave 5 — local CMS scan that flags pages with empty <c>Name</c>,
    /// missing <c>MainBody</c>-style body fields, no <c>Tags</c>, and string
    /// properties exceeding Optimizely Graph's 1024-character sortable-field
    /// limit (per <c>optimizely-graph-site-search.md</c> §3 caveat). Scan is
    /// on-demand only because it walks every published page under every site.
    /// </summary>
    public bool ContentSearchabilityAudit { get; set; } = true;

    /// <summary>
    /// Phase 5 — Relevancy Lab. CRUD for golden query sets, run engine
    /// (NDCG@10 + MRR scoring), DDS-persisted run history, two-config
    /// side-by-side comparison, and CSV export.
    /// </summary>
    public bool RelevancyLab { get; set; } = true;
}
