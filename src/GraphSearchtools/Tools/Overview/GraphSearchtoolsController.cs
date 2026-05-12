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
    /// Phase 4 Wave 5 — Synonym Coverage. Read-only analyzer that joins the
    /// saved synonym blobs with the search-log table to surface unused
    /// entries (prune candidates) and zero-result phrases that look like
    /// missing synonyms (suggested adds).
    /// </summary>
    /// <summary>
    /// Synonym Coverage was absorbed into the Synonyms tool as an "Unused"
    /// tab. We keep this action 301-redirecting to the new home so any
    /// bookmarked links keep working. The Synonyms page reads the #unused
    /// fragment on load and switches to the Unused tab.
    /// </summary>
    [HttpGet]
    public IActionResult SynonymCoverage()
    {
        return RedirectPermanent("/EPiServer/GraphSearchtools/GraphSearchtools/Synonyms#unused");
    }

    /// <summary>
    /// Pinned Coverage was absorbed into the Pinned tool as an "Audit" tab.
    /// We keep this action 301-redirecting so any bookmarked links keep
    /// working. The Pinned page reads the #audit fragment on load and
    /// switches to the Audit tab.
    /// </summary>
    [HttpGet]
    public IActionResult PinnedCoverage()
    {
        return RedirectPermanent("/EPiServer/GraphSearchtools/GraphSearchtools/Pinned#audit");
    }

    [HttpGet]
    public IActionResult About()
    {
        return View("/Views/About/Index.cshtml");
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
