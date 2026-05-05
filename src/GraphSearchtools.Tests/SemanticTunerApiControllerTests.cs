using System.Security.Claims;
using EPiServer.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 3 — guards the four contracts of
/// <see cref="SemanticTunerApiController"/>:
/// <list type="bullet">
///   <item>GET returns whatever the service yields (persisted-or-default).</item>
///   <item>POST roundtrips a valid policy via the service.</item>
///   <item>POST with an overlapping-range policy returns a 400 without persisting.</item>
///   <item>POST with a null body returns a 400.</item>
/// </list>
/// The service itself is mocked so DDS doesn't need to be running — the
/// production <see cref="SemanticTunerService"/> degrades to no-op writes
/// when DDS is unreachable, but here we want to assert wiring rather than
/// persistence.
/// </summary>
public class SemanticTunerApiControllerTests
{
    [Fact]
    public void Get_ReturnsServicePolicy()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 3, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };
        var (controller, service) = NewController();
        service.Setup(s => s.GetPolicy()).Returns(policy);

        var result = controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = ok.Value.Should().BeAssignableTo<SemanticTuningPolicy>().Subject;
        returned.Tiers.Should().HaveCount(2);
        returned.Tiers[1].Ranking.Should().Be(GraphRanking.Semantic);
        returned.Tiers[1].MaxTokens.Should().BeNull();
    }

    [Fact]
    public void Get_ReturnsEmptyPolicy_WhenServiceReturnsEmpty()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.GetPolicy()).Returns(new SemanticTuningPolicy());

        var result = controller.Get();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = ok.Value.Should().BeAssignableTo<SemanticTuningPolicy>().Subject;
        returned.Tiers.Should().BeEmpty();
    }

    [Fact]
    public void Save_RoundtripsValidPolicy()
    {
        var (controller, service) = NewController();
        SemanticTuningPolicy? savedPolicy = null;
        service.Setup(s => s.SavePolicy(It.IsAny<SemanticTuningPolicy>(), It.IsAny<string?>()))
            .Callback<SemanticTuningPolicy, string?>((p, _) => savedPolicy = p);
        service.Setup(s => s.GetPolicy()).Returns(() => savedPolicy ?? new SemanticTuningPolicy());

        var input = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 3, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };

        var result = controller.Save(input);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = ok.Value.Should().BeAssignableTo<SemanticTuningPolicy>().Subject;
        returned.Tiers.Should().HaveCount(2);

        savedPolicy.Should().NotBeNull();
        savedPolicy!.Tiers.Should().HaveCount(2);
        savedPolicy.Tiers[0].MinTokens.Should().Be(1);
        savedPolicy.Tiers[1].MinTokens.Should().Be(3);
    }

    [Fact]
    public void Save_Returns400_OnOverlappingRanges()
    {
        var (controller, service) = NewController();

        var invalid = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                // [1..3] and [2..4] overlap on tokens 2 and 3.
                new() { MinTokens = 1, MaxTokens = 3, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 2, MaxTokens = 4, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };

        var result = controller.Save(invalid);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        // Body is the anonymous { message = ... } shape.
        bad.Value.Should().NotBeNull();
        bad.Value!.ToString().Should().Contain("Overlapping");

        // Service should NOT have been called.
        service.Verify(s => s.SavePolicy(It.IsAny<SemanticTuningPolicy>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Save_Returns400_OnOpenEndedTierNotLast()
    {
        var (controller, service) = NewController();

        var invalid = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                // Open-ended in the middle is invalid: the next tier can never be reached.
                new() { MinTokens = 1, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 },
                new() { MinTokens = 5, MaxTokens = 10, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 }
            }
        };

        var result = controller.Save(invalid);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(s => s.SavePolicy(It.IsAny<SemanticTuningPolicy>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Save_Returns400_OnNullBody()
    {
        var (controller, service) = NewController();

        var result = controller.Save(null);

        result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(s => s.SavePolicy(It.IsAny<SemanticTuningPolicy>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void Save_AcceptsEmptyPolicy()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.GetPolicy()).Returns(new SemanticTuningPolicy());

        var result = controller.Save(new SemanticTuningPolicy());

        result.Should().BeOfType<OkObjectResult>();
        service.Verify(s => s.SavePolicy(It.IsAny<SemanticTuningPolicy>(), It.IsAny<string?>()), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (SemanticTunerApiController controller, Mock<SemanticTunerService> service)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { SemanticTuner = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var service = new Mock<SemanticTunerService>(MockBehavior.Loose, options) { CallBase = false };

        var controller = new SemanticTunerApiController(
            service.Object,
            accessChecker,
            NullLogger<SemanticTunerApiController>.Instance);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester")
            }, authenticationType: "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new RouteData() };

        return (controller, service);
    }
}
