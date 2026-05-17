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
        yield return new UrlMenuItem(L("/graphsearchtools/menu/profiles", "Search profiles"), BaseMenuPath + "/profiles",
            "/EPiServer/cms/graphsearchtools/profiles")
        {
            SortIndex = 150,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Profiles))
        };

        // Aurora refactor — Insights dashboard. Sits between Profiles and the
        // editorial Pinned/Synonyms tools: marketers can see "what's
        // happening" before deciding what to tune.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/insights", "Insights"), BaseMenuPath + "/insights",
            GetResourcePath("Insights/Index"))
        {
            SortIndex = 175,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Insights))
        };

        // Editorial group: top-level Pinned + Synonyms tools — global views
        // that mirror the per-profile tabs inside Profile detail. The Pinned
        // tool also absorbs the former Pinned Coverage as an "Audit" tab.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/pinned", "Pinned"), BaseMenuPath + "/pinned",
            GetResourcePath("GraphSearchtools/Pinned"))
        {
            SortIndex = 200,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Pinned))
        };

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

        yield return new UrlMenuItem(L("/graphsearchtools/menu/autocomplete", "Autocomplete check"), BaseMenuPath + "/autocomplete",
            GetResourcePath("GraphSearchtools/Autocomplete"))
        {
            SortIndex = 310,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Autocomplete))
        };

        // Pinned audit surfacing lives in Insights — no standalone menu
        // entry, no Pinned sub-tab. Legacy /pinnedcoverage URL still 301s
        // to /Pinned via GraphSearchtoolsController.PinnedCoverage.

        // Synonym Coverage absorbed into the Synonyms tool as an "Unused" tab
        // — no standalone menu entry. Legacy /synonymcoverage URL still 301s
        // to /synonyms#unused via GraphSearchtoolsController.SynonymCoverage.
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
