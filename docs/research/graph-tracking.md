# Optimizely Graph — Search Click-Through Tracking

How Optimizely Graph records "which result did the user click for this query?", what gets
sent over the wire, how the surface is reached from .NET, and what the platform exposes
back to operators today.

Research conducted 2026-05-22 against `docs.developers.optimizely.com`, the 2025/2026
Graph release notes, and community write-ups linked at the bottom.

> **TL;DR**
> Tracking is a thin **click-attribution** pipeline, not a full analytics SDK. The
> server stamps each result with a per-impression `TrackUrl`; the page renders results
> as `<graph-trackable-link>`s; on click the browser's Beacon API POSTs to
> `/Optimizely/Track/TrackClickThrough` on **your** host, which forwards to the Graph
> Gateway. Aggregates (CTR, popular/problematic queries) are exposed through the
> in-development **Graph Search Management Portal** and Datadog tenant metrics — there
> is currently no GraphQL/REST surface for replaying raw events.
>
> **Caching:** tokens are per-impression so a `.Track()`-decorated response cannot
> be byte-cached. To keep hot search pages cacheable, split into a heavy content
> query (no `.Track()`, output-cached) and a thin tracked query (ids + `TrackUrls`
> only, runs per impression). Synthetic/preview surfaces must skip `.Track()`
> entirely to avoid polluting the portal. Details in §7.

---

## 1. The shape of the pipeline

```
┌──────────────┐  1. .SearchFor("api").Track()   ┌──────────────────┐
│  ASP.NET app │ ───────────────────────────────▶│  Graph Gateway   │
│   (server)   │◀── results + per-item TrackUrl ─│  (cloud SaaS)    │
└──────┬───────┘                                 └─────────┬────────┘
       │ render <graph-trackable-link>                     ▲
       ▼                                                   │ aggregate
┌──────────────┐  2. user clicks result                    │ + Datadog
│   browser    │ ──── Beacon POST ────▶ /Optimizely/Track/TrackClickThrough
└──────────────┘                              (same-origin proxy → Gateway)
```

Three pieces fit together:

1. **A search query opted into tracking** — `.Track()` on the SDK query (or the
   equivalent flag on the underlying GraphQL request) tells the Gateway to stamp
   each returned hit with a one-shot `TrackUrl`.
2. **A tag helper that decorates the rendered result link** — `<graph-trackable-link>`
   (or any `<a data-track-url="…">`) holds the `TrackUrl` so the page-side script can
   fire when the link is activated.
3. **A same-origin endpoint that forwards the beacon** — `app.UseGraphTrackingScripts()`
   wires up `/Optimizely/Track/TrackClickThrough` and the client JS at
   `/optimizely/graph/scripts/`.

The browser → host hop is mandatory so the anti-forgery token can be validated and so
the Gateway never needs to be exposed CORS-wide; the host then forwards the event upstream
using its own AppKey/Secret.

## 2. Server side: SDK call

The CMS 13 Graph C# SDK adds `.Track()` to the search query builder. It's a no-op unless
combined with `.SearchFor(…)` (or the lower-level `.UsingFullText()` / `.UsingField()`):

```csharp
var results = await _graphClient
    .QueryContent<ArticlePage>()
    .SearchFor(q)
    .UsingFullText()
    .Track()                  // ← opts the query into click tracking
    .IncludeTotal()
    .Limit(20)
    .GetAsContentAsync();

// per-item lookup:
foreach (var item in results.Hits)
{
    results.TrackUrls?.TryGetValue(item, out var trackUrl);
    // pass trackUrl into the view alongside item
}
```

Key facts:

- `IContentResult<T>.TrackUrls` is a dictionary keyed by the result item, holding an
  opaque per-impression URL. **Do not parse it** — treat as a black-box token.
- Each `TrackUrl` is scoped to a single search; replaying the same `(query, item)` pair
  in a second search produces a different `TrackUrl`.
- A query without `.Track()` returns `TrackUrls = null`. Add `.Track()` lazily — if no
  call site renders trackable links, drop it.
- The SDK migration table from Search & Navigation → Graph maps `.Track()` 1:1, so
  legacy Find-style code keeps working at the surface.

Under the hood (GraphQL view, useful when not going through the SDK): the Track
behaviour is selected by a Graph-specific directive on the search field; the response
includes a sibling collection of tracking tokens that the SDK exposes as `TrackUrls`.

## 3. ASP.NET Core view layer

Tracking ships as a separate NuGet:

```
dotnet add package Optimizely.Graph.Cms.Query        # core query SDK
dotnet add package Optimizely.Graph.AspNetCore       # tag helpers + middleware
```

### 3.1 Wire the middleware

