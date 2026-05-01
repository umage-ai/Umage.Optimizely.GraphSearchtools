using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Components;

/// <summary>
/// API endpoints for per-user tool preferences. Shared across all tools.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
[RequireAjax]
public class PreferencesApiController : Controller
{
    private readonly UserPreferencesService _preferencesService;
    private readonly FeatureAccessChecker _accessChecker;

    public PreferencesApiController(UserPreferencesService preferencesService, FeatureAccessChecker accessChecker)
    {
        _preferencesService = preferencesService;
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string id)
    {
        if (!_accessChecker.IsFeatureEnabled(id))
            return Forbid();

        var username = HttpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var json = _preferencesService.Get(username, id);
        if (json == null)
            return Ok(new { });

        return Content(json, "application/json");
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromQuery] string id)
    {
        if (!_accessChecker.IsFeatureEnabled(id))
            return Forbid();

        var username = HttpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();

        _preferencesService.Save(username, id, json);
        return Ok(new { success = true });
    }
}
