using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;

/// <summary>
/// Serves the Razor view for the Relevancy Lab. Standalone controller so the
/// menu URL <c>/EPiServer/cms/graphsearchtools/relevancylab</c> resolves
/// against a tool-local route name (mirrors <c>SemanticTunerController</c>).
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class RelevancyLabController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;

    public RelevancyLabController(FeatureAccessChecker accessChecker)
    {
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.RelevancyLab), GraphSearchtoolsPermissions.RelevancyLab))
            return Forbid();
        return View("/Views/RelevancyLab/Index.cshtml");
    }
}
