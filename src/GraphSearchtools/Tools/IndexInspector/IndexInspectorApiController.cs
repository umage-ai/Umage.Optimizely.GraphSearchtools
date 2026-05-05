using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector;

/// <summary>
/// REST API for the Index Inspector tool. Read-only — the tool surfaces a
/// snapshot of how the Graph index is populated per content type plus a
/// best-effort count of items missing the basic editorial fields. Refresh is
/// the only supported action, so we expose a single GET endpoint.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class IndexInspectorApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.IndexInspector);

    private readonly IndexInspectorService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<IndexInspectorApiController> _logger;

    public IndexInspectorApiController(
        IndexInspectorService service,
        FeatureAccessChecker accessChecker,
        ILogger<IndexInspectorApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.InspectAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.IndexInspector);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Graph index-inspector request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph index-inspector request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in IndexInspectorApiController.");
        return Problem(title: "Index Inspector API request failed.");
    }
}
