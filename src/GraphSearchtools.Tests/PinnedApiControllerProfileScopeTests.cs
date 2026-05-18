using System.Security.Claims;
using EPiServer.Framework.Localization;
using EPiServer.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Overview;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 2.5 §4.1 — guards the profile-scoping behaviour on
/// <see cref="PinnedApiController"/>: writes require a <c>profileKey</c>;
/// the audit log is appended on every successful write; and the legacy
/// <c>/pinned</c> route 301-redirects to the Profiles index.
/// </summary>
public class PinnedApiControllerProfileScopeTests
{
    [Fact]
    public async Task Write_WithoutProfileKey_Returns400()
    {
        var registry = new StaticRegistry(BuildProfile("site-search"));

        var (controller, _, _) = NewController(registry);

        var result = await controller.CreateItem(
            collectionId: "col-1",
            payload: new PinnedItemPayload { Phrases = "warranty", TargetKey = Guid.NewGuid().ToString() },
            profileKey: null,
            site: null,
            locale: "en",
            cancellationToken: CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull();
        var msgProp = bad.Value!.GetType().GetProperty("message");
        msgProp.Should().NotBeNull("the controller returns { message = ... } on validation failures");
    }

    [Fact]
    public async Task Write_AppendsAuditEntry()
    {
        var registry = new StaticRegistry(BuildProfile("site-search"));
        var (controller, graphClient, editLog) = NewController(registry);

        var created = new PinnedItemResult { Id = "item-1", Phrases = "warranty", TargetKey = "guid-1" };
        graphClient
            .Setup(c => c.CreateItemAsync("col-1", It.IsAny<PinnedItemPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        await controller.CreateItem(
            collectionId: "col-1",
            payload: new PinnedItemPayload { Phrases = "warranty", TargetKey = "guid-1" },
            profileKey: "site-search",
            site: "corporate",
            locale: "en",
            cancellationToken: CancellationToken.None);

        // The audit fake captures every Append call so we can assert on shape.
        editLog.AppendedEntries.Should().ContainSingle();
        var entry = editLog.AppendedEntries[0];
        entry.ProfileKey.Should().Be("site-search");
        entry.Site.Should().Be("corporate");
        entry.Locale.Should().Be("en");
        entry.Kind.Should().Be("Pinned");
        entry.Action.Should().Be("Created");
        entry.Subject.Should().Be("warranty");
        entry.At.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void LegacyPinnedRoute_Returns301_To_ProfilesIndex()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Pinned = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        // UiStringsProvider is constructed but never invoked from Pinned() —
        // the action just returns a redirect, so a loose mock is fine.
        var loc = new Mock<LocalizationService>(MockBehavior.Loose, new object[0]).Object;
        var uiStrings = new UmageAI.Optimizely.GraphSearchTools.Localization.UiStringsProvider(loc);

        var controller = new GraphSearchtoolsController(accessChecker, uiStrings);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
        };

        var result = controller.Pinned();

        // RedirectPermanent → IActionResult of type RedirectResult with Permanent = true.
        var redirect = result.Should().BeOfType<RedirectResult>().Subject;
        redirect.Permanent.Should().BeTrue();
        redirect.Url.Should().Be("/EPiServer/cms/graphsearchtools/profiles");
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (PinnedApiController controller, Mock<IGraphAdminClient> graphClient, FakeEditService editLog)
        NewController(ISearchProfileRegistry registry)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Pinned = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var pinnedService = new PinnedService(graphClient.Object);

        // LocalizationService.GetString is non-virtual; Moq can't intercept it.
        // The controller only invokes it for the error message — the message
        // itself isn't asserted on, only the BadRequest result type.
        var loc = new Mock<LocalizationService>(MockBehavior.Loose, new object[0]).Object;

        var editLog = new FakeEditService();

        var controller = new PinnedApiController(
            pinnedService,
            accessChecker,
            registry,
            editLog,
            loc,
            NullLogger<PinnedApiController>.Instance);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester")
            }, authenticationType: "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new RouteData() };

        return (controller, graphClient, editLog);
    }

    private static SearchProfile BuildProfile(string key) => new()
    {
        Key = key,
        DisplayName = LocalizedString.Literal(key),
        Sites = new[] { "corporate" },
        Locales = new[] { "en" },
        PinnedKeyForLocale = locale => $"{key}-{locale}"
    };

    private sealed class StaticRegistry : ISearchProfileRegistry
    {
        public StaticRegistry(params SearchProfile[] profiles) { All = profiles; }
        public IReadOnlyList<SearchProfile> All { get; }
        public SearchProfile? Get(string key) => All.FirstOrDefault(p => p.Key == key);
        public IEnumerable<SearchProfile> ForSite(string siteName) => All;
    }

    /// <summary>
    /// Captures Append calls so tests can assert on the audit row written by the
    /// controller. Inherits from the real service so production code (which
    /// types it as <see cref="SearchProfileEditService"/>) accepts it.
    /// </summary>
    private sealed class FakeEditService : SearchProfileEditService
    {
        public List<SearchProfileEdit> AppendedEntries { get; } = new();

        public override void Append(SearchProfileEdit entry)
        {
            AppendedEntries.Add(entry);
            // Don't call base — the real DDS path requires an Optimizely
            // runtime to be available, which unit tests don't have.
        }
    }
}
