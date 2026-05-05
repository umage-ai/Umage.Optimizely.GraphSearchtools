using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// DDS row recording one edit to a profile-scoped pinned collection or synonym
/// slot. Two surfaces consume this table:
/// <list type="bullet">
///   <item>The Audit Log tab on a profile's detail page (chronological list).</item>
///   <item>The "Last edited" column on the Profiles index (latest row per profile).</item>
/// </list>
/// </summary>
/// <remarks>
/// Persisted via the same <c>DynamicDataStoreFactory</c> pattern used by the
/// Saved Queries store. Keyed by the design doc's
/// <c>(profileKey, site, locale, timestamp)</c> tuple — site / locale are
/// indexed for fast per-context filtering.
/// </remarks>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SearchProfileEdits")]
public class SearchProfileEdit : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>Stable profile key matching <see cref="Configuration.SearchProfile.Key"/>.</summary>
    [EPiServerDataIndex]
    public string ProfileKey { get; set; } = string.Empty;

    /// <summary><c>SiteDefinition.Name</c>; empty string means "shared across sites".</summary>
    [EPiServerDataIndex]
    public string Site { get; set; } = string.Empty;

    /// <summary>BCP-47 lowercase locale; empty string means "locale-agnostic".</summary>
    [EPiServerDataIndex]
    public string Locale { get; set; } = string.Empty;

    /// <summary><c>"Pinned"</c> | <c>"Synonym"</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary><c>"Created"</c> | <c>"Updated"</c> | <c>"Deleted"</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Phrase (Pinned) or slot key (Synonym) the edit targets.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>EPiServer user id of the editor (e.g. <c>String</c> form of <c>SecurityEntity</c>).</summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>Display name of the editor at the time of the edit.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>Timestamp of the edit, UTC.</summary>
    public DateTime At { get; set; }

    /// <summary>Optional free-form note (reserved for future surface — e.g. bulk-import labels).</summary>
    public string Note { get; set; } = string.Empty;
}
