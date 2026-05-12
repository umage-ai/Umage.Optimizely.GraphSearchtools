using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// Tier-3 abstraction over the Optimizely Graph admin REST API and the GraphQL
/// content endpoint. Lives in <c>Abstractions/</c> so the CMS-13 SDK can be
/// swapped in later without touching tool services.
/// </summary>
public interface IGraphAdminClient
{
    Task<IReadOnlyList<PinnedCollectionResult>> GetCollectionsAsync(CancellationToken cancellationToken);
    Task<PinnedCollectionResult> CreateCollectionAsync(PinnedCollectionPayload payload, CancellationToken cancellationToken);
    Task<PinnedCollectionResult> UpdateCollectionAsync(string collectionId, PinnedCollectionUpdatePayload payload, CancellationToken cancellationToken);
    Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken cancellationToken);
    Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken cancellationToken);
    Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken cancellationToken);
    Task DeleteItemAsync(string collectionId, string id, CancellationToken cancellationToken);

    Task<string> GetSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken);
    Task UpdateSynonymsAsync(SynonymsRequest request, CancellationToken cancellationToken);
    Task DeleteSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentSearchHit>> SearchContentAsync(string query, string? locale, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentSearchHit>> ResolveByGuidsAsync(IReadOnlyList<string> guids, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="limit"/> autocomplete suggestions for
    /// <paramref name="value"/>, drawn from <c>{typeName}.autocomplete.{field}</c>.
    /// The available types and fields are tenant-specific because they depend
    /// on what the host marked <c>Searchable</c> in the CMS content model;
    /// use <see cref="GetAutocompleteSchemaAsync"/> to discover them.
    /// </summary>
    Task<IReadOnlyList<string>> AutocompleteAsync(string typeName, string field, string value, string? locale, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Discovers the root content types that expose an <c>autocomplete</c>
    /// field, plus the scalar string fields supported under each type's
    /// autocomplete. Object-typed sub-fields (e.g. <c>ContentLink</c>,
    /// <c>Language</c>) are excluded — only fields that accept
    /// <c>(value, limit)</c> directly are returned.
    /// </summary>
    Task<IReadOnlyList<AutocompleteFieldDescriptor>> GetAutocompleteSchemaAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the locale codes known to Optimizely Graph by introspecting the
    /// <c>Locales</c> enum in the schema. The enum is generated from registered
    /// languages and reflects what Graph itself can serve — not what the host
    /// CMS has configured. Returns an empty list if the enum is missing.
    /// </summary>
    Task<IReadOnlyList<string>> GetGraphLocalesAsync(CancellationToken cancellationToken);
}

public sealed record AutocompleteFieldDescriptor(string TypeName, IReadOnlyList<string> Fields);
