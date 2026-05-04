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

        // Editorial group (Phase 1): Pinned + Synonyms.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/pinned", "Pinned Results"), BaseMenuPath + "/pinned",
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

        yield return new UrlMenuItem(L("/graphsearchtools/menu/autocomplete", "Autocomplete"), BaseMenuPath + "/autocomplete",
            GetResourcePath("GraphSearchtools/Autocomplete"))
        {
            SortIndex = 310,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.Autocomplete))
        };

        // Playground group (Phase 2): Saved Queries — combined runner + presets.
        yield return new UrlMenuItem(L("/graphsearchtools/menu/savedqueries", "Saved Queries"), BaseMenuPath + "/savedqueries",
            GetResourcePath("GraphSearchtools/SavedQueries"))
        {
            SortIndex = 400,
            IsAvailable = context => IsFeatureEnabled(context, nameof(FeatureToggles.SavedQueries))
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
