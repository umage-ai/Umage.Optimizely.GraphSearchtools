using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;

/// <summary>
/// REST API for the Synonym Coverage tool. Mounted under
/// <c>{basePath}/SynonymCoverageApi/{action}</c> by the convention route. The
/// surface is read-only — clicks back to the Synonyms editor produce links
/// in the UI rather than POSTs.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
public class SynonymCoverageApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SynonymCoverage);

    private readonly SynonymCoverageService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SynonymCoverageApiController> _logger;

    public SynonymCoverageApiController(
        SynonymCoverageService service,
        FeatureAccessChecker accessChecker,
        ILogger<SynonymCoverageApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var result = await _service.AnalyzeAsync(cancellationToken);
            return Ok(result);
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
        if (exception is GraphSearchApiException apiException)
        {
            _logger.LogWarning(apiException, "Graph synonym-coverage request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Synonym coverage request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in SynonymCoverageApiController.");
        return Problem(title: "Synonym Coverage API request failed.");
    }
}
