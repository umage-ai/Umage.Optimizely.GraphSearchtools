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
    /// Phase 2.5 §4.1: Pinned no longer has a standalone page. Marketers edit
    /// pinned results via Profiles → {profile} → Pinned tab. We keep this
    /// action for hosts that linked directly to <c>/pinned</c> and 301-redirect
    /// to the Profiles index — they'll click into whichever profile they need.
    /// </summary>
    [HttpGet]
    public IActionResult Pinned()
    {
        return RedirectPermanent("/EPiServer/cms/graphsearchtools/profiles");
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

    [HttpGet]
    public IActionResult DecaySandbox()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.DecaySandbox), GraphSearchtoolsPermissions.DecaySandbox))
            return Forbid();
        return View("/Views/DecaySandbox/Index.cshtml");
    }

    [HttpGet]
    public IActionResult Webhooks()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Webhooks), GraphSearchtoolsPermissions.Webhooks))
            return Forbid();
        return View("/Views/Webhooks/Index.cshtml");
    }

    /// <summary>
    /// Phase 3 — Semantic Weight Tuner. The dedicated
    /// <c>SemanticTunerController</c> hosts the canonical menu URL; this
    /// action preserves the per-tool action pattern shared with DecaySandbox /
    /// Webhooks so direct links to
    /// <c>/cms/graphsearchtools/GraphSearchtools/SemanticTuner</c> resolve too.
    /// </summary>
    [HttpGet]
    public IActionResult SemanticTuner()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.SemanticTuner), GraphSearchtoolsPermissions.SemanticTuner))
            return Forbid();
        return View("/Views/SemanticTuner/Index.cshtml");
    }

    [HttpGet]
    public IActionResult CustomDataSources()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.CustomDataSources), GraphSearchtoolsPermissions.CustomDataSources))
            return Forbid();
        return View("/Views/CustomDataSources/Index.cshtml");
    }

    [HttpGet]
    public IActionResult RequestLogs()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.RequestLogs), GraphSearchtoolsPermissions.RequestLogs))
            return Forbid();
        return View("/Views/RequestLogs/Index.cshtml");
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
