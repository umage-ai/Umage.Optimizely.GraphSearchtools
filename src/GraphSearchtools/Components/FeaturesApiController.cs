using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Components;

/// <summary>
/// Returns which Graph Search Tools features are enabled for the current user.
/// Used by the client-side module initializer to conditionally register commands.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
public class FeaturesApiController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;

    public FeaturesApiController(FeatureAccessChecker accessChecker)
    {
        _accessChecker = accessChecker;
    }

    [HttpGet]
    public IActionResult GetFeatures()
    {
        return Ok(new
        {
            Overview = _accessChecker.HasAccess(HttpContext,
                nameof(FeatureToggles.Overview),
                GraphSearchtoolsPermissions.Overview)
        });
    }
}
