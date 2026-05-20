using EPiServer.Security;

namespace UmageAI.Optimizely.GraphSearchTools.Permissions;

/// <summary>
/// EPiServer permission types for Graph Search Tools, registered as functions
/// under <c>Set Access Rights</c>. The granularity reflects two distinct
/// authority boundaries:
///
/// <list type="bullet">
///   <item><description><b>View</b> permissions gate the tool's read surface
///   (lists, detail views, audits). The Insights permission covers all
///   read-only analytics surfaces (Insights dashboard, search logs, pinned
///   coverage, synonym coverage) — one grant lets a marketer "look around".</description></item>
///   <item><description><b>Edit</b> permissions gate mutating endpoints
///   (POST/PUT/DELETE). Splitting view from edit lets a host grant
///   read-only access to one role and write to another.</description></item>
/// </list>
///
/// The Pinned tool has a further split: <see cref="Pinned"/> + <see cref="PinnedEdit"/>
/// govern items inside a collection, while <see cref="Collections"/> governs
/// the collection shells themselves (create / rename / delete). A marketer
/// curating pins for an existing campaign can hold PinnedEdit without being
/// able to provision or retire collections.
///
/// Tool-level on/off lives in <c>UmageAI:GraphSearchTools:Features</c>
/// (appsettings) — feature toggles decide *whether* a tool is wired,
/// permissions decide *who* among enabled tools.
/// </summary>
[PermissionTypes]
public static class GraphSearchtoolsPermissions
{
    /// <summary>View the Search Channels list and channel-detail tabs.</summary>
    public static PermissionType Channels { get; } =
        new("GraphSearchtools", "Channels");

    /// <summary>
    /// View all read-only analytics: the Insights dashboard, the per-channel
    /// Insights tab, Pinned Coverage and Synonym Coverage audits. Folds the
    /// former SearchLogs / Telemetry / *Coverage permissions into one grant.
    /// </summary>
    public static PermissionType Insights { get; } =
        new("GraphSearchtools", "Insights");

    /// <summary>View pinned collections and their items.</summary>
    public static PermissionType Pinned { get; } =
        new("GraphSearchtools", "Pinned");

    /// <summary>
    /// Add, modify, reorder and delete pinned items inside a collection.
    /// Does NOT cover collection-shell operations — see <see cref="Collections"/>.
    /// </summary>
    public static PermissionType PinnedEdit { get; } =
        new("GraphSearchtools", "PinnedEdit");

    /// <summary>
    /// Create, rename and delete pinned collections. Granted to operators
    /// who provision new search campaigns; day-to-day pin curation only
    /// needs <see cref="PinnedEdit"/>.
    /// </summary>
    public static PermissionType Collections { get; } =
        new("GraphSearchtools", "Collections");

    /// <summary>View synonym rules.</summary>
    public static PermissionType Synonyms { get; } =
        new("GraphSearchtools", "Synonyms");

    /// <summary>Add, modify and delete synonym rules.</summary>
    public static PermissionType SynonymsEdit { get; } =
        new("GraphSearchtools", "SynonymsEdit");
}
