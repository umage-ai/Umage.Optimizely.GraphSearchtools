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
using UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs;
using UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 3 — guards the two contracts of <see cref="RequestLogsApiController"/>:
/// list returns the service's projection one-for-one, and <c>take</c> is
/// clamped before it reaches the gateway so a wild query parameter can't
/// pin the upstream API.
/// </summary>
public class RequestLogsApiControllerTests
{
    [Fact]
    public async Task List_Returns_AllEntries_FromService()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.GetRequestLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RequestLogEntryResult>
            {
                new()
                {
                    Id = "rl-1",
                    At = new DateTime(2026, 5, 5, 8, 0, 0, DateTimeKind.Utc),
                    Method = "POST",
                    Operation = "SiteSearch",
                    Status = 200,
                    DurationMs = 42,
                    ResultCount = 7,
                    Ranking = "SEMANTIC",
                    Query = "query SiteSearch { Content { items { Name } } }"
                },
                new()
                {
                    Id = "rl-2",
                    At = new DateTime(2026, 5, 5, 8, 1, 0, DateTimeKind.Utc),
                    Method = "POST",
                    Operation = "Auth",
                    Status = 401,
                    DurationMs = 5,
                    Query = "query { Content { total } }"
                }
            });

        var result = await controller.List(take: null, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<RequestLogEntrySummary>>().Subject;
        rows.Should().HaveCount(2);
        rows[0].Id.Should().Be("rl-1");
        rows[0].Operation.Should().Be("SiteSearch");
        rows[0].Status.Should().Be(200);
        rows[0].DurationMs.Should().Be(42);
        rows[0].ResultCount.Should().Be(7);
        rows[0].Ranking.Should().Be("SEMANTIC");
        rows[1].Status.Should().Be(401);
    }

    [Fact]
    public async Task List_DefaultTake_IsAppliedWhenCallerOmitsTake()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.GetRequestLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RequestLogEntryResult>());

        await controller.List(take: null, CancellationToken.None);

        // The default — RequestLogsService.DefaultTake — should reach the client
        // verbatim; the service wraps but doesn't transform the value when it
        // already fits within the [1, 1000] window.
        graph.Verify(g => g.GetRequestLogsAsync(RequestLogsService.DefaultTake, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]      // floor: callers asking for 0 land on 1
    [InlineData(-50, 1)]    // floor: negative numbers can't reach the gateway
    [InlineData(5000, 1000)]// ceiling: huge values are capped at MaxTake
    [InlineData(150, 150)]  // pass-through: reasonable values are unmodified
    public async Task List_ClampsTakeBeforeReachingClient(int requested, int expected)
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.GetRequestLogsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RequestLogEntryResult>());

        await controller.List(requested, CancellationToken.None);

        graph.Verify(g => g.GetRequestLogsAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (RequestLogsApiController controller, Mock<IGraphAdminClient> graphClient)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { RequestLogs = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var service = new RequestLogsService(graphClient.Object);

        var controller = new RequestLogsApiController(
            service,
            accessChecker,
            NullLogger<RequestLogsApiController>.Instance);

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
