# NTI Graph Search Addon — Source Survey

Research of the existing **Graph Search** admin tool inside `nti-website/src` that will
be the seed for `Umage.Optimizely.GraphSearchtools`. Captured 2026-04-30 from the
working tree on disk (no git history consulted).

The goal of this document is not to evaluate the code, but to map out exactly what
is there so the port to a generic NuGet add-on can be planned with confidence.

---

## 1. Where the code lives

The addon is a single Razor Class Library tool under `NTI.WebExtensions`, served as a
CMS Shell menu item. It plugs into the host site's existing Optimizely Content Graph
configuration and adds a custom admin page plus a private REST API.

| Path | Purpose |
|---|---|
| `NTI.WebExtensions/GraphSearch/GraphSearchController.cs` | MVC controller — serves the Razor view, registers the Shell menu item. |
| `NTI.WebExtensions/GraphSearch/GraphSearchApiController.cs` | `[ApiController]` REST surface under `/api/graph-search`. |
| `NTI.WebExtensions/GraphSearch/GraphSearchManagementService.cs` | HttpClient-based proxy that talks to Optimizely Graph's REST and GraphQL endpoints. |
| `NTI.WebExtensions/GraphSearch/GraphSearchModels.cs` | Request/response DTOs (records). |
| `NTI.WebExtensions/GraphSearch/GraphSearchViewModel.cs` | View model — just the two API base URLs. |
| `NTI.WebExtensions.Views/Views/GraphSearch/Index.cshtml` | Single-file UI: ~540 lines CSS + HTML + ~1000 lines vanilla JS. |
| `NTI.WebExtensions/ServiceCollectionExtensions.cs` | DI registration: `services.AddHttpClient<IGraphSearchManagementService, GraphSearchManagementService>()`. |
| `NTI.WebExtensions.Tests/GraphSearch/GraphSearchManagementServiceTests.cs` | Unit tests covering Basic auth header + plain-text synonyms PUT body. |

Health-check data displayed by the UI comes from a separate component in
`NTI.Backend` (see §6) — `IGraphHealthStore` and a hosted background service —
which is **not** part of the WebExtensions addon and is more deeply coupled to the
NTI domain.

---

## 2. What the tool does — three tabs

The view (`Index.cshtml`) is one screen with three tabs:

### 2.1 Pinned items
"Pinned results" let editors force a specific page/product to the top when a given
search phrase is typed. The UI is a flat editable grid:

| Site | Phrase | Content | actions |
|---|---|---|---|

- **Site** — derived from the host site's `StartPage.LanguageSitesOrDefault()`, one
  collection per language. Collections are created lazily on first save
  (`ensureCollection`).
- **Phrase** — comma-separated list, free text.
- **Content** — inline content picker: typing >= 2 chars debounces a GraphQL search
  (see §4) against `BaseViewPage` and `Product` content types, dropdown lets the
  editor pick a hit. Selected content is shown as a coloured pill (green = product,
  blue = page).
- Sortable columns (site / phrase / content), free-text filter, site filter.
- Inline save (✔ button per row); rows are dirty-tracked (yellow highlight).

### 2.2 Synonyms
A plain-text editor of synonym rules, scoped per-language plus a "Global" bucket.
Saved as one blob per language slot via `PUT /resources/synonyms?language_routing=...&synonym_slot=one`.

- Hardcoded `slot = "one"` (Optimizely Graph supports up to two synonym slots).
- Two rule types, documented in the footer:
  - **Replacement**: `H2O => water`
  - **Equivalent**: `laptop, computer, pc`
- "Save changes" button persists the entire blob; emptying it issues a `DELETE`.
- Tracks a single `synDirty` flag; warns on tab change and `beforeunload`.

### 2.3 Health
Read-only dashboard showing the rolling status of an out-of-band background job
(see §6) that re-checks 10 products per minute against the Graph index:

- 3 KPI cards: status (green/amber/red dot), avg response time (ms, 24h), catalog
  coverage % (`productsChecked / productsInCatalog` of language pairs).
- 24h response-time line chart drawn on a `<canvas>` with vertical markers for
  failures (red) and product mismatch/missing (amber).
- Paged log table with badge per row (ok / missing / mismatched / reindexed / error).
  Each row has a "Copy" button that copies the GraphQL query used for that check
  to the clipboard — handy for debugging.
- Auto-refreshes every 60s while the tab is active.

---

## 3. REST API surface (under `/api/graph-search`)

All endpoints are gated by `[Authorize(Roles = "Administrators,WebAdmins")]`. Errors
surface as `Problem(...)` or, for upstream Graph errors, the original status code
plus body via `GraphSearchApiException`.

### Pinned collections
| Method | Route | Purpose |
|---|---|---|
| GET | `/collections` | List pinned collections. |
| POST | `/collections` | Create. Body: `PinnedCollectionPayload { Title, Key, IsActive }`. |
| PUT | `/collections/{id}` | Update (partial — `PinnedCollectionUpdatePayload`). |
| DELETE | `/collections/{id}` | Delete. |

