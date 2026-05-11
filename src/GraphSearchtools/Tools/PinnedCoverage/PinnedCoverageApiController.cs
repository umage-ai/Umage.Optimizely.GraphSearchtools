using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage;

/// <summary>
/// Read-only REST API for the Phase 4 Pinned Result Coverage tool. Mounted
/// under <c>{basePath}/PinnedCoverageApi/{action}</c> by convention. Audit
/// composition is on-demand and may take a few seconds on tenants with many
/// collections — the JS shows a loading state while the request is in flight.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class PinnedCoverageApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.PinnedCoverage);

    private readonly PinnedCoverageService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<PinnedCoverageApiController> _logger;

    public PinnedCoverageApiController(
        PinnedCoverageService service,
        FeatureAccessChecker accessChecker,
        ILogger<PinnedCoverageApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Audit(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.RunAuditAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.PinnedCoverage);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Pinned coverage audit failed at Graph with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Pinned coverage audit rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in PinnedCoverageApiController.");
        return Problem(title: "Pinned coverage audit failed.");
    }
}
