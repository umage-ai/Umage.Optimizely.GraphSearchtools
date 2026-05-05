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
using UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks;
using UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 3 — guards the four contracts of <see cref="WebhooksApiController"/>:
/// list returns whatever the service returns, create rejects malformed URLs,
/// create delegates a well-formed payload through the service and 200s, and
/// delete delegates the id and 204s.
/// </summary>
public class WebhooksApiControllerTests
{
    [Fact]
    public async Task List_Returns_AllWebhooks_FromService()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.GetWebhooksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WebhookResult>
            {
                new() { Id = "wh-1", Request = new WebhookRequestShape { Url = "https://a.example/hook", Method = "POST" } },
                new() { Id = "wh-2", Request = new WebhookRequestShape { Url = "https://b.example/hook", Method = "PUT" }, Disabled = true }
            });

        var result = await controller.List(CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<WebhookSummary>>().Subject;
        rows.Should().HaveCount(2);
        rows[0].Id.Should().Be("wh-1");
        rows[0].Url.Should().Be("https://a.example/hook");
        rows[0].Method.Should().Be("POST");
        rows[0].Disabled.Should().BeFalse();
        rows[1].Disabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Returns400_OnInvalidUrl()
    {
        var (controller, _) = NewController();

        // Empty URL → 400.
        (await controller.Create(new WebhookCreateRequest { Url = "" }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        // Missing scheme → not a valid absolute http(s) URI → 400.
        (await controller.Create(new WebhookCreateRequest { Url = "example.com/hook" }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        // Wrong scheme → 400.
        (await controller.Create(new WebhookCreateRequest { Url = "ftp://example.com/hook" }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        // Null body → 400.
        (await controller.Create(null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        // Disallowed method → 400.
        (await controller.Create(new WebhookCreateRequest { Url = "https://example.com/hook", Method = "DELETE" }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Create_AppendsViaService_OnHappyPath()
    {
        var (controller, graph) = NewController();
        WebhookPayload? captured = null;
        graph.Setup(g => g.CreateWebhookAsync(It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookPayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new WebhookResult
            {
                Id = "new-1",
                Request = new WebhookRequestShape { Url = "https://example.com/hook", Method = "POST" }
            });

        var result = await controller.Create(new WebhookCreateRequest
        {
            Url = "  https://example.com/hook  ",
            Method = "post",
            Headers = new Dictionary<string, string> { ["X-Auth"] = "secret" }
        }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<WebhookSummary>()
            .Which.Id.Should().Be("new-1");

        captured.Should().NotBeNull();
        captured!.Request.Url.Should().Be("https://example.com/hook", "service should trim whitespace before forwarding");
        captured.Request.Method.Should().Be("POST", "method should be normalised to upper-case");
        captured.Request.Headers.Should().ContainKey("X-Auth");

        graph.Verify(g => g.CreateWebhookAsync(It.IsAny<WebhookPayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_DelegatesToService_AndReturns204()
    {
        var (controller, graph) = NewController();
        graph.Setup(g => g.DeleteWebhookAsync("wh-9", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        var result = await controller.Delete("wh-9", CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        graph.Verify();
    }

    [Fact]
    public async Task Delete_Returns400_OnEmptyId()
    {
        var (controller, _) = NewController();
        var result = await controller.Delete("", CancellationToken.None);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (WebhooksApiController controller, Mock<IGraphAdminClient> graphClient)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Webhooks = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var graphClient = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        var service = new WebhooksService(graphClient.Object);

        var controller = new WebhooksApiController(
            service,
            accessChecker,
            NullLogger<WebhooksApiController>.Instance);

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
