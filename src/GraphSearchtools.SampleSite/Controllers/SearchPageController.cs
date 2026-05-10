using System.Diagnostics;
using EPiServer.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.Pages;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Models.ViewModels;
using UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

public class SearchPageController : PageControllerBase<SearchPage>
{
    /// <summary>
    /// Profile key the storefront search runs under. Matches the registration
    /// in <c>Startup.AddSearchProfile("alloy-search", …)</c> so telemetry rows
    /// roll up against the same profile that admins tune in the addon UI.
    /// </summary>
    private const string ProfileKey = "alloy-search";

    private readonly AlloySearchService _search;
    private readonly SearchLogService _searchLog;
    private readonly ILogger<SearchPageController> _logger;

    public SearchPageController(
        AlloySearchService search,
        SearchLogService searchLog,
        ILogger<SearchPageController> logger)
    {
        _search = search;
        _searchLog = searchLog;
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

        var stopwatch = Stopwatch.StartNew();
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
        finally
        {
            stopwatch.Stop();
        }

        // Emit a telemetry row for the Search Logs analytics tool. Co-located
        // host: we call SearchLogService.Append directly rather than POSTing
        // to the addon's TelemetryApi (which is editor-auth-gated and aimed
        // at out-of-process hosts pushing over HTTP).
        if (hasPhrase)
        {
            EmitTelemetry(
                phrase: trimmedPhrase,
                locale: locale,
                durationMs: (int)stopwatch.ElapsedMilliseconds,
                resultCount: result?.Configured == true ? result.Total : -1,
                searchFailed: model.ErrorMessage != null || model.SearchServiceDisabled);
        }

        return View(model);
    }

    private void EmitTelemetry(string phrase, string? locale, int durationMs, int resultCount, bool searchFailed)
    {
        try
        {
            var entry = new SearchLogEntry
            {
                At = DateTime.UtcNow,
                Phrase = phrase,
                Locale = (locale ?? string.Empty).ToLowerInvariant(),
                Site = SiteDefinition.Current?.Name ?? string.Empty,
                ProfileKey = ProfileKey,
                ResultCount = searchFailed ? -1 : resultCount,
                // Click attribution lands separately from a beacon endpoint
                // the SERP wires up — leaving these unset for now.
                TopResultRank = null,
                TopResultId = string.Empty,
                DurationMs = durationMs,
                // Mirrors the orderBy in AlloySearchService.BuildHitsQuery —
                // phrase queries always run SEMANTIC; the empty-phrase branch
                // returns recent-by-publish, which we don't telemeter.
                Ranking = "Semantic",
                ClientId = string.Empty,
                UserAgentClass = ClassifyUserAgent(Request?.Headers["User-Agent"].ToString()),
                Source = "host-sdk"
            };
            _searchLog.Append(entry);
        }
        catch (Exception ex)
        {
            // Telemetry must never fail loudly. Mirror the controller-side
            // policy in TelemetryApiController: log + swallow.
            _logger.LogWarning(ex, "Search telemetry append failed for phrase '{Phrase}'.", phrase);
        }
    }

    /// <summary>
    /// Coarse classification of the visitor's User-Agent into the bucket
    /// names that <see cref="SearchLogEntry.UserAgentClass"/> expects
    /// (<c>"bot"</c> / <c>"mobile"</c> / <c>"desktop"</c>). Heuristic but
    /// stable enough for the Search Logs cards. Empty when no UA header.
    /// </summary>
    private static string ClassifyUserAgent(string? ua)
    {
        if (string.IsNullOrEmpty(ua)) return string.Empty;
        // Bot detection wins — many bots set Mobile-style UAs to scrape mobile
        // pages. Order matters here.
        if (ua.Contains("bot", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("crawler", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("spider", StringComparison.OrdinalIgnoreCase))
        {
            return "bot";
        }
        if (ua.Contains("Mobile", StringComparison.Ordinal)
            || ua.Contains("Android", StringComparison.Ordinal)
            || ua.Contains("iPhone", StringComparison.Ordinal))
        {
            return "mobile";
        }
        return "desktop";
    }
}
