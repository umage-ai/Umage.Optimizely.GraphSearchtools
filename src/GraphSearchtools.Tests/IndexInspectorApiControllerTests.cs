using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using EPiServer.Security;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector;
using UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 Wave 5 — guards the contract between
/// <see cref="IndexInspectorApiController"/>, the service that wraps the
/// Graph admin client, and the snapshot DTO returned to the page JS. The
/// service forwards the host's <c>SearchableContentTypes</c> allow-list to
/// the admin client, and the controller's only happy-path endpoint surfaces
/// the result verbatim.
/// </summary>
public class IndexInspectorApiControllerTests
{
    [Fact]
    public async Task Get_Returns_SnapshotFromService()
    {
        var (controller, graph) = NewController(searchableContentTypes: new[] { "_Page", "StandardPage" });
        var captured = DateTime.UtcNow;
        graph.Setup(g => g.InspectIndexAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexInspectionResult
            {
                TotalItems = 42,
                MissingNameCount = 3,
                MissingTitleCount = 0,
                CapturedAt = captured,
                PerContentType = new List<ContentTypeIndexRow>
                {
                    new() { Name = "StandardPage", Count = 30, MissingNameCount = 2 },
                    new() { Name = "ArticlePage", Count = 12, MissingNameCount = 1 }
                }
            });

        var result = await controller.Get(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var snapshot = ok.Value.Should().BeOfType<IndexInspectorSnapshot>().Subject;

        snapshot.TotalItems.Should().Be(42);
        snapshot.MissingNameCount.Should().Be(3);
        snapshot.CapturedAt.Should().Be(captured);
        snapshot.PerContentType.Should().HaveCount(2);
        snapshot.PerContentType[0].Name.Should().Be("StandardPage");
        snapshot.PerContentType[0].Count.Should().Be(30);
        snapshot.PerContentType[0].MissingNameCount.Should().Be(2);
        snapshot.PerContentType[1].Name.Should().Be("ArticlePage");
    }

    [Fact]
    public async Task Get_PassesSearchableContentTypes_FromOptions_ToAdminClient()
    {
        IReadOnlyList<string>? captured = null;
        var (controller, graph) = NewController(searchableContentTypes: new[] { "_Page", "BlogPost" });
        graph.Setup(g => g.InspectIndexAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>, CancellationToken>((types, _) => captured = types)
            .ReturnsAsync(new IndexInspectionResult { TotalItems = 0, CapturedAt = DateTime.UtcNow });

        var result = await controller.Get(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        captured.Should().NotBeNull();
        captured!.Should().BeEquivalentTo(new[] { "_Page", "BlogPost" });
    }

    [Fact]
    public async Task Get_Returns503_WhenGraphNotConfigured()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.InspectIndexAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph not configured."));

        var result = await controller.Get(CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task Get_BubblesGraphApiError_AsUpstreamStatus()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.InspectIndexAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphSearchApiException(System.Net.HttpStatusCode.BadGateway, "upstream"));

        var result = await controller.Get(CancellationToken.None);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be((int)System.Net.HttpStatusCode.BadGateway);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (IndexInspectorApiController controller, Mock<IGraphAdminClient> graphClient)
        NewController(string[]? searchableContentTypes = null)
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { IndexInspector = true },
            CheckPermissionForEachFeature = false,
            SearchableContentTypes = searchableContentTypes ?? new[] { "_Page" }
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var service = new IndexInspectorService(graphClient.Object, options);

        var controller = new IndexInspectorApiController(
            service,
            accessChecker,
            NullLogger<IndexInspectorApiController>.Instance);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester")
            }, authenticationType: "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new RouteData() };

        return (controller, graphClient);
    }
}
