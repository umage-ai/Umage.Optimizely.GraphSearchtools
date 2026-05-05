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

    [HttpGet("")]
    public IActionResult Index()
    {
        if (!HasAccess()) return Forbid();
        return View("/Views/Profiles/Index.cshtml");
    }

    [HttpGet("{key}")]
    public IActionResult Detail(string key)
    {
        if (!HasAccess()) return Forbid();

        var model = _service.BuildDetailViewModel(key);
        if (model == null) return NotFound();

        return View("/Views/Profiles/Detail.cshtml", model);
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Profiles);
}
