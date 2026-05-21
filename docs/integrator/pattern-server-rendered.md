# Pattern: server-rendered ASP.NET

The add-on's primary intended environment. The host project handles the
search request, queries Optimizely Graph from the server, and renders the
result list in Razor. Telemetry is emitted in-process — no HTTP roundtrip.

## Where the add-on fits

```
┌─────────────────────────────┐
│  CMS host (ASP.NET + Razor) │
│                             │
│  SearchController ─────┐    │
│                        │    │     ┌─────────────────────┐
│                        ├───▶│ ──▶ │  Optimizely Graph   │
│  Razor view  ◀─────────┘    │     └─────────────────────┘
│                             │
│  ITelemetrySink (in-proc)   │     ┌─────────────────────┐
│  ──────────────────────▶    │ ──▶ │  DDS aggregates     │
│                             │     │  (Insights UI)      │
└─────────────────────────────┘     └─────────────────────┘
        ▲
        │ CMS shell admin UI on the same host
        │ Edit → Add-ons → Graph Search Tools
        │ (Search channels / Pinned / Synonyms / Insights)
```

The CMS shell admin UI and the public search page share a process. Both
the addon's tools and your search controller resolve services from the
same DI container.

## Emit search events from your controller

Inject [`ITelemetrySink`](../../src/GraphSearchtools/Abstractions/ITelemetrySink.cs)
where you handle the search request. The contract is non-blocking, total,
and never throws — it is safe to call inside a hot render path.

```csharp
public sealed class SearchController : Controller
{
    private readonly ISearchService _search;
    private readonly ITelemetrySink _telemetry;

    public SearchController(ISearchService search, ITelemetrySink telemetry)
    {
        _search = search;
        _telemetry = telemetry;
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Index(string q, string locale = "en")
    {
        var results = await _search.QueryAsync(q, locale);

        _telemetry.Record(new SearchEvent(
            phrase: q ?? string.Empty,
            channelKey: "site-search",
            locale: locale,
            resultCount: results.Count,
            timestampUtc: DateTime.UtcNow));

        return View(results);
    }
}
```

`channelKey` must match a key you registered with `AddSearchChannel(...)`.
Events with unknown channel keys are still recorded but won't tie back to
a channel in the Insights filters.

## Track click-throughs

For click attribution, render each result with a small handler that posts
back to the same controller (or directly to the ingest endpoint — see
[the headless pattern](pattern-headless.md)) before navigating:

```cshtml
@foreach (var (hit, index) in Model.Results.Select((h, i) => (h, i)))
{
    <a href="@hit.Url"
       data-gst-click
       data-gst-phrase="@Model.Query"
       data-gst-rank="@index">
        @hit.Title
    </a>
}
```

```javascript
// site.js
document.querySelectorAll('[data-gst-click]').forEach(a => {
    a.addEventListener('click', () => {
        navigator.sendBeacon('/api/telemetry/searchlog', JSON.stringify({
            kind: 'click',
            phrase: a.dataset.gstPhrase,
            channelKey: 'site-search',
            locale: document.documentElement.lang || 'en',
            rank: Number(a.dataset.gstRank),
        }));
    });
});
```

`sendBeacon` is the right tool here: it survives page navigation and never
blocks the click.

## Tying pinned results into the live query

The add-on only writes to Optimizely Graph's pinned-result collection
under the key your channel's `UsesPinnedKey(...)` resolves to. Your live
GraphQL query must opt in to pinned results using Graph's native query
modifiers — see Optimizely's
[Graph documentation](https://docs.developers.optimizely.com/) for the
exact syntax. The `UsesPinnedKey` value in the channel registration must
match the collection name your query references.

## Gotchas

- **Non-blocking sink.** `ITelemetrySink.Record(...)` returns immediately
  and drops events under overload (DropOldest semantics). Don't rely on
  it for accounting or billing — it's an analytics signal, not an audit
  log. The audit log is a separate concern (`AuditLogService`).
- **One channel per surface, not per query.** A channel models a *search
  surface* (header search, product listing, KB). Multiple Razor pages
  that hit the same surface share one channel key.
- **Locale is free-form.** The add-on does not validate the locale string
  beyond lowercasing it. Stay consistent — `en-us` and `en_US` will be
  treated as different locales in aggregates.
