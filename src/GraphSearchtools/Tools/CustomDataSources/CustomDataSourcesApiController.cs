using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources;

/// <summary>
/// REST API for the Custom Data Sources tool. Mounted under
/// <c>{basePath}/CustomDataSourcesApi/{action}</c> by the convention route.
/// v1 surface is intentionally minimal: list registered sources and trigger
/// a full resync. Source registration happens out-of-band — content
/// engineers configure non-CMS pipelines themselves; this tool only inspects
/// + nudges.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class CustomDataSourcesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.CustomDataSources);

    private readonly CustomDataSourcesService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<CustomDataSourcesApiController> _logger;

    public CustomDataSourcesApiController(
        CustomDataSourcesService service,
        FeatureAccessChecker accessChecker,
        ILogger<CustomDataSourcesApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.ListAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Sync([FromQuery] string name, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { message = "Source name is required." });
        }

        try
        {
            await _service.TriggerSyncAsync(name, cancellationToken);
            // 202 Accepted — Graph will run the resync asynchronously; the
            // page polls List() to surface the resulting status change.
            return Accepted();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.CustomDataSources);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Graph data sources request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph data sources request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
        if (exception is ArgumentException argEx)
        {
            _logger.LogWarning(argEx, "Data source request rejected: {Reason}.", argEx.Message);
            return BadRequest(new { message = "Invalid data source request." });
        }

        _logger.LogError(exception, "Unhandled error in CustomDataSourcesApiController.");
        return Problem(title: "Custom Data Sources API request failed.");
    }
}
