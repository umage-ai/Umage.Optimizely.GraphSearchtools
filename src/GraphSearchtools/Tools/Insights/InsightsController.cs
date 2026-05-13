using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights;

/// <summary>
/// Serves the Razor view for the Insights dashboard. Read APIs live in
/// <see cref="InsightsApiController"/>.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class InsightsController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;

    public InsightsController(FeatureAccessChecker accessChecker)
    {
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Insights), GraphSearchtoolsPermissions.Insights))
            return Forbid();
        return View("/Views/Insights/Index.cshtml");
    }
}
