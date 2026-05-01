using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Connectivity;

/// <summary>
/// One read-only endpoint that returns the four-probe connectivity status. The
/// view auto-refreshes from this; support can screenshot the rendered page
/// without leaking credentials.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class ConnectivityApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Connectivity);

    private readonly ConnectivityService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<ConnectivityApiController> _logger;

    public ConnectivityApiController(
        ConnectivityService service,
        FeatureAccessChecker accessChecker,
        ILogger<ConnectivityApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Check(CancellationToken cancellationToken)
    {
        if (!_accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Connectivity))
        {
            return Forbid();
        }

        try
        {
            var result = await _service.CheckAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connectivity probes failed unexpectedly.");
            return Problem(title: "Connectivity check failed.");
        }
    }
}