```csharp
// Program.cs
builder.Services.AddContentGraph();      // SDK
builder.Services.AddGraphContentClient();
builder.Services.AddAntiforgery();       // required — beacon payloads are AF-protected

var app = builder.Build();
app.UseGraphTrackingScripts();           // mounts /optimizely/graph/scripts/* and
                                         // /Optimizely/Track/TrackClickThrough
```

### 3.2 Register the tag helper namespace

```csharp
@* _ViewImports.cshtml *@
@addTagHelper *, Optimizely.Graph.AspNetCore
```

### 3.3 Render the head setup and trackable links

```cshtml
<!-- _Layout.cshtml -->
<head>
    <graph-tracking-setup />     <!-- emits beacon script + anti-forgery meta -->
</head>
```

```cshtml
<!-- search-results.cshtml -->
@foreach (var item in Model.Hits)
{
    var trackUrl = Model.TrackUrls?[item];
    <graph-trackable-link url="@item.Metadata?.Url"
                          track-url="@trackUrl"
                          class="result-link">
        @item.Metadata?.DisplayName
    </graph-trackable-link>
}
```

Equivalent plain HTML works too — any `<a data-track-url="…">` is picked up by the
script:

```html
<a href="@item.Metadata?.Url" data-track-url="@trackUrl">@item.Metadata?.DisplayName</a>
```

### 3.4 What the script does on click

- Listens for `click` (and keyboard activation) on `[data-track-url]`.
- Synchronously calls `navigator.sendBeacon('/Optimizely/Track/TrackClickThrough', body)`
  with the `track-url` token and an AF-token header read from the meta emitted by
  `<graph-tracking-setup/>`.
- Falls through to the normal navigation — the beacon is fire-and-forget and does not
  block. If `sendBeacon` is unavailable, it falls back to a `fetch(..., {keepalive: true})`.

## 4. The host endpoint

`/Optimizely/Track/TrackClickThrough` is the only externally observable URL. Properties
worth knowing for diagnostics:

| Aspect | Behaviour |
| --- | --- |
| **Method** | `POST` (Beacon API restricts the available verbs) |
| **Anti-forgery** | Required. 400 on missing/expired token. Token is rendered by `<graph-tracking-setup/>` — if the layout omits it, every beacon 400s silently. |
| **Auth** | None at the user level. The host adds the Graph AppKey/Secret server-side when forwarding. |
| **Idempotency** | Single click = single beacon. The `track-url` token is single-use; firing twice double-counts (no client dedupe). |
| **Failure mode** | Best-effort. A failed beacon does *not* block navigation; there is no client retry. |

> Common gotcha: the script source path is `/optimizely/graph/scripts/…`. If a
> middleware that strips the `optimizely` prefix sits in front of the app
> (e.g. a reverse proxy rewrite), the script 404s, `TrackUrls` are emitted by the
> server but never POSTed, and the dashboard sits at 0% CTR. Spot it by checking
> network requests for `/optimizely/graph/scripts/track.js` on page load.

## 5. What's actually recorded

The `TrackUrl` token already encodes everything the Gateway needs — the page just
echoes it back. Decoded server-side, the recorded fact is:

- **Query** (string, locale, any filters that were part of the search context)
- **Hit identity** (which content ID/index entry was clicked)
- **Hit position** (1-based rank within the result list at impression time)
- **Tenant** (from the AppKey)
- **Timestamp** (server-stamped on receive)

What is **not** recorded:

- The user (no PII, no IP retained for attribution purposes)
- Dwell time or post-click events
- Impressions that did *not* generate a click — only clicks travel back

This is the key conceptual gap from Find/Search & Navigation, which used a richer
`TrackContext`/`TrackId` pairing and let you stitch impressions to clicks. In Graph
the impression is implicit (the server already saw the query when minting the
`TrackUrl`); only the click event is reported.

## 6. Aggregates and read-back

There is currently **no public API to replay raw events** out of Graph. Aggregates
surface in two places:

### 6.1 Graph Search Management Portal (Beta, 2026-05-21)

The 2026 release notes describe a hosted dashboard with an **Overview** tab showing:

- Total query volume
- Click-through rate (CTR)
- Problematic queries — queries with hits but zero/low CTR, and queries with no
  hits at all (the latter were always available from the index, the former are
  what tracking unlocks)

The portal is in beta and is hosted by Optimizely — it is not part of the CMS
admin shell and there is no documented embed/SDK for surfacing the same numbers
inside your own UI.

### 6.2 Datadog (Gateway 3.23.0, 2025-12-09)

