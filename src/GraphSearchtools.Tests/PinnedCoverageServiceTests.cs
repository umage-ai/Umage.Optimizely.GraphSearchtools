using EPiServer;
using EPiServer.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 §6 — guards each issue kind <see cref="PinnedCoverageService"/>
/// can surface, plus the overlap-detection contract.
/// </summary>
/// <remarks>
/// The service stitches together four collaborators (graph client, content
/// loader, profile registry, search log service). Every test sets up a fake
/// for each so we can pin the audit's behaviour to one moving part at a time.
/// We use the same in-memory <c>SearchLogService</c> idiom as
/// <c>SearchLogServiceTests</c> — one fewer mock, real aggregation logic.
/// </remarks>
public class PinnedCoverageServiceTests
{
    [Fact]
    public async Task Audit_FlagsDeleted_When_TargetGuid_NotInCms()
    {
        var (service, graph, content, _) = NewService();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("warranty", targetKey: Guid.NewGuid().ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        // No content registered → TryGet returns false → "Deleted".

        var result = await service.RunAuditAsync(CancellationToken.None);

        result.Issues.Should().ContainSingle(i => i.Kind == "Deleted")
            .Which.Phrase.Should().Be("warranty");
    }

    [Fact]
    public async Task Audit_FlagsUnpublished_When_ContentExists_ButStatus_NotPublished()
    {
        var (service, graph, content, _) = NewService();
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("warranty", targetKey: guid.ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        RegisterContent(content, guid, "Warranty page", VersionStatus.CheckedOut);

        var result = await service.RunAuditAsync(CancellationToken.None);

        var issue = result.Issues.Should().ContainSingle(i => i.Kind == "Unpublished").Subject;
        issue.TargetName.Should().Be("Warranty page");
        issue.CollectionKey.Should().Be("site-search-en");
    }

    [Fact]
    public async Task Audit_FlagsExpired_When_EffectiveTo_InThePast()
    {
        var (service, graph, content, _) = NewService();
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("shipping", targetKey: guid.ToString()) with
        {
            EffectiveTo = DateTime.UtcNow.AddDays(-3)
        };
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        RegisterContent(content, guid, "Shipping page", VersionStatus.Published);

        var result = await service.RunAuditAsync(CancellationToken.None);

        result.Issues.Should().ContainSingle(i => i.Kind == "Expired");
    }

    [Fact]
    public async Task Audit_FlagsLowCtr_When_PinShown_ButRarelyClicked()
    {
        // 6 sessions for the phrase, only 1 of them clicked the pinned target →
        // CTR 1/6 ≈ 0.166. Threshold is 5% so... that's >5%. Use 0/6 instead so
        // it strictly trips the threshold.
        var (service, graph, content, logs) = NewService();
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("warranty", targetKey: guid.ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        RegisterContent(content, guid, "Warranty page", VersionStatus.Published);

        // 6 sessions, all served the same target as top result, none clicked.
        for (var i = 0; i < 6; i++)
        {
            logs.Append(new SearchLogEntry
            {
                Phrase = "warranty",
                TopResultId = guid.ToString(),
                TopResultRank = null,
                ResultCount = 1,
                At = DateTime.UtcNow.AddHours(-1)
            });
        }

        var result = await service.RunAuditAsync(CancellationToken.None);

        var lowCtr = result.Issues.Should().ContainSingle(i => i.Kind == "LowCtr").Subject;
        lowCtr.Detail.Should().Contain("0 / 6");
    }

    [Fact]
    public async Task Audit_FlagsNoActivity_When_LogsExist_ButPhraseNeverShows()
    {
        // Logs are populated (so we DO have ingestion data) but none mention
        // this pin's phrase → "NoActivity" — the most actionable pruning hint.
        var (service, graph, content, logs) = NewService();
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("rare-phrase", targetKey: guid.ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        RegisterContent(content, guid, "Rare page", VersionStatus.Published);

        logs.Append(new SearchLogEntry { Phrase = "shipping", ResultCount = 5, At = DateTime.UtcNow.AddHours(-1) });

        var result = await service.RunAuditAsync(CancellationToken.None);

        result.Issues.Should().ContainSingle(i => i.Kind == "NoActivity");
    }

    [Fact]
    public async Task Audit_DoesNotFlagNoActivity_When_NoLogs_AtAll()
    {
        // Empty log table — most installs in v1 won't have wired up the host
        // SDK yet. Surfacing "no activity" against every pin would be useless,
        // so the audit suppresses the whole class when the window is empty.
        var (service, graph, content, _) = NewService();
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("warranty", targetKey: guid.ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        RegisterContent(content, guid, "Warranty page", VersionStatus.Published);

        var result = await service.RunAuditAsync(CancellationToken.None);

        result.Issues.Should().NotContain(i => i.Kind == "NoActivity");
    }

    [Fact]
    public async Task Audit_DetectsOverlap_When_Phrase_PinnedInTwoCollections()
    {
        var (service, graph, content, _) = NewService();
        var col1 = NewCollection("col-1", "site-search-en");
        var col2 = NewCollection("col-2", "kb-search-en");
        var guid = Guid.NewGuid();
        var item1 = NewItem("warranty", targetKey: guid.ToString());
        var item2 = NewItem("warranty", targetKey: guid.ToString());

        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { col1, col2 });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item1 });
        graph.Setup(g => g.GetItemsAsync("col-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item2 });
        RegisterContent(content, guid, "Warranty page", VersionStatus.Published);

        var result = await service.RunAuditAsync(CancellationToken.None);

        var overlap = result.Overlaps.Should().ContainSingle().Subject;
        overlap.Phrase.Should().Be("warranty");
        overlap.Collections.Should().BeEquivalentTo(new[] { "site-search-en", "kb-search-en" });
    }

    [Fact]
    public async Task Audit_PopulatesProfileKey_From_ProfileRegistry_PinnedKey_Lookup()
    {
        var profile = new SearchProfile
        {
            Key = "site-search",
            DisplayName = LocalizedString.Literal("Site Search"),
            Locales = new[] { "en" },
            PinnedKeyForLocale = locale => "site-search-" + locale
        };
        var registry = new StaticRegistry(profile, BuildGeneric());

        var (service, graph, content, _) = NewService(registry);
        var guid = Guid.NewGuid();
        var collection = NewCollection("col-1", "site-search-en");
        var item = NewItem("warranty", targetKey: guid.ToString());
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { collection });
        graph.Setup(g => g.GetItemsAsync("col-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { item });
        // Content not registered → "Deleted" issue, with profileKey set to "site-search".

        var result = await service.RunAuditAsync(CancellationToken.None);

        result.Issues.Should().ContainSingle()
            .Which.ProfileKey.Should().Be("site-search");
    }

    [Fact]
    public async Task Audit_GeneratedAt_IsUtcNow()
    {
        var (service, graph, _, _) = NewService();
        graph.Setup(g => g.GetCollectionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedCollectionResult>());

        var before = DateTime.UtcNow;
        var result = await service.RunAuditAsync(CancellationToken.None);
        var after = DateTime.UtcNow;

        result.GeneratedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.Issues.Should().BeEmpty();
        result.Overlaps.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (PinnedCoverageService service,
            Mock<IGraphAdminClient> graph,
            FakeContentLoader content,
            InMemorySearchLogService logs)
        NewService(ISearchProfileRegistry? registry = null)
    {
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var content = new FakeContentLoader();
        var logs = new InMemorySearchLogService();
        registry ??= new StaticRegistry(BuildGeneric());
        var service = new PinnedCoverageService(
            graph.Object,
            content.Loader,
            registry,
            logs,
            NullLogger<PinnedCoverageService>.Instance);
        return (service, graph, content, logs);
    }

    private static PinnedCollectionResult NewCollection(string id, string key) => new()
    {
        Id = id,
        Key = key,
        Title = key,
        IsActive = true
    };

    private static PinnedItemResult NewItem(string phrase, string targetKey) => new()
    {
        Id = "item-" + Guid.NewGuid().ToString("N").Substring(0, 8),
        Phrases = phrase,
        TargetKey = targetKey,
        IsActive = true
    };

    private static void RegisterContent(FakeContentLoader loader, Guid guid, string name, VersionStatus status)
    {
        loader.Add(guid, new FakeContent(guid, name, status));
    }

    private static SearchProfile BuildGeneric() => new()
    {
        Key = "generic",
        DisplayName = LocalizedString.Literal("Generic"),
        Locales = Array.Empty<string>(),
        Sites = Array.Empty<string>()
    };

    private sealed class StaticRegistry : ISearchProfileRegistry
    {
        public StaticRegistry(params SearchProfile[] profiles) { All = profiles; }
        public IReadOnlyList<SearchProfile> All { get; }
        public SearchProfile? Get(string key) => All.FirstOrDefault(p => p.Key == key);
        public IEnumerable<SearchProfile> ForSite(string siteName) => All;
    }

    /// <summary>
    /// Wraps a <see cref="Mock{IContentLoader}"/> and exposes a tidy
    /// <see cref="Add"/> API the tests use to register guid→content. We mock
    /// the interface (rather than implementing it directly) because the real
    /// interface surface is wide and varies between CMS 12 / 13 — only
    /// <c>TryGet&lt;IContent&gt;(Guid, out)</c> matters for this audit.
    /// </summary>
    private sealed class FakeContentLoader
    {
        private readonly Mock<IContentLoader> _mock = new(MockBehavior.Loose);
        private readonly Dictionary<Guid, IContent> _byGuid = new();

        public FakeContentLoader()
        {
            _mock.Setup(m => m.TryGet(It.IsAny<Guid>(), out It.Ref<IContent>.IsAny))
                .Returns((Guid guid, out IContent content) =>
                {
                    if (_byGuid.TryGetValue(guid, out var found))
                    {
                        content = found;
                        return true;
                    }
                    content = null!;
                    return false;
                });
        }

        public IContentLoader Loader => _mock.Object;
        public void Add(Guid guid, IContent content) => _byGuid[guid] = content;
    }

    /// <summary>
    /// Bare-bones <see cref="IContent"/> + <see cref="IVersionable"/> shim used
    /// by the loader tests. The audit only reads <c>Name</c> + <c>Status</c>,
    /// so the rest of the surface is left at default values.
    /// </summary>
    private sealed class FakeContent : IContent, IVersionable
    {
        public FakeContent(Guid guid, string name, VersionStatus status)
        {
            ContentGuid = guid;
            Name = name;
            Status = status;
        }

        public string Name { get; set; }
        public Guid ContentGuid { get; set; }
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public int ContentTypeID { get; set; }
        public bool IsDeleted { get; set; }
        public PropertyDataCollection Property { get; } = new();
        public VersionStatus Status { get; set; }
        public DateTime? StartPublish { get; set; }
        public DateTime? StopPublish { get; set; }
        public bool IsPendingPublish { get; set; }
#if OPTIMIZELY_CMS13
        public string? Variation { get; set; }
#endif
    }

    /// <summary>
    /// Same in-memory <see cref="SearchLogService"/> idiom as
    /// <c>SearchLogServiceTests.InMemorySearchLogService</c>. We only need the
    /// methods <see cref="PinnedCoverageService"/> calls — <c>Append</c>,
    /// <c>ListSince</c> — so the rest defer to the base no-op behaviour.
    /// </summary>
    private sealed class InMemorySearchLogService : SearchLogService
    {
        private readonly List<SearchLogEntry> _rows = new();

        public override void Append(SearchLogEntry entry)
        {
            if (entry.At == default) entry.At = DateTime.UtcNow;
            _rows.Add(entry);
        }

        public override IEnumerable<SearchLogEntry> ListSince(DateTime sinceUtc, int take = 1000)
            => _rows.Where(e => e.At >= sinceUtc).OrderByDescending(e => e.At).Take(take).ToList();
    }
}
