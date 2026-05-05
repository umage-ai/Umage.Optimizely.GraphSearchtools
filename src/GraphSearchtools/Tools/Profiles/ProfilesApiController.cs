using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;

/// <summary>
/// JSON API for the Profiles surface. Read-only in v1: writes for pinned /
/// synonym / saved-query data go through their existing controllers — see
/// docs/search-profiles-design.md §4.1–§4.3.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
[Route("EPiServer/cms/graphsearchtools/api/profiles")]
public class ProfilesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Profiles);

    private readonly ProfilesService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<ProfilesApiController> _logger;

    public ProfilesApiController(
        ProfilesService service,
        FeatureAccessChecker accessChecker,
        ILogger<ProfilesApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult List()
    {
        if (!HasAccess()) return Forbid();
        try { return Ok(_service.ListSummaries()); }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpGet("{key}")]
    public IActionResult Get(string key)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Profile key is required." });
        try
        {
            var detail = _service.BuildDetail(key);
            return detail == null ? NotFound() : Ok(detail);
        }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpGet("{key}/audit")]
    public IActionResult Audit(string key, [FromQuery] int take = 100)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Profile key is required." });
        var clamped = Math.Clamp(take, 1, 500);
        try { return Ok(_service.ListAudit(key, clamped)); }
        catch (Exception ex) { return Handle(ex); }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Profiles);

    private IActionResult Handle(Exception ex)
    {
        _logger.LogError(ex, "Profiles API error.");
        return Problem(title: "Profiles request failed.");
    }
}
