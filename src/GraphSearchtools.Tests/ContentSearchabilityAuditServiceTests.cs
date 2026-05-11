using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit;
using UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit.Models;

#pragma warning disable CS0618 // ISiteDefinitionRepository — see LanguageSiteEnumerator / HealthScanService.

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 — verifies <see cref="ContentSearchabilityAuditService"/> detects
/// each of the four issue kinds against a representative content tree, and
/// that a fully clean page produces no false positives. The service walks
/// <c>ISiteDefinitionRepository</c> + <c>IContentLoader.GetDescendents</c>
/// per site, so the fixture seeds a single fake site whose descendants are
/// a hand-built mix of clean / dirty pages.
/// </summary>
public class ContentSearchabilityAuditServiceTests
{
    [Fact]
    public void Run_DetectsAllFourIssueKinds_AndLeavesCleanPagesAlone()
    {
        var pages = new List<IContent>
        {
            // Page A: every editorial field is present and all string fields fit
            // — this page must never appear in the result.
            new FakeArticlePage
            {
                ContentLink = new ContentReference(11),
                ContentGuid = Guid.NewGuid(),
                Name = "Healthy article",
                Heading = "A reasonable heading",
                MainBody = "Body text.",
                Tags = new[] { "alpha" }
            },

            // Page B: missing Name AND empty body AND empty tags. Should
            // surface three issues against the same content link.
            new FakeArticlePage
            {
                ContentLink = new ContentReference(12),
                ContentGuid = Guid.NewGuid(),
                Name = "   ",                  // whitespace counts as missing
                Heading = "Heading two",
                MainBody = string.Empty,
                Tags = Array.Empty<string>()
            },

            // Page C: oversize searchable string. Heading length intentionally
            // exceeds the 1024-char sortable-field limit so the audit flags it
            // as the OversizeSortField kind.
            new FakeArticlePage
            {
                ContentLink = new ContentReference(13),
                ContentGuid = Guid.NewGuid(),
                Name = "Long heading page",
                Heading = new string('x', 1500),
                MainBody = "Body text.",
                Tags = new[] { "beta" }
            }
        };

        var service = BuildService(pages);

        var result = service.Run();

        // 3 seeded pages + the site root the fixture exposes via TryGet on
        // SiteDefinition.StartPage. The root is a clean FakeMinimalPage so it
        // never appears in the issues list (asserted below).
        result.ItemsScanned.Should().Be(4);
        var byKind = result.Issues.GroupBy(i => i.Kind).ToDictionary(g => g.Key, g => g.ToList());

        // MissingName — only Page B (whitespace). The clean and oversize pages
        // both have valid names.
        byKind[AuditIssueKinds.MissingName].Should().HaveCount(1)
            .And.OnlyContain(i => i.ContentLink == 12);

        // MissingMainBody — only Page B. Page A has body, Page C has body too;
        // Page B's body is the empty string.
        byKind[AuditIssueKinds.MissingMainBody].Should().HaveCount(1)
            .And.OnlyContain(i => i.ContentLink == 12);

        // NoTags — only Page B. Page A has a tag; Page C has a tag.
        byKind[AuditIssueKinds.NoTags].Should().HaveCount(1)
            .And.OnlyContain(i => i.ContentLink == 12);

        // OversizeSortField — only Page C. Detail must mention the actual
        // length so the editor can act on it (trim vs. unmark searchable).
        byKind[AuditIssueKinds.OversizeSortField].Should().HaveCount(1)
            .And.OnlyContain(i => i.ContentLink == 13 && i.Detail.Contains("1500"));

        // The clean Page A produces zero issues — all four lists agree.
        result.Issues.Should().NotContain(i => i.ContentLink == 11);
        result.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Run_OnlyMarksFieldOversizeWhenLengthExceedsTheGraphLimit()
    {
        // Boundary check: Graph's caveat is "do not sort on values longer than
        // 1024 characters". Exactly-1024 is fine; 1025 is not. The page below
        // is clean except for the borderline-length heading.
        var rightAtLimit = new FakeArticlePage
        {
            ContentLink = new ContentReference(21),
            ContentGuid = Guid.NewGuid(),
            Name = "Edge limit",
            Heading = new string('x', 1024),
            MainBody = "Body.",
            Tags = new[] { "tag" }
        };
        var oneOver = new FakeArticlePage
        {
            ContentLink = new ContentReference(22),
            ContentGuid = Guid.NewGuid(),
            Name = "One over",
            Heading = new string('x', 1025),
            MainBody = "Body.",
            Tags = new[] { "tag" }
        };

        var service = BuildService(new[] { (IContent)rightAtLimit, oneOver });

        var result = service.Run();

        var oversize = result.Issues.Where(i => i.Kind == AuditIssueKinds.OversizeSortField).ToList();
        oversize.Should().HaveCount(1);
        oversize[0].ContentLink.Should().Be(22);
    }

    [Fact]
    public void Run_AbsentMainBodyOrTagsProperty_DoesNotProduceFalsePositives()
    {
        // A content type without MainBody / Tags shouldn't be flagged for
        // missing those fields — the heuristic only fires when a property
        // with one of the recognised names actually exists on the type.
        var minimal = new FakeMinimalPage
        {
            ContentLink = new ContentReference(31),
            ContentGuid = Guid.NewGuid(),
            Name = "Minimal page"
        };

        var service = BuildService(new IContent[] { minimal });

        var result = service.Run();

        result.Issues.Should().NotContain(i => i.Kind == AuditIssueKinds.MissingMainBody);
        result.Issues.Should().NotContain(i => i.Kind == AuditIssueKinds.NoTags);
        result.Issues.Should().NotContain(i => i.Kind == AuditIssueKinds.MissingName);
        result.Issues.Should().NotContain(i => i.Kind == AuditIssueKinds.OversizeSortField);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Fixture
    // ──────────────────────────────────────────────────────────────────

    private static ContentSearchabilityAuditService BuildService(IList<IContent> pages)
    {
        var startPage = new ContentReference(1);

        var siteRepo = new Mock<ISiteDefinitionRepository>(MockBehavior.Loose);
        siteRepo.Setup(s => s.List()).Returns(new[]
        {
            new SiteDefinition { Id = Guid.NewGuid(), Name = "site", StartPage = startPage }
        });

        var contentLoader = new Mock<IContentLoader>(MockBehavior.Loose);
        var pagesByLink = pages.ToDictionary(p => p.ContentLink.ID);

        // GetDescendents returns just the seeded items' content links — the
        // service walks them itself via TryGet<IContent>.
        contentLoader.Setup(c => c.GetDescendents(startPage))
            .Returns(pages.Select(p => p.ContentLink).ToList());

        // The service also probes the site root (TryGet on startPage) so we
        // satisfy that with a stub no-op page that has no editorial issues.
        var rootStub = new FakeMinimalPage
        {
            ContentLink = startPage,
            ContentGuid = Guid.NewGuid(),
            Name = "Root"
        };

        contentLoader.Setup(c => c.TryGet(It.IsAny<ContentReference>(), out It.Ref<IContent>.IsAny))
            .Returns(new TryGetCallback((ContentReference link, out IContent content) =>
            {
                if (link == startPage) { content = rootStub; return true; }
                if (pagesByLink.TryGetValue(link.ID, out var page)) { content = page; return true; }
                content = default!;
                return false;
            }));

        var typeRepo = new Mock<IContentTypeRepository>(MockBehavior.Loose);
        // Each fake page exposes its CLR type via GetOriginalType(); the service
        // falls back to that when ContentType.Load returns null. Loose mock
        // already returns null for reference types — explicit setup omitted to
        // avoid a Mock.Returns nullability cast warning.

        return new ContentSearchabilityAuditService(
            contentLoader.Object,
            typeRepo.Object,
            siteRepo.Object,
            NullLogger<ContentSearchabilityAuditService>.Instance);
    }

    // Helper delegate for mocking IContentLoader.TryGet's out-parameter
    // signature with Moq.
    private delegate bool TryGetCallback(ContentReference link, out IContent content);

    // ──────────────────────────────────────────────────────────────────
    //   Fakes — minimal IContent shapes the service can reflect on.
    // ──────────────────────────────────────────────────────────────────

    private class FakeArticlePage : IContent, IVersionable
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; }
        public int ContentTypeID { get; set; } = 100;
        public bool IsDeleted { get; set; }
        public PropertyDataCollection Property { get; } = new();

        // Heading is searchable — used as the oversize-sort target in tests.
        [Searchable]
        public string Heading { get; set; } = string.Empty;

        public string MainBody { get; set; } = string.Empty;

        public IEnumerable<string> Tags { get; set; } = Array.Empty<string>();

        // IVersionable — every page in tests is published.
        public VersionStatus Status { get => VersionStatus.Published; set { } }
        public DateTime? StartPublish { get; set; }
        public DateTime? StopPublish { get; set; }
        public bool IsPendingPublish { get; set; }
#if OPTIMIZELY_CMS13
        // CMS 13 added a Variation key on IVersionable. Tests don't exercise
        // variation, but the interface is mandatory on the fake.
        public string? Variation { get; set; }
#endif
    }

    /// <summary>
    /// A content type that has neither MainBody nor Tags — exercises the
    /// "no false positive when the property doesn't exist" guard.
    /// </summary>
    private class FakeMinimalPage : IContent, IVersionable
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; }
        public int ContentTypeID { get; set; } = 200;
        public bool IsDeleted { get; set; }
        public PropertyDataCollection Property { get; } = new();
        public VersionStatus Status { get => VersionStatus.Published; set { } }
        public DateTime? StartPublish { get; set; }
        public DateTime? StopPublish { get; set; }
        public bool IsPendingPublish { get; set; }
#if OPTIMIZELY_CMS13
        public string? Variation { get; set; }
#endif
    }
}
