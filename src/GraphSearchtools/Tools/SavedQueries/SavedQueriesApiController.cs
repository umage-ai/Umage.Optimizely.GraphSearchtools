using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

/// <summary>
/// Internal runner endpoint used by the Pinned tab's A/B preview. The Saved
/// Queries top-level surface was removed in favour of relying on Graph's own
/// GraphiQL playground for ad-hoc query exploration. The class name and
/// <c>/SavedQueriesApi</c> route are preserved so the Pinned JS keeps hitting
/// <c>/SavedQueriesApi/Run</c> unmodified.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
public class SavedQueriesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Pinned);

    private readonly QueryRunnerService _runner;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SavedQueriesApiController> _logger;

    public SavedQueriesApiController(
        QueryRunnerService runner,
        FeatureAccessChecker accessChecker,
        ILogger<SavedQueriesApiController> logger)
    {
        _runner = runner;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    /// <summary>
    /// Executes a Graph query and returns the hits plus the literal GraphQL
    /// document we sent. Only consumer is the Pinned tab's A/B side panel —
    /// gated on the Pinned feature toggle / permission accordingly.
    /// </summary>
    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Run([FromBody] RunnerRequest request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Run request is required." });

        try
        {
            var result = await _runner.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Saved Queries run failed with status {StatusCode}. Graph response body: {Body}", ex.StatusCode, ex.ResponseContent);
            return StatusCode(ex.StatusCode, new { message = "Graph query failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Saved Queries run rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Pinned);
}