Per the 2025 release notes: "Added new click-through tracking endpoint for search,
including **Datadog integration and tenant-level metrics**." Translation: Optimizely
itself watches these events through Datadog for operational telemetry. Customers do
not get a Datadog feed; this is internal observability, useful only if you're filing
support tickets ("the dashboard shows 0% but I have proof of clicks — please check
ingestion").

### 6.3 Implications for an addon

If a tool wants its own CTR/popular-queries surface today, the options are:

1. **Mirror locally** — emit a parallel custom event to your own store (DDS,
   PostgreSQL, Application Insights) from a client wrapper around
   `<graph-trackable-link>`. Loses the upstream relevance-feedback loop but gives
   full control over the read-back surface.
2. **Wait** — the portal API may eventually be exposed; the 2026 notes hint at it
   but ship no public surface.
3. **Treat Graph CTR as a black box** — render impressions and clicks via Graph
   tracking, embed/link out to the portal for the editor-facing read-back.

For GraphSearchtools the natural play is (1) for the in-app Insights surface and an
optional link out to the portal once it lands a public URL — the cross-channel
slicing the addon already does (per channel, per pinned-collection, per synonym
group) is not something the upstream portal will know about.

## 7. Caching: tokens are per-impression, content is not

`.Track()` does *not* disable Graph's query-template byte cache (the same one
[[feedback_graph_query_byte_cache_staleness]] documents for pin/synonym edits) —
template parsing is still reused. What it does mean is that the **response payload
cannot be byte-cached** as a single artifact, because each `TrackUrl` is minted
per-impression. The token format isn't public, but it has to vary per execution —
otherwise CTR for a hot cached query would collapse to "one impression with N
clicks" and the analytics fall apart.

So "search results can never be cached" is only true if you insist on one query
carrying both content and tracking. Decouple them and content caching is back on
the table.

### 7.1 Three composition patterns

```
┌─ pattern A: tracked, uncached ───────────────────────────────┐
│  one query, .Track() on, no output cache                     │
│  every search page render = full Gateway round-trip          │
│  fine for low QPS / editor surfaces                          │
└──────────────────────────────────────────────────────────────┘

┌─ pattern B: untracked, cached ───────────────────────────────┐
│  one query, no .Track(), CDN/output cache freely             │
│  no click data flows; portal CTR stays at 0%                 │
│  fine if you don't care about CTR for that surface           │
└──────────────────────────────────────────────────────────────┘

┌─ pattern C: split — content cached, tokens minted live ──────┐
│  query 1: heavy content fetch, no .Track(), output-cached    │
│  query 2: thin .Track() query, fetches only ids + TrackUrls  │
│  merge server-side before rendering                          │
│  hot search pages stay cached; tracking still works          │
└──────────────────────────────────────────────────────────────┘
```

### 7.2 The thin tracked query

The split-query path only pays off if query 2 is genuinely cheap. The pattern is:

```csharp
// Query 1 — heavy, cacheable
var content = await _outputCache.GetOrAddAsync(cacheKey, async () =>
    await _graphClient.QueryContent<ArticlePage>()
        .SearchFor(q).UsingFullText()
        // full projection: title, teaser, url, score, facets...
        .GetAsContentAsync());

// Query 2 — thin, per-impression
var tracked = await _graphClient.QueryContent<ArticlePage>()
    .SearchFor(q).UsingFullText()
    .Track()
    // .Select(...) to project only _metadata.id — keep payload minimal
    .Limit(content.Hits.Count)
    .GetAsContentAsync();

// merge by id
var trackUrlByContentId = tracked.Hits
    .ToDictionary(h => h.Metadata.Id,
                  h => tracked.TrackUrls[h]);
```

The thin query still hits the Gateway every impression, but it's a tiny payload and
the template cache shields the parsing cost. The expensive part — joins, facets,
field projections — stays cached.

> Caveat: the two queries must use **identical** search criteria (operator,
> boosts, filters, locale). If filter chips diverge between the cached and tracked
> queries, ranks drift and the `TrackUrls` get stitched to the wrong rank in the
> portal. Build the criteria once and pass it to both.

### 7.3 Edge case: synthetic and preview surfaces must skip `.Track()`

Anything that replays a query for non-user purposes — admin "what would users
see?" previews, scheduled health checks, integration tests, the addon's own
side-by-side query playground — **must not** call `.Track()`. Synthetic
impressions inflate denominators in the portal and produce the "0% CTR despite
real clicks" failure mode in §10. The rule of thumb: `.Track()` belongs only on
the request that produces the result list a real user will actually see.

### 7.4 Relationship to the existing nonce-busting pattern

Caching-vs-tracking and template-cache-vs-edit-staleness are independent axes:

| Concern | Cause | Fix |
| --- | --- | --- |
| Edit staleness after pin/synonym change | Gateway template cache returns pre-edit bytes | Append per-call nonce comment to bust |
| Cacheability of tracked responses | `TrackUrl` must be fresh per impression | Split into content + thin tracked query (pattern C) |

