using EPiServer.Framework.Localization;
using EPiServer.ServiceLocation;
using EPiServer.Shell;
using EPiServer.Shell.Navigation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Menu;

[MenuProvider]
public class GraphSearchtoolsMenuProvider : IMenuProvider
{
    private static readonly string BaseMenuPath = MenuPaths.Global + "/cms/graphsearchtools";
    private readonly LocalizationService _localization;

    public GraphSearchtoolsMenuProvider()
    {
        _localization = ServiceLocator.Current.GetInstance<LocalizationService>();
    }

    private string L(string path, string fallback) =>
        _localization.GetStringByCulture(path, fallback, System.Globalization.CultureInfo.CurrentUICulture);

    public IEnumerable<MenuItem> GetMenuItems()
    {
        yield return new SectionMenuItem(L("/graphsearchtools/menu/title", "Graph Search Tools"), BaseMenuPath)
        {
            Url = GetResourcePath("GraphSearchtools/Overview"),
            SortIndex = 510,
            IsAvailable = _ => true
        };

        yield return new UrlMenuItem(L("/graphsearchtools/menu/overview", "Overview"), BaseMenuPath + "/overview",
            GetResourcePath("GraphSearchtools/Overview"))
        {
            SortIndex = 100,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Overview))
        };

        // Phase 2.5 — Search Profiles top-level surface. Sits between Overview
        // and the editorial tools so marketers land on the per-surface tuning
        // index before drilling into individual data shapes.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/profiles", "Profiles"), BaseMenuPath + "/profiles",
            "/EPiServer/cms/graphsearchtools/profiles")
        {
            SortIndex = 150,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Profiles))
        };

        // Phase 2.5 §4.1: Pinned no longer has a top-level menu entry — pinned
        // results are always profile-scoped (Graph keys them per collection),
        // so marketers reach the editor via Profiles → {profile} → Pinned tab.
        // The legacy /pinned URL still serves a 301 redirect for hosts that
        // bookmark it (see GraphSearchtoolsController.Pinned). The Pinned
        // FeatureToggle now gates the in-profile tab instead.

        // Editorial group (Phase 1): Synonyms keeps its top-level entry
        // because Graph's Global synonym slot needs a tenant-level surface.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/synonyms", "Synonyms"), BaseMenuPath + "/synonyms",
            GetResourcePath("GraphSearchtools/Synonyms"))
        {
            SortIndex = 210,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Synonyms))
        };

        // Diagnostics group (Phase 2): Health first so it's the place editors
        // look when something else is misbehaving.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/health", "Health"), BaseMenuPath + "/health",
            GetResourcePath("GraphSearchtools/Health"))
        {
            SortIndex = 300,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Health))
        };

        yield return new UrlMenuItem(L("/graphsearchtools/menu/autocomplete", "Autocomplete"), BaseMenuPath + "/autocomplete",
            GetResourcePath("GraphSearchtools/Autocomplete"))
        {
            SortIndex = 310,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Autocomplete))
        };

        // Tuning power tools group (Phase 3) — Decay & Factor Sandbox is a
        // client-side preview, no Graph calls.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/decaySandbox", "Decay & Factor Sandbox"), BaseMenuPath + "/decaysandbox",
            GetResourcePath("GraphSearchtools/DecaySandbox"))
        {
            SortIndex = 410,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.DecaySandbox))
        };

        // Webhooks (Phase 3) — list/create/delete Graph webhooks. Edits are
        // delete + recreate per the upstream constraint; the page surfaces
        // that honestly via an info banner.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/webhooks", "Webhooks"), BaseMenuPath + "/webhooks",
            GetResourcePath("GraphSearchtools/Webhooks"))
        {
            SortIndex = 420,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Webhooks))
        };

        // Custom Data Sources (Phase 3) — read-only inspection of registered
        // non-CMS sources, with a per-source "trigger resync" action. New
        // sources are registered out-of-band by their own pipelines.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/customDataSources", "Custom Data Sources"), BaseMenuPath + "/customdatasources",
            GetResourcePath("GraphSearchtools/CustomDataSources"))
        {
            SortIndex = 430,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.CustomDataSources))
        };

        // Request Logs (Phase 3) — recent Graph queries with timing, status,
        // and ranking. Click-to-expand reveals the full GraphQL document and
        // a "Copy query" button so it can be replayed via Search Console.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/requestLogs", "Request Logs"), BaseMenuPath + "/requestlogs",
            GetResourcePath("GraphSearchtools/RequestLogs"))
        {
            SortIndex = 440,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.RequestLogs))
        };

        // Semantic Weight Tuner (Phase 3) — token-count-tiered ranking policy
        // editor. Pure client + tiny persistence layer; emits an
        // appsettings.json snippet alongside the saved DDS row.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/semanticTuner", "Semantic Weight Tuner"), BaseMenuPath + "/semantictuner",
            GetResourcePath("SemanticTuner/Index"))
        {
            SortIndex = 450,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.SemanticTuner))
        };

        yield return new UrlMenuItem(L("/graphsearchtools/menu/about", "About"), BaseMenuPath + "/about",
            GetResourcePath("GraphSearchtools/About"))
        {
            SortIndex = 900,
            IsAvailable = _ => true
        };
    }

    private static string GetResourcePath(string resourcePath)
    {
        return Paths.ToResource(typeof(GraphSearchtoolsMenuProvider), resourcePath);
    }

    private static bool IsFeatureEnabled(HttpContext context, string featureName)
    {
        var checker = context.RequestServices.GetService<FeatureAccessChecker>();
        return checker?.IsFeatureEnabled(featureName) ?? true;
    }
}
