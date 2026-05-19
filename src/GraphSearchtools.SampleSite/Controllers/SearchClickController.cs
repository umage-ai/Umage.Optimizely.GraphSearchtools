using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

/// <summary>
/// Click-beacon receiver for the search SERP. The SearchPage view fires a
/// <c>navigator.sendBeacon</c> request here when a visitor clicks a result;
/// we record a <see cref="ClickEvent"/> against the originating search bucket
/// so the telemetry flusher can attribute it to the right minute even when
/// the click arrives after the bucket has rolled over.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous on purpose — public visitors fire this beacon, not editors. The
/// endpoint is a SampleSite controller (not the addon's public ingest beacon),
/// because we resolve <see cref="ITelemetrySink"/> directly via DI rather than
/// going over HTTP for the co-located case.
/// </para>
/// <para>
/// Telemetry is fire-and-forget: returns 204 No Content on every successful
/// path so the beacon's response is always tiny, and any record failure is
/// logged + swallowed so a visitor's click never fails because of a sink
/// hiccup. The sink itself is contractually non-blocking, so this defense is
/// belt-and-braces.
/// </para>
/// </remarks>
[Route("search/click")]
public class SearchClickController : Controller
{
    private const string ChannelKey = "alloy-search";

    private readonly ITelemetrySink _telemetry;
    private readonly ILogger<SearchClickController> _logger;

    public SearchClickController(ITelemetrySink telemetry, ILogger<SearchClickController> logger)
    {
        _telemetry = telemetry;
        _logger = logger;
    }

    public sealed class ClickRequest
    {
        public string? Phrase { get; set; }
        public string? Locale { get; set; }
        public int Rank { get; set; }

        /// <summary>
        /// Minute-truncated timestamp of the originating search bucket.
        /// Optional — when absent the flusher folds into the click's own
        /// minute as a best-effort fallback (per design §5).
        /// </summary>
        public DateTime? OriginalBucketUtc { get; set; }
    }

    [HttpPost]
    public IActionResult Index([FromBody] ClickRequest? request)
    {
        // Validation is permissive — beacons fire from in-flight navigations
        // and we never want to error out a visitor's click. A drop is silent.
        if (request == null
            || string.IsNullOrWhiteSpace(request.Phrase)
            || request.Rank < 1)
        {
            return NoContent();
        }

        try
        {
            _telemetry.Record(new ClickEvent(
                Phrase: request.Phrase.Trim(),
                ChannelKey: ChannelKey,
                Locale: (request.Locale ?? string.Empty).ToLowerInvariant(),
                Rank: request.Rank,
                TimestampUtc: DateTime.UtcNow,
                OriginalBucketUtc: request.OriginalBucketUtc));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Search-click telemetry record failed for phrase '{Phrase}' rank {Rank}.",
                request.Phrase, request.Rank);
        }

        return NoContent();
    }
}
