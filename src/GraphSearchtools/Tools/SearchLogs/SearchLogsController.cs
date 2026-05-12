using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

/// <summary>
/// Serves the Razor view for the Search Logs dashboard. Standalone controller
/// so the menu URL <c>/EPiServer/cms/graphsearchtools/searchlogs</c> resolves
/// against a tool-local route name. Read APIs live in
/// <see cref="SearchLogsApiController"/>.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SearchLogsController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;

    public SearchLogsController(FeatureAccessChecker accessChecker)
    {
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult Index()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.SearchLogs), GraphSearchtoolsPermissions.SearchLogs))
            return Forbid();
        return View("/Views/SearchLogs/Index.cshtml");
    }
}
