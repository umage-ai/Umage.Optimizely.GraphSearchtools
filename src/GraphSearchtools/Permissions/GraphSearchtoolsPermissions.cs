using EPiServer.Security;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// Defines EPiServer permission types for each tool.
/// Registered as "functions" in the CMS admin UI under Set Access Rights.
/// When GraphSearchtoolsOptions.CheckPermissionForEachFeature is true,
/// each tool checks its corresponding permission in addition to the authorization policy.
/// </summary>
[PermissionTypes]
public static class GraphSearchtoolsPermissions
{
    public static PermissionType Overview { get; } =
        new("GraphSearchtools", "Overview");

    public static PermissionType Pinned { get; } =
        new("GraphSearchtools", "Pinned");

    public static PermissionType Synonyms { get; } =
        new("GraphSearchtools", "Synonyms");

    public static PermissionType Health { get; } =
        new("GraphSearchtools", "Health");

    public static PermissionType Autocomplete { get; } =
        new("GraphSearchtools", "Autocomplete");

    // Phase 2.5 — Search Profiles top-level surface.
    public static PermissionType Profiles { get; } =
        new("GraphSearchtools", "Profiles");

    // Phase 3 — token-count-tiered Semantic Weight Tuner policy editor.
    public static PermissionType SemanticTuner { get; } =
        new("GraphSearchtools", "SemanticTuner");

    // Phase 3 — Optimizely Graph webhook administration (list, create, delete).
    public static PermissionType Webhooks { get; } =
        new("GraphSearchtools", "Webhooks");

    // Phase 4 foundation — push search-log telemetry from host search surfaces
    // into the GraphSearchtools log table (consumed by Phase 4 Wave 5 tools).
    public static PermissionType Telemetry { get; } =
        new("GraphSearchtools", "Telemetry");

    // Phase 4 Wave 5 — Search Logs UI (top phrases, zero-result phrases,
    // low-CTR phrases, recent raw events). Read-only over the search log table.
    public static PermissionType SearchLogs { get; } =
        new("GraphSearchtools", "SearchLogs");

    // Phase 4 Wave 5 — Synonym Coverage analyzer. Joins saved synonym blobs
    // with the search-log table to surface unused entries and zero-result
    // phrases that look like missing synonyms.
    public static PermissionType SynonymCoverage { get; } =
        new("GraphSearchtools", "SynonymCoverage");

    // Phase 4 Wave 5 — Pinned Result Coverage audit. Read-only audit that
    // joins Graph pinned data with CMS content state and the search-log table
    // to surface broken / stale / overlapping pins.
    public static PermissionType PinnedCoverage { get; } =
        new("GraphSearchtools", "PinnedCoverage");

    // Phase 4 Wave 5 — Content Searchability Audit. Local CMS scan flagging
    // pages with empty Name, missing MainBody-style body fields, no Tags, and
    // string fields exceeding Graph's 1024-char sortable-field limit.
    public static PermissionType ContentSearchabilityAudit { get; } =
        new("GraphSearchtools", "ContentSearchabilityAudit");

    // Phase 5 — Relevancy Lab (golden query sets, NDCG@10 + MRR scoring,
    // run history, two-config comparison, CSV export).
    public static PermissionType RelevancyLab { get; } =
        new("GraphSearchtools", "RelevancyLab");
}
