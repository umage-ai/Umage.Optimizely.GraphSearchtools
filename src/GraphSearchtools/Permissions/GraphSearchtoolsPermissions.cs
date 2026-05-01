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
}
