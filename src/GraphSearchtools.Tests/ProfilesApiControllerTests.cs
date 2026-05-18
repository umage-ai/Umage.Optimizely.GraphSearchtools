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
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

public class ProfilesApiControllerTests
{
    [Fact]
    public void Index_ReturnsRegisteredProfiles()
    {
        var alloy = new SearchProfile
        {
            Key = "alloy-search",
            DisplayName = LocalizedString.Literal("Alloy site search"),
            Sites = Array.Empty<string>(),
            Locales = new[] { "en" }
        };
        var controller = NewController(new StaticRegistry(alloy));

        var result = controller.List();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IEnumerable<ProfileSummary>>().Subject;
        rows.Should().ContainSingle()
            .Which.Key.Should().Be("alloy-search");
    }

    [Fact]
    public void Index_ReturnsEmptyWhenNoProfilesRegistered()
    {
        var controller = NewController(new StaticRegistry(/* empty */));

        var result = controller.List();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IEnumerable<ProfileSummary>>().Subject;
        rows.Should().BeEmpty();
    }

    [Fact]
    public void Detail_404OnUnknownKey()
    {
        var controller = NewController(new StaticRegistry(/* empty */));

        var result = controller.Get("does-not-exist");

        result.Should().BeOfType<NotFoundResult>();
    }

    private static ProfilesApiController NewController(ISearchProfileRegistry registry)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Profiles = true },
            CheckPermissionForEachFeature = false  // skips the PermissionService.IsPermitted path
        });

        // Both LocalizationService and PermissionService are abstract / require
        // EPiServer runtime services; mocking them is the lightest path. With
        // CheckPermissionForEachFeature = false, FeatureAccessChecker never
        // calls IsPermitted, so the mock returns nothing meaningful.
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var localization = new Mock<LocalizationService>(MockBehavior.Loose, new object[0]).Object;
        var hostEnv = new StubHostEnvironment();

        // The Index/Detail tests don't exercise endpoints that hit Graph, but
        // ProfilesService and PinnedService both demand graph-client deps now —
        // a loose mock satisfies the constructor without making any calls.
        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose).Object;
        var credentialsResolver = new Mock<IGraphCredentialsResolver>(MockBehavior.Loose).Object;
        var queryRunner = new QueryRunnerService(new HttpClient(), credentialsResolver, options);
        var service = new ProfilesService(registry, new SearchProfileEditService(), localization, hostEnv, graphClient, queryRunner);

        var pinnedService = new PinnedService(graphClient);

        var controller = new ProfilesApiController(service, registry, accessChecker, pinnedService, NullLogger<ProfilesApiController>.Instance);
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
