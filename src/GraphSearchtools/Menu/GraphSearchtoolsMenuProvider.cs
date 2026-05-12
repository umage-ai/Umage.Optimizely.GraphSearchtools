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

        // Analytics & audits group (Phase 4 Wave 5). Search Logs leads the
        // group because it's the synonym-mining surface every other Wave 5
        // tool feeds off: top phrases, zero-result phrases, low-CTR phrases,
        // recent raw events.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/searchLogs", "Search Logs"), BaseMenuPath + "/searchlogs",
            GetResourcePath("SearchLogs/Index"))
        {
            SortIndex = 510,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.SearchLogs))
        };

        // Pinned Result Coverage (Phase 4 Wave 5 §6) — read-only audit that
        // joins Graph pinned data with CMS content state and the 7-day
        // search-log window. Surfaces broken targets (unpublished/deleted),
        // expired pins, low-CTR pins, no-activity pins, and overlap conflicts
        // (same phrase pinned in multiple collections).
        yield return new UrlMenuItem(L("/graphsearchtools/menu/pinnedCoverage", "Pinned Coverage"), BaseMenuPath + "/pinnedcoverage",
            GetResourcePath("GraphSearchtools/PinnedCoverage"))
        {
            SortIndex = 520,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.PinnedCoverage))
        };

        // Synonym Coverage (Phase 4 Wave 5) — joins the saved synonym blobs
        // with the search-log table to surface unused entries (prune
        // candidates) and zero-result phrases that look like missing synonyms
        // (suggested adds). Read-only; both tables deep-link into the
        // Synonyms editor.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/synonymCoverage", "Synonym Coverage"), BaseMenuPath + "/synonymcoverage",
            GetResourcePath("GraphSearchtools/SynonymCoverage"))
        {
            SortIndex = 530,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.SynonymCoverage))
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
