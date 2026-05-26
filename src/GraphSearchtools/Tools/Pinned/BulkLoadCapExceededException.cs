namespace UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

/// <summary>
/// Thrown by <see cref="PinnedService.LoadAllItemsAsync"/> when a collection
/// is larger than <see cref="PinnedService.BulkLoadSafetyCap"/>. Controllers
/// should map this to 503 with a message that points the operator at the
/// per-collection page rather than letting the full Pinned grid load forever.
/// </summary>
internal sealed class BulkLoadCapExceededException : Exception
{
    public BulkLoadCapExceededException(string collectionId, int cap)
        : base($"Collection '{collectionId}' exceeds the bulk-load safety cap of {cap} items.")
    {
        CollectionId = collectionId;
        Cap = cap;
    }

    public string CollectionId { get; }
    public int Cap { get; }
}