### Pinned items
| Method | Route | Purpose |
|---|---|---|
| GET | `/collections/{id}/items` | List items. |
| POST | `/collections/{id}/items` | Create. Body: `PinnedItemPayload { Phrases, TargetKey, Language?, Priority?, IsActive? }`. |
| PUT | `/collections/{id}/items/{itemId}` | Update. |
| DELETE | `/collections/{id}/items/{itemId}` | Delete. |

### Synonyms
| Method | Route | Purpose |
|---|---|---|
| GET | `/synonyms?language_routing=&source_routing=&synonym_slot=` | Returns text blob. |
| PUT | `/synonyms` | Body `{ Content, LanguageRouting?, SourceRouting?, Slot? }`. Translates to plain-text PUT upstream. |
| DELETE | `/synonyms?...` | Removes the slot. |

### Sites & content lookup
| Method | Route | Purpose |
|---|---|---|
| GET | `/sites` | Reads `StartPage.LanguageSitesOrDefault()`, returns `[{Title, LanguageCode, CollectionKey}]`. |
| GET | `/content/search?q=&locale=` | GraphQL fulltext search across `BaseViewPage` and `Product`, returns 20 hits. |
| POST | `/content/resolve` | Body `string[]` of GUIDs; resolves names via GraphQL. Used to label pinned items after load. |
| GET | `/content/{contentId:int}` | Translates a numeric content id to `{ContentId, ContentGuid, Name}` — convenience for deep-linking. |

### Health
| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | `HealthSnapshot` (status, avg ms, coverage, history). |
| GET | `/health/log?page=&pageSize=&issuesOnly=&search=` | Paged `ProductCheckLog`. |

Both health endpoints degrade gracefully when `IGraphHealthStore` is not registered
(returns empty snapshot), which makes them safe to keep as part of the generic
addon and expose only when health monitoring is wired up.

---

## 4. How the management service talks to Optimizely Graph

`GraphSearchManagementService` is a thin HttpClient wrapper. Two distinct upstream
surfaces are used.

### 4.1 REST (admin) — Basic auth with `AppKey:Secret`
Configuration block: `Optimizely:ContentGraph` → `GatewayAddress`, `AppKey`, `Secret`.

```
Authorization: Basic base64(AppKey:Secret)
```

Endpoints called:

- `GET/POST/PUT/DELETE {gateway}/api/pinned/collections[/{id}[/items[/{itemId}]]]`
- `GET/PUT/DELETE {gateway}/resources/synonyms?language_routing=&source_routing=&synonym_slot=`
  - PUT body is `text/plain` (the rule blob), **not** JSON. The unit test pins this
    contract — easy to break if the controller is rewritten.

JSON serialization uses `JsonNamingPolicy.CamelCase` and ignores nulls.

### 4.2 GraphQL (query) — `SingleKey` in querystring
Configuration adds `SingleKey`. The query endpoint is:

```
{gateway}/content/v2?auth={SingleKey}
```

Two queries are issued, both inline strings inside the C# service:

**`SearchContent`** — content picker for the "Content" cell. Searches up to 20
items across `BaseViewPage` and `Product`, ordered by `_ranking: RELEVANCE`,
matched against `_fulltext` OR `Name like %q%`, optionally filtered by locale.

**`ResolveByGuids`** — bulk-resolve up to 100 GUIDs on page load to label pinned
items. Deduplicates on GUID (Graph returns one row per language).

Both queries return `Name`, `ContentType`, `ContentLink.GuidValue`, `Language.Name`.

> **Note for porting:** `BaseViewPage` and `Product` are NTI-specific content type
> names hard-coded into the GraphQL string. A generic addon must let consumers
> configure which content types are searchable, or fall back to `_Content` /
> `_Page`.

---

## 5. UI implementation notes

The view is **purely vanilla JS in a single Razor file** — no build step, no React,
no module bundler. Two view-model values are emitted server-side:

```cshtml
const apiBase = '@Model.ApiBaseUrl';                     // /api/graph-search
const contentSearchUrl = '@Model.ContentSearchUrl';      // /api/graph-search/content/search
```

Style is plain CSS scoped under `.graph-*` classes, ~540 lines. No external assets.

State model:

```js
let sites = [];        // { title, languageCode, collectionKey }
let collections = [];  // { id, key, title, isActive }
let allRows = [];      // pinned items, with per-row `dirty` and `isNew` flags
let synRows = [];      // synonym rules
let healthData = ...
```

Dirty tracking is per-row; saves are individual rather than batched. The synonyms
tab uses an all-or-nothing save (single text blob).

Layout uses `Layout = "../Shared/_Layout.cshtml"`, which lives in the same
`NTI.WebExtensions.Views` Razor library — porting requires picking a layout for the
new addon (likely the EditorPowertools shell layout pattern).

---

## 6. Backend dependencies the addon assumes

These are **not** part of the addon directory but are referenced by the API
controller and service. Each one needs a porting decision.

