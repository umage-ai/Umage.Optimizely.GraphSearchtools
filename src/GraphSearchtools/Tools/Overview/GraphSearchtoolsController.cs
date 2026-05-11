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
    public IActionResult Webhooks()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Webhooks), GraphSearchtoolsPermissions.Webhooks))
            return Forbid();
        return View("/Views/Webhooks/Index.cshtml");
    }

    /// <summary>
    /// Phase 3 — Semantic Weight Tuner. The dedicated
    /// <c>SemanticTunerController</c> hosts the canonical menu URL; this
    /// action preserves the per-tool action pattern shared with Webhooks so
    /// direct links to
    /// <c>/cms/graphsearchtools/GraphSearchtools/SemanticTuner</c> resolve too.
    /// </summary>
    [HttpGet]
    public IActionResult SemanticTuner()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.SemanticTuner), GraphSearchtoolsPermissions.SemanticTuner))
            return Forbid();
        return View("/Views/SemanticTuner/Index.cshtml");
    }

    /// <summary>
    /// Phase 4 Wave 5 — Synonym Coverage. Read-only analyzer that joins the
    /// saved synonym blobs with the search-log table to surface unused
    /// entries (prune candidates) and zero-result phrases that look like
    /// missing synonyms (suggested adds).
    /// </summary>
    [HttpGet]
    public IActionResult SynonymCoverage()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.SynonymCoverage), GraphSearchtoolsPermissions.SynonymCoverage))
            return Forbid();
        return View("/Views/SynonymCoverage/Index.cshtml");
    }

    /// <summary>
    /// Phase 4 Wave 5 §6 — Pinned Result Coverage audit. Joins Graph pinned
    /// data with CMS content state and the 7-day search-log window to surface
    /// broken targets (unpublished/deleted), expired pins, low-CTR pins,
    /// no-activity pins, and overlap conflicts.
    /// </summary>
    [HttpGet]
    public IActionResult PinnedCoverage()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.PinnedCoverage), GraphSearchtoolsPermissions.PinnedCoverage))
            return Forbid();
        return View("/Views/PinnedCoverage/Index.cshtml");
    }

    /// <summary>
    /// Phase 4 Wave 5 §6 — Content Searchability Audit. Renders the runner
    /// page; the actual scan kicks off via
    /// <c>POST /ContentSearchabilityAuditApi/Run</c>. The view is gated on the
    /// matching feature toggle + EPiServer permission so direct URL hits respect
    /// the same access checks the menu does.
    /// </summary>
    [HttpGet]
    public IActionResult ContentSearchabilityAudit()
    {
        if (!_accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.ContentSearchabilityAudit), GraphSearchtoolsPermissions.ContentSearchabilityAudit))
            return Forbid();
        return View("/Views/ContentSearchabilityAudit/Index.cshtml");
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
