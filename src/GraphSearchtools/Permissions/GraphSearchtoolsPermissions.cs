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

    // Phase 3 — client-side ranking-curve preview tool.
    public static PermissionType DecaySandbox { get; } =
        new("GraphSearchtools", "DecaySandbox");

    // Phase 3 — token-count-tiered Semantic Weight Tuner policy editor.
    public static PermissionType SemanticTuner { get; } =
        new("GraphSearchtools", "SemanticTuner");

    // Phase 3 — Optimizely Graph webhook administration (list, create, delete).
    public static PermissionType Webhooks { get; } =
        new("GraphSearchtools", "Webhooks");

    // Phase 3 — recent Graph queries (timing + ranking + replay).
    public static PermissionType RequestLogs { get; } =
        new("GraphSearchtools", "RequestLogs");

    // Phase 4 foundation — push search-log telemetry from host search surfaces
    // into the GraphSearchtools log table (consumed by Phase 4 Wave 5 tools).
    public static PermissionType Telemetry { get; } =
        new("GraphSearchtools", "Telemetry");
}