A tracked query still needs the nonce after a pin/synonym edit; a cached content
query still needs the nonce after edits even though it carries no tokens. The two
mechanisms compose — apply both where both apply.

## 8. Operational notes

- **Sampling**: no documented sampling on the Gateway side. High-traffic sites
  send one beacon per click; the AF round-trip is the practical floor.
- **Bot traffic**: the dashboard does not filter; `<graph-trackable-link>` fires
  for any agent that runs JS and resolves the `data-track-url`. Headless crawlers
  that follow links without executing JS do not pollute the data; scripted
  crawlers that do execute JS will. Filter your own bot patterns at the host
  endpoint if this matters.
- **Anti-forgery rotation**: if your AF token expires (e.g. very long-lived SPA
  sessions), beacons start 400ing and CTR craters silently. A periodic refresh of
  the meta tag (or re-rendering the partial) is the workaround.
- **CSP**: `connect-src` needs to allow the same origin for the beacon POST. The
  Gateway is **not** contacted directly from the browser, so no third-party origin
  needs allowlisting.
- **CMS 12 / .NET 8**: the SDK and tag helpers target CMS 13. On CMS 12 you can
  call the underlying GraphQL surface yourself and POST the resulting token to a
  hand-rolled endpoint — but there is no first-party support for the
  `<graph-trackable-link>` UX on the CMS 12 stack.

## 9. Relation to Search & Navigation tracking

| Aspect | Search & Navigation (Find) | Graph |
| --- | --- | --- |
| Impression record | Yes — `StatisticsClient.TrackQuery()` returns a `TrackId` | Implicit (server already saw the query) |
| Click record | `TrackHit()` or `/go` redirect | `TrackClickThrough` beacon |
| Per-search session ID | Client-bound, sometimes per-browser-session | Server-issued per-impression token in `TrackUrl` |
| Raw event read-back | Search statistics API + Find admin UI | None (portal aggregates only) |
| Per-result rank capture | Manual (`trackhitpos` in query string) | Automatic |
| API surface in SDK | `.Track()` | `.Track()` (intentional naming carry-over) |

The migration story is "your view code mostly stays the same, your analytics
back-end gets replaced by the hosted portal." Bespoke Find dashboards built off the
statistics API need to be re-implemented; if they relied on raw event access they
will need the local-mirror strategy from §6.3.

## 10. Failure cases to know about

| Symptom | Likely cause |
| --- | --- |
| `TrackUrls` is null on the result | `.Track()` missing, or call placed before `.SearchFor(…)` so the builder returned a non-search query type |
| Network tab shows beacon POST 400 | AF token absent — confirm `<graph-tracking-setup/>` rendered, AF service registered |
| Network tab shows no beacon at all | Tracking script not loaded — confirm `app.UseGraphTrackingScripts()` ran before terminal middleware, and that `<a>` carries `data-track-url` (not just `track-url`) |
| Portal shows 0% CTR despite clicks | Token round-trip is working only one direction (impressions stamped, clicks dropped). Replay the click with browser devtools, confirm `/Optimizely/Track/TrackClickThrough` returns 204 |
| Portal shows impressions but no queries | `.Track()` was added to a list query that is not a search (no `.SearchFor`). The Gateway stamps tokens defensively but the resulting events have no query string and roll up as `(no query)` |

---

## Sources

- [Optimizely Graph — overview](https://docs.developers.optimizely.com/platform-optimizely/docs/introduction-optimizely-graph)
- [Graph C# SDK overview](https://docs.developers.optimizely.com/content-management-system/v13.0.0-CMS/docs/overview-of-c-sharp-sdk)
- [Search content in Optimizely Graph (CMS 13)](https://docs.developers.optimizely.com/content-management-system/v13-Pre-Release/docs/searching)
- [Migrate Alloy from Search & Navigation to Graph](https://docs.developers.optimizely.com/content-management-system/v13.0.0-CMS/docs/migrate-alloy-12-to-13)
- [Introducing the Optimizely CMS 13 Graph SDK — Jake Minard](https://world.optimizely.com/blogs/jake-minard/dates/2026/3/introducing-optimizely-cms-13-graph-sdk/)
- [2025 Optimizely Graph release notes](https://support.optimizely.com/hc/en-us/articles/42414448425101-2025-Optimizely-Graph-release-notes)
- [2026 Optimizely Graph release notes](https://support.optimizely.com/hc/en-us/articles/25432213670413-2026-Optimizely-Graph-release-notes)
- [Search & Navigation Click Tracking (legacy reference) — Dan Copping](https://world.optimizely.com/blogs/dan-copping---medium/dates/2025/7/search--navigation---click-tracking/)
- [Track search activity (Search & Navigation, for comparison)](https://docs.developers.optimizely.com/content-management-system/v1.1.0-search-and-navigation/docs/search-statistics)
