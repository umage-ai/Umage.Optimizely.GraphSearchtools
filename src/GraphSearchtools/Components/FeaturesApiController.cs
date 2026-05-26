using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Components;

/// <summary>
/// Returns which top-level Graph Search Tools surfaces the current user can
/// reach. Used by the client-side module initializer to conditionally register
/// menu commands. Each entry is <c>true</c> only when both the feature toggle
/// and the user's permission grant pass.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
internal class FeaturesApiController : Controller
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
            Channels = _accessChecker.HasAccess(HttpContext,
                nameof(FeatureToggles.Channels), GraphSearchtoolsPermissions.Channels),
            Insights = _accessChecker.HasAccess(HttpContext,
                nameof(FeatureToggles.Insights), GraphSearchtoolsPermissions.Insights),
            Pinned = _accessChecker.HasAccess(HttpContext,
                nameof(FeatureToggles.Pinned), GraphSearchtoolsPermissions.Pinned),
            Synonyms = _accessChecker.HasAccess(HttpContext,
                nameof(FeatureToggles.Synonyms), GraphSearchtoolsPermissions.Synonyms),
        });
    }
}
