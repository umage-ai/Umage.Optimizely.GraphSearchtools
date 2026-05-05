using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

/// <summary>
/// REST API for the Synonyms tool. Each language has its own synonym blob
/// addressed by <c>language_routing</c>; the "Global" blob is reached by
/// passing no language. The wire format with Optimizely Graph is plain text —
/// the line-per-rule structure is enforced client-side and joined here.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SynonymsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Synonyms);

    private readonly SynonymsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SynonymsApiController> _logger;

    public SynonymsApiController(
        SynonymsService service,
        FeatureAccessChecker accessChecker,
        ILogger<SynonymsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? languageRouting,
        [FromQuery] string? sourceRouting,
        [FromQuery] string? slot,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var query = new SynonymsQuery
        {
            LanguageRouting = languageRouting,
            SourceRouting = sourceRouting,
            Slot = slot
        };

        try
        {
            var content = await _service.GetAsync(query, cancellationToken);
            return Ok(new SynonymsResponse { Content = content ?? string.Empty });
        }
        catch (GraphSearchApiException ex) when (ex.StatusCode == 404)
        {
            // No synonym set for this scope — return empty.
            return Ok(new SynonymsResponse { Content = string.Empty });
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPut]
    [RequireAjax]
    public async Task<IActionResult> Update([FromBody] SynonymsRequest request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Synonyms payload is required." });

        try
        {
            await _service.UpdateAsync(request, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> Delete(
        [FromQuery] string? languageRouting,
        [FromQuery] string? sourceRouting,
        [FromQuery] string? slot,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var query = new SynonymsQuery
        {
            LanguageRouting = languageRouting,
            SourceRouting = sourceRouting,
            Slot = slot
        };

        try
        {
            await _service.DeleteAsync(query, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Synonyms);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            _logger.LogWarning(apiException, "Graph synonyms request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph synonyms request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
        _logger.LogError(exception, "Unhandled error in SynonymsApiController.");
        return Problem(title: "Synonyms API request failed.");
    }
}
