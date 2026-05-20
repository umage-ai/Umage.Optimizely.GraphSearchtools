using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

/// <summary>
/// Internal read API over the search-log telemetry. Mounted under
/// <c>{basePath}/SearchLogsApi/{action}</c> by the convention route. Drives
/// the per-channel Insights tab on Channel Detail; the standalone Search Logs
/// page that previously consumed every endpoint here has been retired.
/// All four endpoints are read-only and idempotent; writes go through the
/// public telemetry beacon (<c>POST /api/telemetry/searchlog</c>), which this
/// surface intentionally does not touch.
/// </summary>
/// <remarks>
/// Each endpoint accepts the same <c>since</c> + <c>take</c> pair so the JS
/// can drive the time-window pill from a single state variable.
/// <c>since</c> is wire-formatted as a UTC ISO-8601 string; <c>take</c> is a
/// plain integer. Both are normalised by <see cref="SearchLogsService"/>.
/// </remarks>
[Authorize(Policy = "umageai:graphsearchtools")]
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
    public async Task<IActionResult> Top([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? channelKey = null, [FromQuery] string? locale = null, [FromQuery] DateTime? until = null, CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.TopPhrasesAsync(since, take, channelKey, locale, until, cancellationToken));
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
    public async Task<IActionResult> ZeroResults([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? channelKey = null, [FromQuery] string? locale = null, [FromQuery] DateTime? until = null, CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.ZeroResultPhrasesAsync(since, take, channelKey, locale, until, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/LowCtr?since=…&amp;take=…</c> — phrases with the
    /// worst click-through rate. The reader's threshold (half the window mean)
    /// keeps the surface actionable.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LowCtr([FromQuery] DateTime? since, [FromQuery] int? take, [FromQuery] string? channelKey = null, [FromQuery] string? locale = null, [FromQuery] DateTime? until = null, CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.LowCtrPhrasesAsync(since, take, channelKey, locale, until, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET /SearchLogsApi/Raw?since=…&amp;take=…</c> — recent raw entries
    /// in the window, newest-first. Backs the "live tail" card. Per design
    /// §3.3 the underlying ring is a per-instance reservoir sample; cross-node
    /// forensics is a non-goal.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Raw([FromQuery] DateTime? since, [FromQuery] int? take, CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.RecentEntriesAsync(since, take, cancellationToken));
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
        // are "DDS not initialised" (handled gracefully by the reader's null
        // fallback) and unexpected-but-benign serialisation errors. Never leak
        // the exception message to the wire.
        _logger.LogError(exception, "Unhandled error in SearchLogsApiController.");
        return Problem(title: "Search Logs API request failed.");
    }
}
