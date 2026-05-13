using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

/// <summary>
/// Thin orchestration around <see cref="IGraphAdminClient"/> for the pinned-results tool.
/// Today the service is a pass-through; bug fixes or per-tenant logic land here.
/// </summary>
public sealed class PinnedService
{
    /// <summary>
    /// ContentGraph caps a single GET to <c>/api/pinned/collections/{id}/items</c>
    /// at 20 items regardless of any client-side limit; <see cref="LoadAllItemsAsync"/>
    /// walks pages of this size until it sees a short page.
    /// </summary>
    internal const int GraphPageSize = 20;

    /// <summary>
    /// Hard cap on the number of items <see cref="LoadAllItemsAsync"/> will
    /// return. A runaway tenant could otherwise exhaust memory walking
    /// hundreds of pages on every tab open; we'd rather fail honestly.
    /// </summary>
    internal const int BulkLoadSafetyCap = 5000;

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

    public Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken ct, int offset = 0)
        => _client.GetItemsAsync(collectionId, ct, offset);

    /// <summary>
    /// Reads every pinned item in a collection by walking ContentGraph's
    /// 20-item pages until a short page is returned. Throws
    /// <see cref="BulkLoadCapExceededException"/> when more than
    /// <see cref="BulkLoadSafetyCap"/> items would be loaded — the caller
    /// should map this to 503 so the marketer sees an honest error rather
    /// than a stalled page.
    /// </summary>
    public async Task<IReadOnlyList<PinnedItemResult>> LoadAllItemsAsync(string collectionId, CancellationToken ct)
    {
        var all = new List<PinnedItemResult>();
        var offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _client.GetItemsAsync(collectionId, ct, offset);
            if (page.Count == 0)
            {
                break;
            }
            all.AddRange(page);
            if (page.Count < GraphPageSize)
            {
                break;
            }
            offset += page.Count;
            if (all.Count > BulkLoadSafetyCap)
            {
                throw new BulkLoadCapExceededException(collectionId, BulkLoadSafetyCap);
            }
        }
        return all;
    }

    public Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken ct)
        => _client.CreateItemAsync(collectionId, payload, ct);

    public Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken ct)
        => _client.UpdateItemAsync(collectionId, id, payload, ct);

    public Task DeleteItemAsync(string collectionId, string id, CancellationToken ct)
        => _client.DeleteItemAsync(collectionId, id, ct);
}
