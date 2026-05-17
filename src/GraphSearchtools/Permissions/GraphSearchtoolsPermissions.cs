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

    // Aurora refactor — Insights dashboard. Read-only over the same telemetry
    // / synonym / audit data the other tools already touch.
    public static PermissionType Insights { get; } =
        new("GraphSearchtools", "Insights");

    // Phase 4 foundation — push search-log telemetry from host search surfaces
    // into the GraphSearchtools log table (consumed by Phase 4 Wave 5 tools).
    public static PermissionType Telemetry { get; } =
        new("GraphSearchtools", "Telemetry");

    // Gates the internal /SearchLogsApi read endpoints — now consumed only by
    // the Profile Detail Insights tab. No standalone Search Logs UI ships any
    // more.
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
}
