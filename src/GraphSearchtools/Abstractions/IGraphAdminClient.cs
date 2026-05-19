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

    /// <summary>
    /// Reads one page of pinned items from a collection. ContentGraph caps a
    /// page at 20 items regardless of any client-side limit; pass
    /// <paramref name="offset"/> to walk past the cap. The default 0 reads
    /// from the start.
    /// </summary>
    Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken cancellationToken, int offset = 0);
    Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken cancellationToken);
    Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken cancellationToken);
    Task DeleteItemAsync(string collectionId, string id, CancellationToken cancellationToken);

    Task<string> GetSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken);
    Task UpdateSynonymsAsync(SynonymsRequest request, CancellationToken cancellationToken);
    Task DeleteSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentSearchHit>> SearchContentAsync(string query, string? locale, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken);
    Task<IReadOnlyList<ContentSearchHit>> ResolveByGuidsAsync(IReadOnlyList<string> guids, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the locale codes known to Optimizely Graph by introspecting the
    /// <c>Locales</c> enum in the schema. The enum is generated from registered
    /// languages and reflects what Graph itself can serve — not what the host
    /// CMS has configured. Returns an empty list if the enum is missing.
    /// </summary>
    Task<IReadOnlyList<string>> GetGraphLocalesAsync(CancellationToken cancellationToken);
}
