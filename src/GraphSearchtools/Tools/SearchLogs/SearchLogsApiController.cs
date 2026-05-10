using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

/// <summary>
/// REST API for the Search Logs UI. Mounted under
/// <c>{basePath}/SearchLogsApi/{action}</c> by the convention route. All four
/// endpoints are read-only and idempotent — the writes go through the Phase 4
/// foundation <c>TelemetryApiController</c>, which this surface intentionally
/// does not touch.
/// </summary>
/// <remarks>
/// Each endpoint accepts the same <c>since</c> + <c>take</c> pair so the JS
/// can drive the time-window pill from a single state variable.
/// <c>since</c> is wire-formatted as a UTC ISO-8601 string; <c>take</c> is a
/// plain integer. Both are normalised by <see cref="SearchLogsService"/>.
/// </remarks>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SearchLogsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SearchLogs);

    private readonly SearchLogsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SearchLogsApiController> _logger;

    public SearchLogsApiController(
        SearchLogsService service,
        FeatureAccessChecker accessChecker,
        ILogger<SearchLogsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/Top?since=…&amp;take=…</c> — top phrases ranked
    /// by hit count, most-frequent first.
    /// </summary>
    [HttpGet]
    public IActionResult Top([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? profileKey = null)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_service.TopPhrases(since, take, profileKey));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/ZeroResults?since=…&amp;take=…</c> — phrases
    /// whose sessions returned zero hits. The synonym-mining list.
    /// </summary>
    [HttpGet]
    public IActionResult ZeroResults([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? profileKey = null)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_service.ZeroResultPhrases(since, take, profileKey));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/LowCtr?since=…&amp;take=…</c> — phrases with the
    /// worst click-through rate (sessions with <c>TopResultRank ≤ 3</c> over
    /// total). Phrases with fewer than 5 sessions are excluded by the
    /// foundation service to keep the surface actionable.
    /// </summary>
    [HttpGet]
    public IActionResult LowCtr([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? profileKey = null)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_service.LowCtrPhrases(since, take, profileKey));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/Raw?since=…&amp;take=…</c> — recent raw entries
    /// in the window, newest-first. Backs the "live tail" card.
    /// </summary>
    [HttpGet]
    public IActionResult Raw([FromQuery] DateTime? since, [FromQuery] int? take)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_service.RecentEntries(since, take));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.SearchLogs);

    private IActionResult HandleError(Exception exception)
    {
        // Search Logs is a read surface over DDS — the realistic failure modes
        // are "DDS not initialised" (handled gracefully by the foundation
        // service which returns an empty list) and unexpected-but-benign
        // serialisation errors. Never leak the exception message to the wire.
        _logger.LogError(exception, "Unhandled error in SearchLogsApiController.");
        return Problem(title: "Search Logs API request failed.");
    }
}
