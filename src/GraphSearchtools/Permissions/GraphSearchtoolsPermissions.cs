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

    public static PermissionType SavedQueries { get; } =
        new("GraphSearchtools", "SavedQueries");

    // Phase 2.5 — Search Profiles top-level surface.
    public static PermissionType Profiles { get; } =
        new("GraphSearchtools", "Profiles");
}
