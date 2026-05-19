using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// One row in the tenant-global audit log — records every CRUD we send to
/// Graph for pinned items, pinned collections, and synonym rules.
/// </summary>
/// <remarks>
/// Polymorphic by <see cref="Kind"/>: the spine (When/Who/Action/Subject) is
/// shared; entity-specific context lives in optional fields that the kinds
/// that don't use them leave empty. One store avoids a 3-way merge when
/// rendering the unified changelog feeds.
/// </remarks>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_AuditLog")]
public class AuditLogEntry : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>UTC timestamp; populated by <see cref="AuditLogService.Append"/> if unset.</summary>
    [EPiServerDataIndex]
    public DateTime At { get; set; }

    /// <summary>
    /// Discriminator: <c>"PinnedItem"</c> | <c>"Collection"</c> | <c>"Synonym"</c>.
    /// Stored as a string so adding new kinds later doesn't require a DDS migration.
    /// </summary>
    [EPiServerDataIndex]
    public string Kind { get; set; } = string.Empty;

    /// <summary><c>"Created"</c> | <c>"Updated"</c> | <c>"Deleted"</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable target of the edit. Pinned item: phrase. Collection:
    /// collection key. Synonym: full rule body (e.g. <c>"test =&gt; alloy"</c>).
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>EPiServer user id of the editor.</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Display name of the editor at the time of the edit.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>
    /// Channel that owns this edit, when applicable. Empty for synonyms
    /// (tenant-global) and for collection events that aren't initiated from a
    /// channel-scoped flyout. Indexed so the Channels index's "last edited"
    /// hint still answers in O(log n).
    /// </summary>
    [EPiServerDataIndex]
    public string ChannelKey { get; set; } = string.Empty;

    /// <summary>SiteDefinition.Name; empty means "shared across sites" or "not applicable".</summary>
    public string Site { get; set; } = string.Empty;

    /// <summary>BCP-47 locale; empty means "locale-agnostic" or "not applicable".</summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Synonym slot name (typically <c>"one"</c>); empty for non-synonym kinds.
    /// Stored as a string for forward-compatibility with Graph's slot names.
    /// </summary>
    public string Slot { get; set; } = string.Empty;

    /// <summary>
    /// Collection key. Set for both pinned-item events (the owning collection)
    /// and collection events (the collection itself — same as <see cref="Subject"/>).
    /// </summary>
    public string CollectionKey { get; set; } = string.Empty;

    /// <summary>Optional free-form note (reserved for bulk-import labels, etc.).</summary>
    public string Note { get; set; } = string.Empty;
}
