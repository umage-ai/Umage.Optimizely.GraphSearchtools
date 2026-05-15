using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// The Aurora Pinned grid relies on <see cref="PinnedService.LoadAllItemsAsync"/>
/// returning the full collection. ContentGraph caps a single GET at 20 items,
/// so the bulk walker has to be both correct (no double-counting,
/// short-page terminates) and safe (the 5000-item cap turns into a 503 rather
/// than an unbounded loop).
/// </summary>
public class PinnedServiceLoadAllItemsTests
{
    [Fact]
    public async Task LoadAllItemsAsync_SinglePage_TerminatesWithoutSecondCall()
    {
        var fake = new RecordingGraphAdmin(totalItems: 5);
        var service = new PinnedService(fake);

        var items = await service.LoadAllItemsAsync("collection", CancellationToken.None);

        items.Should().HaveCount(5);
        fake.Calls.Should().Equal(0);
    }

    [Fact]
    public async Task LoadAllItemsAsync_ExactlyOneFullPage_RequestsSecondPageThenStops()
    {
        // Upstream returning 20 looks identical to "page is full"; the only way
        // to know it's the last page is to ask again and get an empty page.
        var fake = new RecordingGraphAdmin(totalItems: 20);
        var service = new PinnedService(fake);

        var items = await service.LoadAllItemsAsync("collection", CancellationToken.None);

        items.Should().HaveCount(20);
        fake.Calls.Should().Equal(0, 20);
    }

    [Fact]
    public async Task LoadAllItemsAsync_WalksMultiplePages_ConcatenatesInOrder()
    {
        var fake = new RecordingGraphAdmin(totalItems: 47);
        var service = new PinnedService(fake);

        var items = await service.LoadAllItemsAsync("collection", CancellationToken.None);

        items.Should().HaveCount(47);
        fake.Calls.Should().Equal(0, 20, 40);
        // Order must be preserved end-to-end; the marketer's priority ordering
        // is meaningful in the UI.
        items.Select(i => i.Id).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task LoadAllItemsAsync_TerminatesOnEmptyPageWithoutItems()
    {
        // 60 items = three full pages of 20 each. The walker must terminate on
        // the *next* empty page (offset 60) rather than spin.
        var fake = new RecordingGraphAdmin(totalItems: 60);
        var service = new PinnedService(fake);

        var items = await service.LoadAllItemsAsync("collection", CancellationToken.None);

        items.Should().HaveCount(60);
        fake.Calls.Should().Equal(0, 20, 40, 60);
    }

    [Fact]
    public async Task LoadAllItemsAsync_ThrowsBulkCapExceeded_WhenCollectionExceedsSafetyCap()
    {
        // 5021 = cap + one full page beyond. The walker should throw rather
        // than complete; the controller maps the exception to 503.
        var fake = new RecordingGraphAdmin(totalItems: PinnedService.BulkLoadSafetyCap + 21);
        var service = new PinnedService(fake);

        var act = () => service.LoadAllItemsAsync("collection", CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BulkLoadCapExceededException>();
        ex.Which.CollectionId.Should().Be("collection");
        ex.Which.Cap.Should().Be(PinnedService.BulkLoadSafetyCap);
    }

    /// <summary>
    /// Stand-in for <see cref="IGraphAdminClient"/> that returns a known
    /// number of items, paged at 20 per <c>GetItemsAsync</c> call, and
    /// records the offsets the walker actually requested.
    /// </summary>
    private sealed class RecordingGraphAdmin : IGraphAdminClient
    {
        private readonly int _totalItems;
        public List<int> Calls { get; } = new();

        public RecordingGraphAdmin(int totalItems)
        {
            _totalItems = totalItems;
        }

        public Task<IReadOnlyList<PinnedItemResult>> GetItemsAsync(string collectionId, CancellationToken cancellationToken, int offset = 0)
        {
            Calls.Add(offset);
            const int pageSize = 20;
            var remaining = Math.Max(0, _totalItems - offset);
            var pageCount = Math.Min(pageSize, remaining);
            var page = new List<PinnedItemResult>(pageCount);
            for (int i = 0; i < pageCount; i++)
            {
                var idx = offset + i;
                page.Add(new PinnedItemResult { Id = $"item-{idx:D6}" });
            }
            return Task.FromResult<IReadOnlyList<PinnedItemResult>>(page);
        }

        // ── Unused methods (interface contract) ─────────────────────────────
        public Task<IReadOnlyList<PinnedCollectionResult>> GetCollectionsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PinnedCollectionResult> CreateCollectionAsync(PinnedCollectionPayload payload, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PinnedCollectionResult> UpdateCollectionAsync(string collectionId, PinnedCollectionUpdatePayload payload, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PinnedItemResult> CreateItemAsync(string collectionId, PinnedItemPayload payload, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<PinnedItemResult> UpdateItemAsync(string collectionId, string id, PinnedItemPayload payload, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteItemAsync(string collectionId, string id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string> GetSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateSynonymsAsync(SynonymsRequest request, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DeleteSynonymsAsync(SynonymsQuery query, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContentSearchHit>> SearchContentAsync(string query, string? locale, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ContentSearchHit>> ResolveByGuidsAsync(IReadOnlyList<string> guids, IReadOnlyList<string> contentTypes, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> AutocompleteAsync(string typeName, string field, string value, string? locale, int limit, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<AutocompleteFieldDescriptor>> GetAutocompleteSchemaAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetGraphLocalesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
