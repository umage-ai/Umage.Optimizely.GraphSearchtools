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
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 5 — guards the wiring of <see cref="RelevancyLabApiController"/>
/// to <see cref="RelevancyLabService"/>:
/// <list type="bullet">
///   <item>Set CRUD round-trips (list, get, upsert, delete).</item>
///   <item>Run delegation passes the resolved set + config through to the service.</item>
///   <item>Validation rejection (missing name) returns 400 without persisting.</item>
///   <item>Compare and CSV export delegate to the service and return the
///         expected response shapes.</item>
/// </list>
/// The service is mocked so DDS doesn't need to be running and the run
/// engine doesn't need an HttpClient.
/// </summary>
public class RelevancyLabApiControllerTests
{
    [Fact]
    public void ListSets_ReturnsServiceList()
    {
        var (controller, service) = NewController();
        var sets = new List<GoldenSet>
        {
            new() { Id = Guid.NewGuid(), Name = "Top phrases", Items = new() }
        };
        service.Setup(s => s.ListSets()).Returns(sets);

        var result = controller.ListSets();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<IReadOnlyList<GoldenSet>>()
            .Subject.Should().HaveCount(1);
    }

    [Fact]
    public void GetSet_ReturnsNotFoundWhenMissing()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.GetSet(It.IsAny<Guid>())).Returns((GoldenSet?)null);

        controller.GetSet(Guid.NewGuid()).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void UpsertSet_RoundtripsValidPayload()
    {
        var (controller, service) = NewController();
        var input = new GoldenSet
        {
            Name = "Top phrases",
            Items = new()
            {
                new() { Phrase = "boots", ExpectedTop = new() { new() { ContentLink = "g1", Weight = 1 } } }
            }
        };

        GoldenSet? captured = null;
        service.Setup(s => s.UpsertSet(It.IsAny<GoldenSet>(), It.IsAny<string?>()))
            .Callback<GoldenSet, string?>((g, _) => captured = g)
            .Returns(() =>
            {
                var saved = captured!;
                saved.Id = Guid.NewGuid();
                return saved;
            });

        var result = controller.UpsertSet(input);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var saved = ok.Value.Should().BeAssignableTo<GoldenSet>().Subject;
        saved.Id.Should().NotBe(Guid.Empty);
        saved.Items.Should().HaveCount(1);
        captured.Should().NotBeNull();
    }

    [Fact]
    public void UpsertSet_Returns400_OnNullBody()
    {
        var (controller, _) = NewController();
        controller.UpsertSet(null).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void UpsertSet_Returns400_OnValidationFailure()
    {
        var (controller, service) = NewController();
        var invalid = new GoldenSet { Name = "" };

        var result = controller.UpsertSet(invalid);

        result.Should().BeOfType<BadRequestObjectResult>();
        // The service should never see the invalid payload.
        service.Verify(s => s.UpsertSet(It.IsAny<GoldenSet>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void DeleteSet_NoContentOnSuccess()
    {
        var (controller, service) = NewController();
        var id = Guid.NewGuid();
        service.Setup(s => s.DeleteSet(id)).Returns(true);

        controller.DeleteSet(id).Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public void DeleteSet_NotFoundWhenMissing()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.DeleteSet(It.IsAny<Guid>())).Returns(false);

        controller.DeleteSet(Guid.NewGuid()).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Run_DelegatesToService()
    {
        var (controller, service) = NewController();
        var setId = Guid.NewGuid();
        var set = new GoldenSet { Id = setId, Name = "Top", Items = new() };
        service.Setup(s => s.GetSet(setId)).Returns(set);

        var run = new Run { Id = Guid.NewGuid(), GoldenSetId = setId, Ndcg10 = 0.7, Mrr = 0.5 };
        service.Setup(s => s.RunAsync(set, It.IsAny<RankingConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        var result = await controller.Run(new RunRequest { GoldenSetId = setId, Config = new RankingConfig() }, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var returned = ok.Value.Should().BeAssignableTo<Run>().Subject;
        returned.Id.Should().Be(run.Id);
        returned.Ndcg10.Should().Be(0.7);
    }

    [Fact]
    public async Task Run_NotFoundWhenSetMissing()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.GetSet(It.IsAny<Guid>())).Returns((GoldenSet?)null);

        var result = await controller.Run(new RunRequest { GoldenSetId = Guid.NewGuid() }, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Run_BadRequest_OnNullOrEmptyId()
    {
        var (controller, _) = NewController();

        (await controller.Run(null, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();

        (await controller.Run(new RunRequest { GoldenSetId = Guid.Empty }, CancellationToken.None))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ListRuns_DelegatesToService()
    {
        var (controller, service) = NewController();
        var setId = Guid.NewGuid();
        service.Setup(s => s.ListRecentRuns(setId, It.IsAny<int>()))
            .Returns(new List<Run> { new() { Id = Guid.NewGuid(), GoldenSetId = setId } });

        var result = controller.ListRuns(setId, 25);
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<IReadOnlyList<Run>>().Subject.Should().HaveCount(1);
    }

    [Fact]
    public void Compare_DelegatesToService()
    {
        var (controller, service) = NewController();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var result = new CompareResult { Entries = new() { new() { Phrase = "boots", NdcgDelta = 0.1 } }, NdcgDelta = 0.05 };
        service.Setup(s => s.CompareRuns(a, b)).Returns(result);

        var actual = controller.Compare(a, b);
        var ok = actual.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeAssignableTo<CompareResult>().Subject.Entries.Should().HaveCount(1);
    }

    [Fact]
    public void Compare_BadRequest_OnEmptyIds()
    {
        var (controller, _) = NewController();
        controller.Compare(Guid.Empty, Guid.NewGuid()).Should().BeOfType<BadRequestObjectResult>();
        controller.Compare(Guid.NewGuid(), Guid.Empty).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ExportCsv_StreamsRunPayload()
    {
        var (controller, service) = NewController();
        var run = new Run
        {
            Id = Guid.NewGuid(),
            PerQuery = new()
            {
                new() { Phrase = "boots", Ndcg10 = 1.0, Mrr = 1.0, ActualTop = new() { "g1" } }
            }
        };
        service.Setup(s => s.GetRun(run.Id)).Returns(run);

        var result = controller.ExportCsv(run.Id);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents);
        csv.Should().StartWith("phrase,ndcg10,mrr,topResults");
        csv.Should().Contain("boots");
    }

    [Fact]
    public void ExportCsv_NotFoundWhenMissing()
    {
        var (controller, service) = NewController();
        service.Setup(s => s.GetRun(It.IsAny<Guid>())).Returns((Run?)null);

        controller.ExportCsv(Guid.NewGuid()).Should().BeOfType<NotFoundResult>();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (RelevancyLabApiController controller, Mock<RelevancyLabService> service)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { RelevancyLab = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var service = new Mock<RelevancyLabService>(MockBehavior.Loose) { CallBase = false };

        var controller = new RelevancyLabApiController(
            service.Object,
            accessChecker,
            NullLogger<RelevancyLabApiController>.Instance);

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
