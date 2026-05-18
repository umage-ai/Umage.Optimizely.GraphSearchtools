using System.Text.Json.Serialization;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.Models;

/// <summary>
/// Status displayed on the index table per design §4.7. The heuristic is
/// intentionally simple in v1; richer derivations land with Phase 4 telemetry.
/// </summary>
public enum ProfileStatus
{
    /// <summary>Pinned/synonym data exists and the GraphQL document is wired up.</summary>
    Tuned = 0,
    /// <summary>Pinned data needs marketer attention (e.g. flagged in audit log).</summary>
    NeedsReview = 1,
    /// <summary>Profile declares a GraphQL document path that doesn't resolve on disk.</summary>
    DocMissing = 2,
    /// <summary>No edits recorded for this profile yet.</summary>
    Cold = 4
}

/// <summary>
/// Row shape for the Profiles index table. Keep flat — the JS table builder
/// renders one row per summary without any joins.
/// </summary>
public sealed record ProfileSummary
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? DescriptionResolved { get; init; }

    public IReadOnlyList<string> Sites { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Locales { get; init; } = Array.Empty<string>();

    /// <summary>True iff the profile declares a GraphQL document path.</summary>
    public bool HasGraphQLDoc { get; init; }
    public string? GraphQLDocPath { get; init; }

    public double SemanticWeight { get; init; }
    public string RankingName { get; init; } = nameof(GraphRanking.Relevance);

    public ProfileStatus Status { get; init; }

    public DateTime? LastEditedAt { get; init; }
    public string? LastEditedBy { get; init; }
}

/// <summary>
/// Detail-page shape — superset of <see cref="ProfileSummary"/> plus a few
/// derived bits (sample resolved pinned key, recent edits, computed counts).
/// Counts that require live Graph calls are zero in v1 — see notes in
/// <see cref="ProfilesService"/>.
/// </summary>
public sealed record ProfileDetail
{
    public ProfileSummary Summary { get; init; } = new();

    public IReadOnlyList<string> SearchedFields { get; init; } = Array.Empty<string>();

    /// <summary>Sample resolved pinned key (first locale × shared site).</summary>
    public string? PinnedKeySample { get; init; }

    /// <summary>True when the pinned-key formula doesn't include the site name.</summary>
    public bool PinnedKeyIsSiteShared { get; init; }

    public int PinnedPhraseCount { get; init; }
    public int SynonymEntryCount { get; init; }

    public IReadOnlyList<ProfileEditDto> RecentEdits { get; init; } = Array.Empty<ProfileEditDto>();
}

/// <summary>JSON-friendly projection of <see cref="SearchProfileEdit"/>.</summary>
public sealed record ProfileEditDto
{
    public string ProfileKey { get; init; } = string.Empty;
    public string? Site { get; init; }
    public string? Locale { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? ActorId { get; init; }
    public string? ActorName { get; init; }
    public DateTime At { get; init; }
    public string? Note { get; init; }

    public static ProfileEditDto From(SearchProfileEdit e) => new()
    {
        ProfileKey = e.ProfileKey,
        Site = e.Site,
        Locale = e.Locale,
        Kind = e.Kind,
        Action = e.Action,
        Subject = e.Subject,
        ActorId = e.ActorId,
        ActorName = e.ActorName,
        At = e.At,
        Note = e.Note
    };
}

/// <summary>
/// Pinned-tab payload for the Profile detail page. Returned by
/// <c>GET /api/profiles/{key}/pinned?site=&amp;locale=</c>. Includes the
/// resolved <see cref="PinnedKey"/> so the UI can show the read-only "Pinned
/// key:" line, and <see cref="CollectionId"/> so the JS can pass it back to
/// the existing <c>/PinnedApi/CreateItem</c> endpoint without re-resolving.
/// </summary>
public sealed record ProfilePinnedResponse
{
    public string ProfileKey { get; init; } = string.Empty;
    public string? Site { get; init; }
    public string? Locale { get; init; }

    /// <summary>Resolved Graph collection key, e.g. <c>"site-en"</c>.</summary>
    public string? PinnedKey { get; init; }

    /// <summary>Graph collection id matching <see cref="PinnedKey"/>, or <c>null</c> when no collection exists yet.</summary>
    public string? CollectionId { get; init; }

    public IReadOnlyList<ProfilePinnedRow> Rows { get; init; } = Array.Empty<ProfilePinnedRow>();
}

/// <summary>
/// Flat row shape per pinned item, decorated with the parent collection's key
/// and id so the JS can route writes back through <c>PinnedApi/{Update,Delete}Item</c>.
/// </summary>
public sealed record ProfilePinnedRow
{
    public string Id { get; init; } = string.Empty;
    public string CollectionId { get; init; } = string.Empty;
    public string CollectionKey { get; init; } = string.Empty;
    public string Phrases { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public string? Language { get; init; }
    public double Priority { get; init; }
    public bool IsActive { get; init; }

    public static ProfilePinnedRow From(PinnedCollectionResult col, PinnedItemResult item) => new()
    {
        Id = item.Id,
        CollectionId = item.CollectionId is { Length: > 0 } cid ? cid : col.Id,
        CollectionKey = col.Key,
        Phrases = item.Phrases,
        TargetKey = item.TargetKey,
        Language = item.Language,
        Priority = item.Priority,
        IsActive = item.IsActive
    };
}

/// <summary>
/// Razor view model for <c>Views/Profiles/Detail.cshtml</c>. We pre-resolve
/// localized strings here rather than in the view so the markup stays
/// declarative.
/// </summary>
public sealed class ProfileDetailViewModel
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DescriptionResolved { get; set; }
    public bool IsSiteShared { get; set; }
    public IReadOnlyList<string> Sites { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Locales { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchedFields { get; set; } = Array.Empty<string>();
    public string? PinnedKeyFormula { get; set; }
    public string RankingName { get; set; } = nameof(GraphRanking.Relevance);
    public double SemanticWeight { get; set; }
    public string? GraphQLDocPath { get; set; }
    public bool GraphQLDocExists { get; set; }

    /// <summary>
    /// The GraphQL document body, when available — either inline content
    /// supplied via <see cref="SearchProfileBuilder.GraphQLDocumentInline"/> or
    /// the file at <see cref="GraphQLDocPath"/>. Null when neither source
    /// resolves.
    /// </summary>
    public string? GraphQLDocContent { get; set; }

    /// <summary>
    /// True when the document was registered inline (via
    /// <c>GraphQLDocumentInline</c>) rather than read from disk. Lets the view
    /// label the source so admins know the query lives in code.
    /// </summary>
    public bool GraphQLDocIsInline { get; set; }

    /// <summary>
    /// True when the registered GraphQL document references Graph's
    /// <c>usePinned</c> directive — i.e. pinned-results edits saved via this
    /// profile will actually surface in the storefront SERP. False means the
    /// pinned editor is informational only and the storefront query needs to
    /// be updated before edits take effect.
    /// </summary>
    public bool QueryAppliesPinned { get; set; }

    /// <summary>
    /// True when the registered GraphQL document opts in to synonym
    /// substitution by passing <c>synonyms: ONE</c> (or <c>TWO</c>) inside
    /// <c>_fulltext</c>. False means rules saved via the synonyms editor will
    /// be stored in Graph but not applied at storefront query time.
    /// </summary>
    public bool QueryAppliesSynonyms { get; set; }
}
