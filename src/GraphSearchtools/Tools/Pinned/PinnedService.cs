using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

/// <summary>
/// Thin orchestration around <see cref="IGraphAdminClient"/> for the pinned-results tool.
/// Today the service is a pass-through; bug fixes or per-tenant logic land here.
/// </summary>
public sealed class PinnedService
{
    private readonly IGraphAdminClient _client;

    public PinnedService(IGraphAdminClient client)
    {
        _client = client;
    }

    public Task<IReadOnlyList<PinnedCollectionResult>> GetCollectionsAsync(CancellationToken ct)
        => _client.GetCollectionsAsync(ct);

    public Task<PinnedCollectionResult> CreateCollectionAsync(PinnedCollectionPayload payload, CancellationToken ct)
        => _client.CreateCollectionAsync(payload, ct);

    public Task<PinnedCollectionResult> UpdateCollectionAsync(string collectionId, PinnedCollectionUpdatePayload payload, CancellationToken ct)
        => _client.UpdateCollectionAsync(collectionId, payload, ct);

    public Task DeleteCollectionAsync(string collectionId, CancellationToken ct)
        => _client.DeleteCollectionAsync(collectionId, ct);

    public Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken ct)
        => _client.GetItemsAsync(collectionId, ct);

    public Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken ct)
        => _client.CreateItemAsync(collectionId, payload, ct);

    public Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken ct)
        => _client.UpdateItemAsync(collectionId, id, payload, ct);

    public Task DeleteItemAsync(string collectionId, string id, CancellationToken ct)
        => _client.DeleteItemAsync(collectionId, id, ct);
}
