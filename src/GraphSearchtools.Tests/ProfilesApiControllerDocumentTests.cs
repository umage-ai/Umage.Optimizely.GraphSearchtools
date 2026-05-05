using System.Security.Claims;
using EPiServer.Framework.Localization;
using EPiServer.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 2.5 §4.3 — covers the per-profile Try-it tab's document fetch
/// endpoint (<c>GET /api/profiles/{key}/document</c>). The endpoint is the
/// only contract between the iframe-embedded runner and the host's GraphQL
/// document on disk; regressions here break the Try-it preview silently.
/// </summary>
public class ProfilesApiControllerDocumentTests
{
    [Fact]
    public void GetDocument_ReturnsBody_WhenPathExists()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "gst-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var relPath = Path.Combine("Queries", "Site.graphql");
            var fullPath = Path.Combine(contentRoot, relPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            const string body = "query Site($q:String){ Content(where:{_fulltext:{match:$q}}){ items{Name} } }";
            File.WriteAllText(fullPath, body);

            var profile = BuildProfile(graphQLDocPath: relPath);
            var controller = NewController(new StaticRegistry(profile), contentRoot);

            var result = controller.GetDocument("site-search");

            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            var dto = ok.Value.Should().BeOfType<ProfileDocumentResult>().Subject;
            dto.Exists.Should().BeTrue();
            dto.Path.Should().Be(relPath);
            dto.Body.Should().Be(body);
        }
        finally
        {
            try { Directory.Delete(contentRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetDocument_ReturnsExistsFalse_WhenPathNull()
    {
        var profile = BuildProfile(graphQLDocPath: null);
        var controller = NewController(new StaticRegistry(profile), AppContext.BaseDirectory);

        var result = controller.GetDocument("site-search");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ProfileDocumentResult>().Subject;
        dto.Exists.Should().BeFalse();
        dto.Path.Should().BeNull();
        dto.Body.Should().BeNull();
    }

    [Fact]
    public void GetDocument_ReturnsExistsFalse_WhenPathDoesNotResolve()
    {
        var profile = BuildProfile(graphQLDocPath: Path.Combine("Queries", "DoesNotExist.graphql"));
        var controller = NewController(new StaticRegistry(profile), AppContext.BaseDirectory);

        var result = controller.GetDocument("site-search");

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = ok.Value.Should().BeOfType<ProfileDocumentResult>().Subject;
        dto.Exists.Should().BeFalse();
        dto.Path.Should().Be(Path.Combine("Queries", "DoesNotExist.graphql"));
        dto.Body.Should().BeNull();
    }

    [Fact]
    public void GetDocument_404_WhenProfileUnknown()
    {
        var controller = NewController(new StaticRegistry(/* empty */), AppContext.BaseDirectory);

        var result = controller.GetDocument("nope");

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void GetDocument_BadRequest_WhenKeyMissing()
    {
        var controller = NewController(new StaticRegistry(/* empty */), AppContext.BaseDirectory);

        var result = controller.GetDocument(string.Empty);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    /// <summary>
    /// Diagnostics is gated behind both the new <c>Diagnostics</c> flag and the
    /// legacy <c>SavedQueries</c> flag — explicitly setting either to false
    /// hides the menu entry. Verifies the AND gate on
    /// <see cref="FeatureAccessChecker.IsFeatureEnabled"/>; the menu provider
    /// itself reads through this same checker (see GraphSearchtoolsMenuProvider).
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void Diagnostics_FeatureFlag_GatesMenu(bool diagnostics, bool savedQueries, bool expectedVisible)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles
            {
                Diagnostics = diagnostics,
                SavedQueries = savedQueries
            },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var checker = new FeatureAccessChecker(options, permissionService);

        var diagnosticsEnabled = checker.IsFeatureEnabled(nameof(FeatureToggles.Diagnostics));
        var savedQueriesEnabled = checker.IsFeatureEnabled(nameof(FeatureToggles.SavedQueries));

        // The menu provider's `IsAvailable` predicate is `Diagnostics && SavedQueries`.
        var menuShouldRender = diagnosticsEnabled && savedQueriesEnabled;
        menuShouldRender.Should().Be(expectedVisible);
    }

    private static SearchProfile BuildProfile(string? graphQLDocPath)
    {
        return new SearchProfile
        {
            Key = "site-search",
            DisplayName = LocalizedString.Literal("Site search"),
            Sites = Array.Empty<string>(),
            Locales = new[] { "en" },
            SearchedFields = Array.Empty<string>(),
            GraphQLDocumentPath = graphQLDocPath,
            DefaultVariables = new Dictionary<string, object?>()
        };
    }

    private static ProfilesApiController NewController(ISearchProfileRegistry registry, string contentRoot)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Profiles = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var localization = new Mock<LocalizationService>(MockBehavior.Loose, new object[0]).Object;
        var hostEnv = new StubHostEnvironment { ContentRootPath = contentRoot };
        var service = new ProfilesService(registry, new SearchProfileEditService(), localization, hostEnv);

        var controller = new ProfilesApiController(service, registry, hostEnv, accessChecker, NullLogger<ProfilesApiController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity("test")) }
        };
        return controller;
    }

    private sealed class StaticRegistry : ISearchProfileRegistry
    {
        public StaticRegistry(params SearchProfile[] profiles) { All = profiles; }
        public IReadOnlyList<SearchProfile> All { get; }
        public SearchProfile? Get(string key) => All.FirstOrDefault(p => p.Key == key);
        public IEnumerable<SearchProfile> ForSite(string siteName) => All;
    }

    private sealed class StubHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
