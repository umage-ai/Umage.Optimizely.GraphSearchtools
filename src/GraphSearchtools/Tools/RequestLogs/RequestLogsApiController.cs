using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs;

/// <summary>
/// REST API for the Request Logs tool. Mounted under
/// <c>{basePath}/RequestLogsApi/{action}</c> by the convention route. List
/// only — the underlying Graph endpoint is read-only and replay is handled
/// client-side by piping a row's query/variables back into Search Console.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class RequestLogsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.RequestLogs);

    private readonly RequestLogsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<RequestLogsApiController> _logger;

    public RequestLogsApiController(
        RequestLogsService service,
        FeatureAccessChecker accessChecker,
        ILogger<RequestLogsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? take, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var requested = take ?? RequestLogsService.DefaultTake;
            return Ok(await _service.ListAsync(requested, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.RequestLogs);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            _logger.LogWarning(apiException, "Graph request-logs request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph request-logs request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in RequestLogsApiController.");
        return Problem(title: "Request Logs API request failed.");
    }
}
