using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// Per-permission marker recording that <c>PermissionSeeder</c> has already
/// granted (or considered) a given permission to the configured
/// <c>AuthorizedRoles</c>. Stored in DynamicDataStore so the marker survives
/// restarts and the seeder remains a no-op after the first run.
/// </summary>
/// <remarks>
/// One row per <see cref="EPiServer.Security.PermissionType.Name"/>. We key on
/// the permission name (not the (group, name) tuple) because every permission
/// this addon defines lives under the <c>GraphSearchtools</c> group, and the
/// name alone is unique within it. When a future addon version adds a new
/// permission, its marker won't exist on upgraded installs — the seeder will
/// pick it up on the next boot without re-touching the existing grants.
/// </remarks>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_PermissionSeeded")]
public class PermissionSeederMarker : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>The <see cref="EPiServer.Security.PermissionType.Name"/> that was seeded.</summary>
    [EPiServerDataIndex]
    public string PermissionName { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the seeding attempt.</summary>
    public DateTime SeededAt { get; set; }

    /// <summary>
    /// <c>true</c> when the seeder actually wrote grants (no prior grants found).
    /// <c>false</c> when grants already existed and the seeder left them alone.
    /// Both cases set the marker so subsequent boots don't re-evaluate.
    /// </summary>
    public bool DidGrant { get; set; }
}
