using EPiServer.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Localization;
using UmageAI.Optimizely.GraphSearchTools.Menu;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

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
    public IActionResult SynonymCoverage() => RedirectPermanent(ChannelsUrl());

    /// <summary>Legacy URL — Pinned Coverage now lives inside each Channel detail.</summary>
    [HttpGet]
    public IActionResult PinnedCoverage() => RedirectPermanent(ChannelsUrl());

    // Resolve the channels surface URL through Paths.ToResource so the
    // redirect lands on the same module base path the rest of the menu uses
    // (e.g. /Optimizely/GraphSearchtools on CMS 13).
    private static string ChannelsUrl() => Paths.ToResource(typeof(GraphSearchtoolsMenuProvider), "Channels/Index");

    /// <summary>
    /// Returns all UI strings as JSON for CMS shell widgets that cannot access window.GST_STRINGS.
    /// </summary>
    [HttpGet]
    public IActionResult WidgetStrings()
    {
        return Json(_uiStrings.GetAll());
    }

    /// <summary>
    /// Live setup sanity check. Returns the same diagnostics
    /// <see cref="StartupDiagnostics"/> logs at boot, plus the registered
    /// channels, credential configuration state (booleans only — no
    /// secret values are echoed), feature toggles, and the telemetry
    /// pipeline state. Intended for AI agents verifying a fresh install
    /// and for human operators looking for an actionable diagnostic
    /// string instead of inferring success from the absence of errors.
    /// </summary>
    [HttpGet]
    public IActionResult Health(
        [FromServices] IOptions<GraphSearchtoolsOptions> options,
        [FromServices] ISearchChannelRegistry registry,
        [FromServices] IServiceProvider services)
    {
        var opts = options.Value;
        var channels = registry.All;
        var sink = services.GetService<ITelemetrySink>();
        var metrics = services.GetService<ITelemetryMetrics>();
        var hasLocalSink = sink is LocalTelemetrySink;

        var diagnostics = StartupDiagnostics.Evaluate(opts, channels, hasLocalSink);

        var status = diagnostics.Any(d => d.Level == DiagnosticLevel.Error)
            ? "misconfigured"
            : diagnostics.Any(d => d.Level == DiagnosticLevel.Warning)
                ? "warnings"
                : "ok";

        var creds = opts.Graph;
        var assemblyVersion = typeof(GraphSearchtoolsController).Assembly
            .GetName().Version?.ToString();

        return Json(new
        {
            version = assemblyVersion,
            status,
            diagnostics = diagnostics.Select(d => new
            {
                level = d.Level.ToString().ToLowerInvariant(),
                code = d.Code,
                message = d.Message,
            }),
            channels = channels.Select(c => new
            {
                key = c.Key,
                displayName = c.DisplayName.ToString(),
                description = c.Description?.ToString(),
                locales = c.Locales,
                sites = c.Sites,
                searchedFields = c.SearchedFields,
                hasGraphQLDocument = !string.IsNullOrEmpty(c.GraphQLDocumentPath)
                    || !string.IsNullOrEmpty(c.GraphQLDocumentContent),
            }),
            credentials = new
            {
                source = creds == null
                    ? "Optimizely:ContentGraph (host config)"
                    : "UmageAI:GraphSearchTools:Graph (override)",
                gatewayAddress = creds?.GatewayAddress,
                appKeyConfigured = !string.IsNullOrWhiteSpace(creds?.AppKey),
                secretConfigured = !string.IsNullOrWhiteSpace(creds?.Secret),
                singleKeyConfigured = !string.IsNullOrWhiteSpace(creds?.SingleKey),
            },
            features = opts.Features,
            authorizedRoles = opts.AuthorizedRoles,
            telemetry = new
            {
                sink = hasLocalSink ? "local" : (sink == null ? "none" : "external"),
                queueDepth = metrics?.ApproximateQueueDepth,
                queueCapacity = metrics?.QueueCapacity,
                dropped = metrics?.ApproximateDroppedTotal,
            },
        });
    }
}
