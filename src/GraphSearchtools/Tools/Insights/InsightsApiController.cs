using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights;

/// <summary>
/// Read-only REST API for the Insights dashboard. Mounted under
/// <c>{basePath}/InsightsApi/{action}</c>. The same endpoints serve both the
/// global Insights view and the per-channel Insights sub-tab —
/// <c>channelKey</c> / <c>locale</c> are optional filters.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
internal class InsightsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Insights);
    private const int DefaultTake = 25;

    private readonly InsightsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<InsightsApiController> _logger;

    public InsightsApiController(
        InsightsService service,
        FeatureAccessChecker accessChecker,
        ILogger<InsightsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    /// <summary>
    /// <c>GET InsightsApi/TopPhrases?days=7&amp;take=25&amp;channelKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> TopPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? channelKey = null,
        [FromQuery] string? locale = null,
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.TopPhrasesAsync(days, take ?? DefaultTake, channelKey, locale, date, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/ZeroResultPhrases?days=7&amp;take=25&amp;channelKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ZeroResultPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? channelKey = null,
        [FromQuery] string? locale = null,
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.ZeroResultPhrasesAsync(days, take ?? DefaultTake, channelKey, locale, date, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/SearchKpis?channelKey=…</c> — 30-day totals +
    /// sparkline series for searches, CTR, and zero-result searches. Window
    /// is fixed at <see cref="InsightsService.SearchKpisWindowDays"/>; the
    /// page-level 7d/30d toggle does not apply. When <c>channelKey</c> is
    /// supplied, results are scoped to that channel (Channel detail surface);
    /// without it, the response is the cluster-wide aggregate (Insights tool).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchKpis(
        [FromQuery] string? channelKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.SearchKpisAsync(channelKey, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/LowCtrPhrases?days=7&amp;take=25&amp;channelKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LowCtrPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? channelKey = null,
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.LowCtrPhrasesAsync(days, take ?? DefaultTake, channelKey, locale, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Insights);

    private IActionResult HandleError(Exception exception)
    {
        _logger.LogError(exception, "Unhandled error in InsightsApiController.");
        return Problem(title: "Insights API request failed.");
    }
}
