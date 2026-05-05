using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;

/// <summary>
/// REST API for the Semantic Weight Tuner. Two routes:
/// <list type="bullet">
///   <item><c>GET /SemanticTunerApi/Get</c> — return the persisted policy or the bound-config default.</item>
///   <item><c>POST /SemanticTunerApi/Save</c> — persist a new policy. Validation rejects overlapping tier ranges with a 400.</item>
/// </list>
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SemanticTunerApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SemanticTuner);

    private readonly SemanticTunerService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SemanticTunerApiController> _logger;

    public SemanticTunerApiController(
        SemanticTunerService service,
        FeatureAccessChecker accessChecker,
        ILogger<SemanticTunerApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_service.GetPolicy());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic tuner GET failed.");
            return Problem(title: "Semantic tuner request failed.");
        }
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult Save([FromBody] SemanticTuningPolicy? policy)
    {
        if (!HasAccess()) return Forbid();
        if (policy == null)
        {
            return BadRequest(new { message = "Policy body is required." });
        }

        var error = SemanticTunerService.Validate(policy);
        if (error != null)
        {
            return BadRequest(new { message = error });
        }

        try
        {
            var actor = HttpContext.User?.Identity?.Name;
            _service.SavePolicy(policy, actor);
            return Ok(_service.GetPolicy());
        }
        catch (ArgumentException argEx)
        {
            // Validation race — Validate() already ran, but guard anyway.
            _logger.LogWarning(argEx, "Semantic tuner save rejected: {Reason}.", argEx.Message);
            return BadRequest(new { message = argEx.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic tuner POST failed.");
            return Problem(title: "Semantic tuner save failed.");
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.SemanticTuner);
}
