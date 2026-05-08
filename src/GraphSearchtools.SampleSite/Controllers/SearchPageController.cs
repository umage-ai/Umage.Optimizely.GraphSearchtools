using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.Pages;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.ViewModels;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;
using Microsoft.AspNetCore.Mvc;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

public class SearchPageController : PageControllerBase<SearchPage>
{
    private readonly AlloySearchService _search;

    public SearchPageController(AlloySearchService search)
    {
        _search = search;
    }

    public async Task<IActionResult> Index(SearchPage currentPage, string q, [FromQuery(Name = "type")] string[] type, CancellationToken cancellationToken)
    {
        // Active language branch for the SearchPage. PageContext.LanguageID
        // is set by Optimizely's page route to the language code being served
        // (e.g. "en" / "sv"). Passing it through gives the service everything
        // it needs to filter by locale AND resolve the matching pinned-results
        // collection without the controller having to know the formula.
        var locale = PageContext?.LanguageID;

        var model = new SearchContentModel(currentPage)
        {
            SearchedQuery = q ?? string.Empty,
            ActiveLocale = locale
        };

        try
        {
            var result = await _search.SearchAsync(new AlloySearchRequest
            {
                Query = q,
                SelectedContentTypes = type ?? Array.Empty<string>(),
                Locale = locale
            }, cancellationToken);

            if (!result.Configured)
            {
                model.SearchServiceDisabled = true;
            }
            else
            {
                model.Hits = result.Hits;
                model.NumberOfHits = result.Total;
                model.ContentTypeFacet = result.ContentTypeFacet;
            }
        }
        catch (Exception ex)
        {
            // Misconfigured tenants and gateway hiccups land here. The page
            // shows a banner instead of yelling at end users with a stack
            // trace; the demo's typical failure mode is empty Graph creds.
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }
}
