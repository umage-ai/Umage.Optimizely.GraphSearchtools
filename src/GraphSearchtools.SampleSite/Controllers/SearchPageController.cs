using EPiServer.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.Pages;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.ViewModels;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

public class SearchPageController : PageControllerBase<SearchPage>
{
    /// <summary>
    /// Profile key the storefront search runs under. Matches the registration
    /// in <c>Startup.AddSearchProfile("alloy-search", …)</c> so telemetry rolls
    /// up against the same profile that admins tune in the addon UI.
    /// </summary>
    private const string ProfileKey = "alloy-search";

    private readonly AlloySearchService _search;
    private readonly ITelemetrySink _telemetry;
    private readonly ILogger<SearchPageController> _logger;

    public SearchPageController(
        AlloySearchService search,
        ITelemetrySink telemetry,
        ILogger<SearchPageController> logger)
    {
        _search = search;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<IActionResult> Index(SearchPage currentPage, string q, [FromQuery(Name = "type")] string[] type, CancellationToken cancellationToken)
    {
        // Active language branch for the SearchPage. PageContext.LanguageID
        // is set by Optimizely's page route to the language code being served
        // (e.g. "en" / "sv"). Passing it through gives the service everything
        // it needs to filter by locale AND resolve the matching pinned-results
        // collection without the controller having to know the formula.
        var locale = PageContext?.LanguageID;
        var trimmedPhrase = (q ?? string.Empty).Trim();
        var hasPhrase = trimmedPhrase.Length > 0;

        var model = new SearchContentModel(currentPage)
        {
            SearchedQuery = q ?? string.Empty,
            ActiveLocale = locale
        };

        AlloySearchResult? result = null;
        try
        {
            result = await _search.SearchAsync(new AlloySearchRequest
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

        // Emit a search event into the local telemetry sink. The sink is
        // non-blocking (bounded channel + DropOldest), so the request thread
        // doesn't wait on storage. We bypass the HTTP beacon endpoint and
        // call the sink directly because the SampleSite is co-located with
        // the addon — same data plane, no JSON round-trip.
        if (hasPhrase)
        {
            var nowUtc = DateTime.UtcNow;
            try
            {
                _telemetry.Record(new SearchEvent(
                    Phrase: trimmedPhrase,
                    ProfileKey: ProfileKey,
                    Locale: (locale ?? string.Empty).ToLowerInvariant(),
                    ResultCount: result?.Configured == true && model.ErrorMessage == null
                        ? result.Total
                        : 0,
                    TimestampUtc: nowUtc));

                // Stamp the originating bucket on the model so the SERP can
                // echo it back on click events. Per design §5 this is what
                // makes click attribution survive the search bucket rolling
                // over before the click arrives.
                model.SearchBucketUtc = TruncateToMinute(nowUtc);
            }
            catch (Exception ex)
            {
                // Defense in depth — Record is contractually total, but a
                // logged warning beats a swallowed bug if that ever changes.
                _logger.LogWarning(ex, "Search telemetry record failed for phrase '{Phrase}'.", trimmedPhrase);
            }
        }

        return View(model);
    }

    private static DateTime TruncateToMinute(DateTime utc)
        => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
}
