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

        // Phase 2.5 — Search Channels top-level surface. Sits between Overview
        // and the editorial tools so marketers land on the per-surface tuning
        // index before drilling into individual data shapes.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/channels", "Search channels"), BaseMenuPath + "/channels",
            "/EPiServer/cms/graphsearchtools/channels")
        {
            SortIndex = 150,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Channels))
        };

        // Aurora refactor — Insights dashboard. Sits between Channels and the
        // editorial Pinned/Synonyms tools: marketers can see "what's
        // happening" before deciding what to tune.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/insights", "Insights"), BaseMenuPath + "/insights",
            GetResourcePath("Insights/Index"))
        {
            SortIndex = 175,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Insights))
        };

        // Top-level cross-channel surfaces. Pinned is a collection-axis
        // browser (prototype, read-only for now); Synonyms is channel-agnostic
        // because Graph synonyms live in a tenant-global pool.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/pinned", "Pinned results"), BaseMenuPath + "/pinned",
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

        // About sits at the bottom of the section so the colophon stays
        // accessible without crowding the marketer's primary tool list.
        // SortIndex 900 leaves room for future tools to slot in between.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/about", "About"), BaseMenuPath + "/about",
            GetResourcePath("GraphSearchtools/About"))
        {
            SortIndex = 900,
            IsAvailable = _ => true
        };

        // Legacy /pinnedcoverage and /synonymcoverage URLs still 301 to
        // /channels via GraphSearchtoolsController.
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
