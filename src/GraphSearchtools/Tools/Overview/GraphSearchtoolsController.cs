using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Localization;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Overview;

/// <summary>
/// Main controller for Graph Search Tools pages.
/// Actions map to menu items via Paths.ToResource("GraphSearchtools/{ActionName}").
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class GraphSearchtoolsController : Controller
{
    private readonly FeatureAccessChecker _accessChecker;
    private readonly UiStringsProvider _uiStrings;

    public GraphSearchtoolsController(
        FeatureAccessChecker accessChecker,
        UiStringsProvider uiStrings)
    {
        _accessChecker = accessChecker;
        _uiStrings = uiStrings;
    }

    [HttpGet]
    public IActionResult Overview()
    {
        return View("/Views/Overview/Index.cshtml");
    }

    /// <summary>
    /// Global Pinned tool — the cross-collection editor. Renders the merged
    /// view with Pins (default) and Audit tabs; Audit absorbs the former
    /// Pinned Coverage view. Phase 2.5 §4.1 originally redirected this to
    /// Profiles, but a global pin-management surface (matching the global
    /// Synonyms tool) is the right home for "see all my pins" workflows.
    /// </summary>
    [HttpGet]
    public IActionResult Pinned()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Pinned), GraphSearchtoolsPermissions.Pinned))
            return Forbid();
        return View("/Views/Pinned/Index.cshtml");
    }

    [HttpGet]
    public IActionResult Synonyms()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Synonyms), GraphSearchtoolsPermissions.Synonyms))
            return Forbid();
        return View("/Views/Synonyms/Index.cshtml");
    }

    [HttpGet]
    public IActionResult Health()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Health), GraphSearchtoolsPermissions.Health))
            return Forbid();
        return View("/Views/Health/Index.cshtml");
    }

    [HttpGet]
    public IActionResult Autocomplete()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Autocomplete), GraphSearchtoolsPermissions.Autocomplete))
            return Forbid();
        return View("/Views/Autocomplete/Index.cshtml");
    }

    /// <summary>
    /// Legacy URL for the standalone Synonym Coverage view. The view is gone
    /// — the unused-rules signal lives in the Aurora Synonyms grid's
    /// Activity (30d) column / filter, and the "suggested adds" half is
    /// being rebuilt as part of the per-profile insights pipeline. 301 to
    /// the Synonyms page so bookmarked links keep working.
    /// </summary>
    [HttpGet]
    public IActionResult SynonymCoverage()
    {
        return RedirectPermanent("/EPiServer/GraphSearchtools/GraphSearchtools/Synonyms");
    }

    /// <summary>
    /// The Pinned Audit tab has been removed; the audit surface lives in
    /// Insights now. Keep this action 301-redirecting old bookmarks to the
    /// Pinned grid rather than 404-ing.
    /// </summary>
    [HttpGet]
    public IActionResult PinnedCoverage()
    {
        return RedirectPermanent("/EPiServer/GraphSearchtools/GraphSearchtools/Pinned");
    }

    /// <summary>
    /// Returns all UI strings as JSON for CMS shell widgets that cannot access window.GST_STRINGS.
    /// </summary>
    [HttpGet]
    public IActionResult WidgetStrings()
    {
        return Json(_uiStrings.GetAll());
    }
}
