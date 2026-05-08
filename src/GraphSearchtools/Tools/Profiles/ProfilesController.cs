using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;

/// <summary>
/// Serves the Razor views for the Profiles top-level surface (index +
/// per-profile detail page). API calls go through
/// <see cref="ProfilesApiController"/>.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
[Route("EPiServer/cms/graphsearchtools/profiles")]
public class ProfilesController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Profiles);

    private readonly ProfilesService _service;
    private readonly FeatureAccessChecker _accessChecker;

    public ProfilesController(ProfilesService service, FeatureAccessChecker accessChecker)
    {
        _service = service;
        _accessChecker = accessChecker;
    }

    /// <summary>
    /// Renders the profiles index when called without <paramref name="key"/>,
    /// otherwise renders the profile detail page for that key.
    /// </summary>
    /// <remarks>
    /// Both surfaces are served from the same controller URL on purpose. The
    /// CMS 12 platform shell maps URL → product-id (e.g. <c>global_cms</c>)
    /// from the set of registered menu URLs; a deep URL like
    /// <c>/profiles/alloy-search</c> doesn't match any menu item, so the shell
    /// falls back to <c>data-epi-product-id=""</c>, which 400s the
    /// <c>/EPiServer/CMS/stores/notification</c> XHR and leaves the sidebar
    /// stuck on the loading dots. Keeping the key as a query parameter
    /// preserves the menu's <c>/profiles</c> URL match.
    /// </remarks>
    [HttpGet("")]
    public IActionResult Index([FromQuery] string? key = null)
    {
        if (!HasAccess()) return Forbid();

        if (!string.IsNullOrEmpty(key))
        {
            var model = _service.BuildDetailViewModel(key);
            if (model == null) return NotFound();
            return View("/Views/Profiles/Detail.cshtml", model);
        }

        return View("/Views/Profiles/Index.cshtml");
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Profiles);
}
