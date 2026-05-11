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
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 Wave 5 — guards the four read-only endpoints of
/// <see cref="SearchLogsApiController"/>: each endpoint delegates to the
/// matching <see cref="SearchLogService"/> aggregation, the camelCase wire
/// projection lines up with the DDS-side record, and <c>take</c> is clamped
/// before the underlying service ever sees a wild value.
/// </summary>
public class SearchLogsApiControllerTests
{
    [Fact]
    public void Top_Returns_RowsFromService_AsCamelCaseDtos()
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.TopPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(new[]
            {
                new SearchLogAggregateRow("warranty", 12, 0.0, 0.5, "en", "site-search"),
                new SearchLogAggregateRow("shipping", 7, 0.14, 0.0, "en", "")
            });

        var result = controller.Top(since: null, take: null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<SearchLogPhraseRow>>().Subject;
        rows.Should().HaveCount(2);
        rows[0].Phrase.Should().Be("warranty");
        rows[0].Hits.Should().Be(12);
        rows[0].Locale.Should().Be("en");
        rows[0].ProfileKey.Should().Be("site-search");
        rows[1].Phrase.Should().Be("shipping");
        rows[1].ZeroResultRate.Should().BeApproximately(0.14, 0.001);
    }

    [Fact]
    public void ZeroResults_Delegates_ToZeroResultPhrases()
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.ZeroResultPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(new[]
            {
                new SearchLogAggregateRow("widget", 4, 1.0, 0.0, "en", "")
            })
            .Verifiable();

        var result = controller.ZeroResults(since: null, take: null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<SearchLogPhraseRow>>().Subject;
        rows.Should().HaveCount(1);
        rows[0].Phrase.Should().Be("widget");
        rows[0].ZeroResultRate.Should().Be(1.0);
        // ZeroResults must hit the zero-result aggregation, not the top one —
        // a regression that swapped them would silently surface every phrase.
        logs.Verify(s => s.ZeroResultPhrases(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
        logs.Verify(s => s.TopPhrases(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void LowCtr_Delegates_ToLowCtrPhrases()
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.LowCtrPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(new[]
            {
                new SearchLogAggregateRow("returns", 9, 0.0, 0.05, "en", "kb-search")
            })
            .Verifiable();

        var result = controller.LowCtr(since: null, take: null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<SearchLogPhraseRow>>().Subject;
        rows.Should().HaveCount(1);
        rows[0].Phrase.Should().Be("returns");
        rows[0].Ctr.Should().BeApproximately(0.05, 0.001);
        rows[0].ProfileKey.Should().Be("kb-search");
        logs.Verify(s => s.LowCtrPhrases(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void Raw_Returns_RecentEntries_AsCamelCaseDtos()
    {
        var (controller, logs) = NewController();
        var t = new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Utc);
        logs.Setup(s => s.ListSince(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(new[]
            {
                new SearchLogEntry
                {
                    At = t, Phrase = "warranty", Locale = "en", Site = "corporate",
                    ProfileKey = "site-search", ResultCount = 12, TopResultRank = 1,
                    TopResultId = "page-42", DurationMs = 87, Ranking = "SEMANTIC",
                    Source = "host-sdk"
                }
            });

        var result = controller.Raw(since: null, take: null);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<IReadOnlyList<SearchLogRawRow>>().Subject;
        rows.Should().HaveCount(1);
        rows[0].At.Should().Be(t);
        rows[0].Phrase.Should().Be("warranty");
        rows[0].TopResultRank.Should().Be(1);
        rows[0].TopResultId.Should().Be("page-42");
        rows[0].DurationMs.Should().Be(87);
        rows[0].Ranking.Should().Be("SEMANTIC");
        rows[0].Source.Should().Be("host-sdk");
    }

    [Theory]
    [InlineData(0, 1)]      // floor: callers asking for 0 land on 1
    [InlineData(-50, 1)]    // floor: negative numbers can't reach the service
    [InlineData(5000, SearchLogsService.MaxTake)] // ceiling: huge values are capped
    [InlineData(150, 150)]  // pass-through: reasonable values are unmodified
    public void Top_ClampsTake_BeforeReachingService(int requested, int expected)
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.TopPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(Array.Empty<SearchLogAggregateRow>());

        controller.Top(since: null, take: requested);

        logs.Verify(s => s.TopPhrases(It.IsAny<DateTime>(), expected), Times.Once);
    }

    [Fact]
    public void Top_DefaultTake_IsAppliedWhenCallerOmitsTake()
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.TopPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(Array.Empty<SearchLogAggregateRow>());

        controller.Top(since: null, take: null);

        logs.Verify(s => s.TopPhrases(It.IsAny<DateTime>(), SearchLogsService.DefaultTake), Times.Once);
    }

    [Fact]
    public void Top_DefaultsSinceToWindow_WhenCallerOmitsSince()
    {
        var (controller, logs) = NewController();
        DateTime captured = default;
        logs.Setup(s => s.TopPhrases(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Callback<DateTime, int>((t, _) => captured = t)
            .Returns(Array.Empty<SearchLogAggregateRow>());

        controller.Top(since: null, take: null);

        // Default window is 24h ago; we allow a wide tolerance because the
        // controller computes "now" inside the call, not at the test's
        // wall-clock instant.
        var expected = DateTime.UtcNow - SearchLogsService.DefaultWindow;
        captured.Should().BeCloseTo(expected, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Raw_ClampsTakeBeforeReachingService()
    {
        var (controller, logs) = NewController();
        logs.Setup(s => s.ListSince(It.IsAny<DateTime>(), It.IsAny<int>()))
            .Returns(Array.Empty<SearchLogEntry>());

        controller.Raw(since: null, take: 9_999_999);

        logs.Verify(s => s.ListSince(It.IsAny<DateTime>(), SearchLogsService.MaxTake), Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static (SearchLogsApiController controller, Mock<SearchLogService> logService)
        NewController()
    {
        var options = Options.Create(new GraphSearchtoolsOptions
        {
            Features = new FeatureToggles { SearchLogs = true },
            CheckPermissionForEachFeature = false
        });
        var permissionService = new Mock<PermissionService>(MockBehavior.Loose, new object[0]).Object;
        var accessChecker = new FeatureAccessChecker(options, permissionService);

        // The DDS-backed methods are virtual on SearchLogService — Moq can
        // override them without needing a real Optimizely runtime.
        var logs = new Mock<SearchLogService>(MockBehavior.Loose);
        var service = new SearchLogsService(logs.Object);

        var controller = new SearchLogsApiController(
            service,
            accessChecker,
            NullLogger<SearchLogsApiController>.Instance);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester")
            }, authenticationType: "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = ctx, RouteData = new RouteData() };

        return (controller, logs);
    }
}
