using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;

/// <summary>
/// Serves the Razor view for the Semantic Weight Tuner. The page itself is a
/// thin client + a small persistence API; this controller's only job is the
/// view-render.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SemanticTunerController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;

    public SemanticTunerController(FeatureAccessChecker accessChecker)
    {
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.SemanticTuner), GraphSearchtoolsPermissions.SemanticTuner))
            return Forbid();
        return View("/Views/SemanticTuner/Index.cshtml");
    }
}
