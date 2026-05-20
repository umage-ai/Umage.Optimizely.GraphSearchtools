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
[Authorize(Policy = "umageai:graphsearchtools")]
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
    /// About / colophon screen. Mirrors the EditorPowertools About surface so
    /// both addons present a consistent identity (version, license, included
    /// tools, "built by umage.ai" promo). Linked from every page's header
    /// rather than the left-nav so it stays one click away without crowding
    /// the marketer's primary tool list.
    /// </summary>
    [HttpGet]
    public IActionResult About()
    {
        return View("/Views/About/Index.cshtml");
    }

    /// <summary>
    /// Top-level Pinned tool — collection-axis browser. Reads collections + items
    /// from Graph and joins with the channel registry so each row carries its
    /// resolved channel. Prototype is read-only; edits still happen inside
    /// Channel detail tabs.
    /// </summary>
    [HttpGet]
    public IActionResult Pinned()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Pinned), GraphSearchtoolsPermissions.Pinned))
            return Forbid();
        return View("/Views/Pinned/Index.cshtml");
    }

    /// <summary>
    /// Top-level Synonyms tool — talks straight to Graph's synonym admin.
    /// Channel-agnostic by design: Graph synonyms live in a tenant-global pool.
    /// </summary>
    [HttpGet]
    public IActionResult Synonyms()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Synonyms), GraphSearchtoolsPermissions.Synonyms))
            return Forbid();
        return View("/Views/Synonyms/Index.cshtml");
    }

    /// <summary>Legacy URL — Synonym Coverage now lives inside each Channel detail.</summary>
    [HttpGet]
    public IActionResult SynonymCoverage() => RedirectPermanent("/EPiServer/cms/graphsearchtools/channels");

    /// <summary>Legacy URL — Pinned Coverage now lives inside each Channel detail.</summary>
    [HttpGet]
    public IActionResult PinnedCoverage() => RedirectPermanent("/EPiServer/cms/graphsearchtools/channels");

    /// <summary>
    /// Returns all UI strings as JSON for CMS shell widgets that cannot access window.GST_STRINGS.
    /// </summary>
    [HttpGet]
    public IActionResult WidgetStrings()
    {
        return Json(_uiStrings.GetAll());
    }
}
