using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.Pages;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.ViewModels;

public class SearchContentModel : PageViewModel<SearchPage>
{
    public SearchContentModel(SearchPage currentPage)
        : base(currentPage)
    {
    }

    public bool SearchServiceDisabled { get; set; }

    public string SearchedQuery { get; set; }

    public int NumberOfHits { get; set; }

    public IEnumerable<AlloySearchHit> Hits { get; set; } = Array.Empty<AlloySearchHit>();

    public FacetGroup ContentTypeFacet { get; set; } = new();

    /// <summary>
    /// Language branch that scoped this search — set from
    /// <c>PageContext.LanguageID</c>. Rendered as a small label on the SERP
    /// so visitors know they're seeing only the active branch's content.
    /// </summary>
    public string ActiveLocale { get; set; }

    /// <summary>
    /// Runtime error the search service surfaced, e.g. a Graph 401 from a
    /// misconfigured tenant. Rendered as an inline notice on the page.
    /// </summary>
    public string ErrorMessage { get; set; }
}
