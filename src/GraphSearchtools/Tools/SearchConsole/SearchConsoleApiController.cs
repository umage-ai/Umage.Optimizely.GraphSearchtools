using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchConsole;

/// <summary>
/// Single endpoint that runs a Graph search per the request body and returns
/// hits + the literal GraphQL document we sent to Graph (so the user can copy
/// it). Side-by-side comparisons in the UI just call this twice with two
/// different requests.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SearchConsoleApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SearchConsole);

    private readonly SearchConsoleService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SearchConsoleApiController> _logger;

    public SearchConsoleApiController(
        SearchConsoleService service,
        FeatureAccessChecker accessChecker,
        ILogger<SearchConsoleApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Run([FromBody] SearchRequest request, CancellationToken cancellationToken)
    {
        if (!_accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.SearchConsole))
        {
            return Forbid();
        }
        if (request == null) return BadRequest(new { message = "Search request is required." });

        try
        {
            var result = await _service.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Search Console request failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Graph query failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Search Console request rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }
}
