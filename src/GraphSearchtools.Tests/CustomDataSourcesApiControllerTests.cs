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
using UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources;
using UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 3 — guards the three contracts of <see cref="CustomDataSourcesApiController"/>:
/// list returns whatever the service returns (mapped to camel-cased
/// <see cref="DataSourceSummary"/>), sync delegates the source name to the
/// underlying client and returns 202, and sync rejects an empty source name
/// with 400 before touching the service.
/// </summary>
public class CustomDataSourcesApiControllerTests
{
    [Fact]
    public async Task List_Returns_AllSources_FromService()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.GetDataSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DataSourceResult>
            {
                new()
                {
                    Name = "products",
                    Type = "PIM",
                    ItemCount = 1234,
                    LastSyncedAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                    Status = "Healthy"
                },
                new()
                {
                    Name = "knowledge-base",
                    Type = "Custom",
                    ItemCount = null,
                    LastSyncedAt = null,
                    Status = "Failed"
                }
            });

        var result = await controller.List(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<DataSourceSummary>>().Subject;
        rows.Should().HaveCount(2);
        rows[0].Name.Should().Be("products");
        rows[0].Type.Should().Be("PIM");
        rows[0].ItemCount.Should().Be(1234);
        rows[0].Status.Should().Be("Healthy");
        rows[1].Name.Should().Be("knowledge-base");
        rows[1].ItemCount.Should().BeNull();
        rows[1].LastSyncedAt.Should().BeNull();
        rows[1].Status.Should().Be("Failed");
    }

    [Fact]
    public async Task Sync_DelegatesToService_AndReturns202()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.TriggerDataSourceSyncAsync("products", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var result = await controller.Sync("products", CancellationToken.None);

        // 202 Accepted — Graph runs the resync asynchronously.
        result.Should().BeOfType<AcceptedResult>();
        graph.Verify();
    }

    [Fact]
    public async Task Sync_Returns400_OnEmptyName()
    {
        var (controller, graph) = NewController();

        var result = await controller.Sync("", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        graph.Verify(g => g.TriggerDataSourceSyncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sync_Returns400_OnWhitespaceName()
    {
        var (controller, graph) = NewController();

        var result = await controller.Sync("   ", CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        graph.Verify(g => g.TriggerDataSourceSyncAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (CustomDataSourcesApiController controller, Mock<IGraphAdminClient> graphClient)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { CustomDataSources = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var service = new CustomDataSourcesService(graphClient.Object);

        var controller = new CustomDataSourcesApiController(
            service,
            accessChecker,
            NullLogger<CustomDataSourcesApiController>.Instance);

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