| Dependency | Used by | Origin | Generic? |
|---|---|---|---|
| `IContentLoader` (Optimizely) | API controller — `GET /sites`, `GET /content/{id}`. | EPiServer.CMS | ✅ keep |
| `StartPage` + `LanguageSitesOrDefault()` | `GET /sites`. | NTI domain model + extension | ❌ replace with `ISiteDefinitionRepository` / language branch enumeration |
| `[Authorize(Roles = "Administrators,WebAdmins")]` | Both controllers. | Episerver default | ⚠️ make role configurable, or use `[Authorize(Policy = …)]` like EditorPowertools |
| `IConfiguration` → `Optimizely:ContentGraph:{GatewayAddress,AppKey,Secret,SingleKey}` | Service. | App settings | ✅ keep — same convention Optimizely Graph SDK uses |
| `MenuItem(MenuPaths.Global + "/cms/Graph Search", ...)` | Controller. | EPiServer.Shell | ✅ keep, prefix path under add-on namespace |
| `IGraphHealthStore`, `HealthSnapshot`, `ProductCheckLog` | Health endpoints. | `NTI.Backend.Interfaces.GraphHealth` + a hosted service that pings the Graph index | ❌ NTI-specific. Either: (a) drop the Health tab in v1 of the addon, (b) ship an optional `Umage.Optimizely.GraphSearchtools.Health` package with a pluggable `IGraphHealthStore` interface, or (c) reduce to a much simpler "ping the gateway" probe. |
| `BaseViewPage`, `Product` content type names in GraphQL | Service `SearchContentAsync` / `ResolveByGuidsAsync`. | NTI domain | ❌ make configurable via options (`GraphSearchtoolsOptions.SearchableContentTypes`) |

The host site's wider Graph wiring (in `NTI.Web/Startup/DependencyRegistration.Graph.cs`)
is **not** needed by the addon itself — `AddContentGraph()`, `AddCommerceGraph()`,
`IGraphQLClient` registration, schema sync, etc. are properties of the host project
that the addon piggy-backs on.

The `IndexingExtensions` / `GraphConventions` files under `NTI.Backend/Features/GraphSearch/`
also live entirely in the host: they configure custom indexed fields for the NTI
domain (markets, course dates, product images, etc.). They are out of scope for a
generic admin add-on.

---

## 7. Summary — what's reusable vs. what isn't

### Reusable (port mostly as-is)
- The whole **pinned-items** feature: API + service + UI grid + GraphQL content
  picker logic. The Optimizely Graph REST surface for pinned collections is generic.
- The whole **synonyms** feature: API + service + UI. The plain-text PUT contract
  is the actual upstream API and won't change.
- The HTTP plumbing in `GraphSearchManagementService`: Basic-auth header from
  `AppKey:Secret`, GraphQL POST with `SingleKey`, `GraphSearchApiException` shape.
- The single-file CSS + vanilla JS pattern (matches EditorPowertools' "no bundler"
  preference).

### Needs generalization
- `StartPage` / `LanguageSitesOrDefault()` → use `ISiteDefinitionRepository` or
  enumerate `EPiServer.Globalization.LanguageBranchRepository`.
- Hardcoded `BaseViewPage` / `Product` GraphQL content types → expose through an
  options class, with a sensible default (e.g. `IContent` derivatives).
- Hardcoded role-based authorization → policy-based, configurable, matching the
  pattern already established in EditorPowertools (`[Authorize(Policy = "codeart:editorpowertools")]`).
- Layout reference (`../Shared/_Layout.cshtml`) → use the same shell layout
  EditorPowertools uses for its admin pages.

### Not reusable / out of scope
- Health-check pipeline (background service writing into `IGraphHealthStore`,
  product reindexer wiring, NTI-specific catalog assumptions). Ship the Health tab
  as a separately-installable optional component, or drop it from v1.
- `IndexingExtensions` and `GraphConventions` field customizations — these are
  host-app concerns, not add-on concerns.

---

## 8. Suggested first port (v0.1 of `Umage.Optimizely.GraphSearchtools`)

1. Bring across the four GraphSearch C# files plus tests verbatim, renaming the
   namespace.
2. Replace `LanguageSitesOrDefault()` with a small helper that enumerates language
   branches from the standard Optimizely API, and let consumers override.
3. Replace the hardcoded `BaseViewPage`/`Product` content type list with an options
   object; default to `_Page`/`_Content` so out-of-the-box install works on a
   vanilla CMS site.
4. Swap role-based authorization for the EditorPowertools policy pattern; bind
   options from `Umage:GraphSearchtools` (or similar).
5. Drop the Health tab for v0.1; leave the `[HttpGet("health")]` endpoints + null
   `IGraphHealthStore` fallback in place (already designed to no-op).
6. Move the Razor view into the addon's RCL, with the addon-shared layout.
7. Mirror the EditorPowertools NuGet/CI pipeline (tag-driven `publish.yml`).

This delivers pinned items + synonyms management on day 1, with the health
pipeline tracked as a follow-up that can either be extracted from NTI or rebuilt
on a smaller "ping the gateway" basis.
