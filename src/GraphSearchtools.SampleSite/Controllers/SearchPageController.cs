using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.Pages;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

public class SearchPageController : PageControllerBase<SearchPage>
{
    public ViewResult Index(SearchPage currentPage, string q)
    {
        var model = new SearchContentModel(currentPage)
        {
            Hits = Enumerable.Empty<SearchContentModel.SearchHit>(),
            NumberOfHits = 0,
            SearchServiceDisabled = true,
            SearchedQuery = q
        };

        return View(model);
    }
}
