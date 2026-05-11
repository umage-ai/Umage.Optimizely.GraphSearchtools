using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using EPiServer.Security;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 foundation — guards <see cref="TelemetryApiController"/>'s
/// contract: server-stamps <c>At</c> (when missing) and <c>Source</c>; rejects
/// oversize batches with 400; quietly drops stale-/future-dated entries with
/// 202.
/// </summary>
public class TelemetryApiControllerTests
{
    [Fact]
    public void Single_AppendsEntry_WithServerSet_At_AndSource()
    {
        var (controller, sink) = NewController();
        var request = new SearchLogEntryRequest
        {
            Phrase = "warranty",
            Locale = "EN", // server is expected to lower-case
            Site = "corporate",
            ProfileKey = "site-search",
            ResultCount = 12,
            TopResultRank = 1,
            DurationMs = 87
        };

        var result = controller.SearchLog(request);

        result.Should().BeOfType<OkObjectResult>();
        sink.Appended.Should().ContainSingle();
        var entry = sink.Appended[0];
        entry.Phrase.Should().Be("warranty");
        entry.Locale.Should().Be("en");
        entry.Source.Should().Be("host-sdk", "controller stamps the ingestion path");
        entry.At.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5),
            "server fills At when the host doesn't supply it");
    }

    [Fact]
    public void Single_HonoursHostAt_When_InWindow()
    {
        var (controller, sink) = NewController();
        var hostAt = DateTime.UtcNow.AddMinutes(-30);

        controller.SearchLog(new SearchLogEntryRequest { Phrase = "p", At = hostAt });

        sink.Appended.Should().ContainSingle();
        sink.Appended[0].At.Should().BeCloseTo(hostAt, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Single_DropsEntries_With_Stale_At_Quietly_With202()
    {
        var (controller, sink) = NewController();
        var request = new SearchLogEntryRequest
        {
            Phrase = "ancient-history",
            At = DateTime.UtcNow.AddDays(-2) // outside the 24h window
        };

        var result = controller.SearchLog(request);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        sink.Appended.Should().BeEmpty("stale entries must be dropped quietly, not stored");
    }

    [Fact]
    public void Single_DropsEntries_With_Future_At_Quietly_With202()
    {
        var (controller, sink) = NewController();
        var request = new SearchLogEntryRequest
        {
            Phrase = "from-the-future",
            At = DateTime.UtcNow.AddMinutes(30) // outside the 5-minute future window
        };

        var result = controller.SearchLog(request);

        var status = result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(StatusCodes.Status202Accepted);
        sink.Appended.Should().BeEmpty();
    }

    [Fact]
    public void Single_Returns400_OnNullBody()
    {
        var (controller, _) = NewController();
        var result = controller.SearchLog(null);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Batch_With201Entries_Returns400()
    {
        var (controller, sink) = NewController();
        var requests = Enumerable.Range(0, TelemetryApiController.MaxBatchSize + 1)
            .Select(i => new SearchLogEntryRequest { Phrase = "p" + i })
            .ToArray();

        var result = controller.SearchLogBatch(requests);

        result.Should().BeOfType<BadRequestObjectResult>();
        sink.Appended.Should().BeEmpty("the controller must reject the whole batch when too large");
    }

    [Fact]
    public void Batch_With200Entries_Accepts()
    {
        var (controller, sink) = NewController();
        var requests = Enumerable.Range(0, TelemetryApiController.MaxBatchSize)
            .Select(i => new SearchLogEntryRequest { Phrase = "p" + i })
            .ToArray();

        var result = controller.SearchLogBatch(requests);

        result.Should().BeOfType<OkObjectResult>();
        sink.Appended.Should().HaveCount(TelemetryApiController.MaxBatchSize);
    }

    [Fact]
    public void Batch_DropsOutOfWindowRows_AndReportsCounts()
    {
        var (controller, sink) = NewController();
        var requests = new[]
        {
            new SearchLogEntryRequest { Phrase = "ok-1" },
            new SearchLogEntryRequest { Phrase = "stale", At = DateTime.UtcNow.AddDays(-7) },
            new SearchLogEntryRequest { Phrase = "future", At = DateTime.UtcNow.AddHours(1) },
            new SearchLogEntryRequest { Phrase = "ok-2", At = DateTime.UtcNow.AddMinutes(-5) }
        };

        var result = controller.SearchLogBatch(requests);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        sink.Appended.Should().HaveCount(2);
        sink.Appended.Select(e => e.Phrase).Should().BeEquivalentTo(new[] { "ok-1", "ok-2" });

        // Response body shape: { accepted, dropped }
        var value = ok.Value!;
        var accepted = (int)value.GetType().GetProperty("accepted")!.GetValue(value)!;
        var dropped = (int)value.GetType().GetProperty("dropped")!.GetValue(value)!;
        accepted.Should().Be(2);
        dropped.Should().Be(2);
    }

    [Fact]
    public void Batch_Returns400_OnNullBody()
    {
        var (controller, _) = NewController();
        var result = controller.SearchLogBatch(null);
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (TelemetryApiController controller, RecordingSearchLogService sink)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { Telemetry = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        var sink = new RecordingSearchLogService();

        var controller = new TelemetryApiController(
            sink,
            accessChecker,
            NullLogger<TelemetryApiController>.Instance);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester")
            }, authenticationType: "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new RouteData() };

        return (controller, sink);
    }

    /// <summary>
    /// Captures every Append / AppendBatch call so tests can assert on the
    /// entries the controller produced, without needing an Optimizely runtime.
    /// </summary>
    private sealed class RecordingSearchLogService : SearchLogService
    {
        public List<SearchLogEntry> Appended { get; } = new();

        public override void Append(SearchLogEntry entry) => Appended.Add(entry);

        public override void AppendBatch(IEnumerable<SearchLogEntry> entries)
        {
            foreach (var e in entries) Appended.Add(e);
        }
    }
}
