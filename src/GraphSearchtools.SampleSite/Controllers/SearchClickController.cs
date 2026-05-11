using EPiServer.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

/// <summary>
/// Click-beacon receiver for the search SERP. The SearchPage view fires a
/// <c>navigator.sendBeacon</c> request here when a visitor clicks a result;
/// we record a follow-up <see cref="SearchLogEntry"/> with
/// <see cref="SearchLogEntry.TopResultRank"/> and
/// <see cref="SearchLogEntry.TopResultId"/> set so the Search Logs analytics
/// can compute click-through rate per phrase.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous on purpose — public visitors fire this beacon, not editors.
/// The endpoint is a SampleSite controller (not the addon's
/// <c>TelemetryApi</c>, which is editor-auth-gated and aimed at out-of-process
/// hosts pushing over HTTP).
/// </para>
/// <para>
/// Telemetry is fire-and-forget: returns 204 No Content on every successful
/// path so the beacon's response is always tiny, and any append failure is
/// logged + swallowed so a visitor's click never fails because of a DDS
/// hiccup.
/// </para>
/// </remarks>
[Route("search/click")]
public class SearchClickController : Controller
{
    private const string ProfileKey = "alloy-search";

    private readonly SearchLogService _searchLog;
    private readonly ILogger<SearchClickController> _logger;

    public SearchClickController(SearchLogService searchLog, ILogger<SearchClickController> logger)
    {
        _searchLog = searchLog;
        _logger = logger;
    }

    public sealed class ClickRequest
    {
        public string? Phrase { get; set; }
        public string? Locale { get; set; }
        public int Rank { get; set; }
        public string? ContentId { get; set; }
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
            _searchLog.Append(new SearchLogEntry
            {
                At = DateTime.UtcNow,
                Phrase = request.Phrase.Trim(),
                Locale = (request.Locale ?? string.Empty).ToLowerInvariant(),
                Site = SiteDefinition.Current?.Name ?? string.Empty,
                ProfileKey = ProfileKey,
                // -1 because the click event isn't a search — the impression
                // was already recorded by SearchPageController.Index. The
                // Search Logs CTR rollup keys off TopResultRank presence,
                // not result counts, so a sentinel here is correct.
                ResultCount = -1,
                TopResultRank = request.Rank,
                TopResultId = (request.ContentId ?? string.Empty).Trim(),
                DurationMs = 0,
                Ranking = "Semantic",
                ClientId = string.Empty,
                UserAgentClass = ClassifyUserAgent(Request?.Headers["User-Agent"].ToString()),
                Source = "host-sdk"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Search-click telemetry append failed for phrase '{Phrase}' rank {Rank}.",
                request.Phrase, request.Rank);
        }

        return NoContent();
    }

    /// <summary>
    /// Mirror of <c>SearchPageController.ClassifyUserAgent</c> — kept inline
    /// rather than extracted because the two controllers are the only callers
    /// and the heuristic is two lines.
    /// </summary>
    private static string ClassifyUserAgent(string? ua)
    {
        if (string.IsNullOrEmpty(ua)) return string.Empty;
        if (ua.Contains("bot", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("crawler", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("spider", StringComparison.OrdinalIgnoreCase))
        {
            return "bot";
        }
        if (ua.Contains("Mobile", StringComparison.Ordinal)
            || ua.Contains("Android", StringComparison.Ordinal)
            || ua.Contains("iPhone", StringComparison.Ordinal))
        {
            return "mobile";
        }
        return "desktop";
    }
}
