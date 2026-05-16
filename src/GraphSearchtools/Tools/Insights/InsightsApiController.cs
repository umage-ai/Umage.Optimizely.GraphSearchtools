using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights;

/// <summary>
/// Read-only REST API for the Insights dashboard. Mounted under
/// <c>{basePath}/InsightsApi/{action}</c>. The same endpoints serve both the
/// global Insights view and the per-profile Insights sub-tab —
/// <c>profileKey</c> / <c>locale</c> are optional filters.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class InsightsApiController : Controller
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
    /// <c>GET InsightsApi/TopPhrases?days=7&amp;take=25&amp;profileKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> TopPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? profileKey = null,
        [FromQuery] string? locale = null,
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.TopPhrasesAsync(days, take ?? DefaultTake, profileKey, locale, date, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/ZeroResultPhrases?days=7&amp;take=25&amp;profileKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ZeroResultPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? profileKey = null,
        [FromQuery] string? locale = null,
        [FromQuery] DateTime? date = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.ZeroResultPhrasesAsync(days, take ?? DefaultTake, profileKey, locale, date, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/SearchKpis?profileKey=…</c> — 30-day totals +
    /// sparkline series for searches, CTR, and zero-result searches. Window
    /// is fixed at <see cref="InsightsService.SearchKpisWindowDays"/>; the
    /// page-level 7d/30d toggle does not apply. When <c>profileKey</c> is
    /// supplied, results are scoped to that profile (Profile detail surface);
    /// without it, the response is the cluster-wide aggregate (Insights tool).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchKpis(
        [FromQuery] string? profileKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.SearchKpisAsync(profileKey, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// <c>GET InsightsApi/LowCtrPhrases?days=7&amp;take=25&amp;profileKey=…&amp;locale=…</c>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LowCtrPhrases(
        [FromQuery] int days = 7,
        [FromQuery] int? take = null,
        [FromQuery] string? profileKey = null,
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var rows = await _service.LowCtrPhrasesAsync(days, take ?? DefaultTake, profileKey, locale, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
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
