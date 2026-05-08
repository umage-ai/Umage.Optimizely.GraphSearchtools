using Microsoft.AspNetCore.Mvc;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

[Route("api/search/suggest")]
public class SearchSuggestController : Controller
{
    private readonly AlloySearchService _search;

    public SearchSuggestController(AlloySearchService search)
    {
        _search = search;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string q, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            return Json(Array.Empty<string>());
        }
        var suggestions = await _search.SuggestAsync(q, limit <= 0 ? 8 : limit, cancellationToken);
        return Json(suggestions);
    }
}
