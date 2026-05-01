# Umage.Optimizely.GraphSearchtools — Implementation Plan

A concrete, phased plan to build `Umage.Optimizely.GraphSearchtools` as a generic
Optimizely CMS add-on that pairs the **framework/UX of `Umage.Optimizely.EditorPowertools`**
with the **Graph admin functionality** captured in `docs/research/`.

This plan turns the design report in `docs/research/addon-design.md` into ordered work,
calls out exactly which files to copy from each source repo, and names the substitutions
that must happen during the port. All references to the originating partner project are
stripped — the goal is a vendor-neutral `UmageAI.Optimizely.GraphSearchTools` package.

> Source repos referenced in this plan
> - **Framework source:** `../Umage.Optimizely.EditorPowertools/`
> - **Functional seed (private codebase, copy with rename only):** `../nti-website/src/NTI.WebExtensions/GraphSearch/` and `NTI.WebExtensions.Views/Views/GraphSearch/`. After porting, no reference to that namespace, repo name, or domain types remains.

---

## 0. Outcomes

A consumer adds one line to `Startup`:

```csharp
services.AddGraphSearchtools(o => Configuration.GetSection("CodeArt:GraphSearchtools").Bind(o));
app.UseGraphSearchtools();
```

…and gets a CMS-shell-integrated admin area at `/EPiServer/cms/graphsearchtools/...`
with the same look-and-feel as Editor Powertools, exposing:

- **v0.1 (Phase 1):** Pinned items + Synonyms management. Subsumes the
  third-party `OptiGraphExtensions` for these two surfaces, on a CMS-shell UX.
- **v0.2 (Phase 2):** Search Console + Saved Queries + Autocomplete Tester +
  Connectivity Tester. The "playground" levers from
  `docs/research/relevancy-optimization.md`.
- **v0.3 (Phase 3):** Tuning power tools (Decay & Factor Sandbox,
  Semantic Weight Tuner, Schema Inspector, Webhooks, Custom Data Sources,
  Request Logs).
- **v0.4 (Phase 4):** Analytics & audits (Search Logs, Index Health, Content
  Searchability Audit, Pinned Result Coverage, Synonym Coverage).
- **v0.5 (Phase 5):** Relevancy Lab (golden-set NDCG/MRR scoring).

Each phase is independently shippable; later phases add tools, not framework.

---

## 1. Naming map — apply mechanically during the port

| Source token | Target token |
|---|---|
| Repo `Umage.Optimizely.EditorPowertools` | `Umage.Optimizely.GraphSearchtools` |
| NuGet `UmageAI.Optimizely.EditorPowerTools` | `UmageAI.Optimizely.GraphSearchTools` |
| Root namespace `UmageAI.Optimizely.EditorPowerTools` | `UmageAI.Optimizely.GraphSearchTools` |
| Project `EditorPowertools.csproj` | `GraphSearchtools.csproj` |
| Solution `EditorPowertools.slnx` | `GraphSearchtools.slnx` |
| Sample sites `EditorPowertools.SampleSite[Cms13]` | `GraphSearchtools.SampleSite[Cms13]` |
| Test project `EditorPowertools.Tests` | `GraphSearchtools.Tests` |
| Module folder `modules/_protected/EditorPowertools/` | `modules/_protected/GraphSearchtools/` |
| Menu base `MenuPaths.Global + "/cms/editorpowertools"` | `MenuPaths.Global + "/cms/graphsearchtools"` |
| Auth policy `codeart:editorpowertools` | `codeart:graphsearchtools` |
| Config section `CodeArt:EditorPowertools` | `CodeArt:GraphSearchtools` |
| `PermissionType` category `"EditorPowertools"` | `"GraphSearchtools"` |
| Loc string root `/editorpowertools/...` | `/graphsearchtools/...` |
| JS globals `window.EPT_*`, `EPT.*`, `EPT_STRINGS` | `window.GST_*`, `GST.*`, `GST_STRINGS` |
| CSS prefix `ept-`, vars `--ept-*` | `gst-`, `--gst-*` |
| Layout `_PowertoolsLayout.cshtml` | `_SearchtoolsLayout.cshtml` |
| Shell title "Editor Powertools" | "Graph Search Tools" |
| DI extensions `AddEditorPowertools` / `UseEditorPowertools` / `MapEditorPowertools` | `AddGraphSearchtools` / `UseGraphSearchtools` / `MapGraphSearchtools` |
| `ProtectedModuleOptions` `Name = "EditorPowertools"` | `Name = "GraphSearchtools"` |

Keep MIT license, `Authors = UmageAI`, and `PackageProjectUrl` pattern (with the new
repo URL). Watermark "Powered by umage.ai" stays.

---

## 2. Phase 0 — Framework fork (no tools yet)

**Goal:** Empty shell installs cleanly on CMS 12 and CMS 13, the menu opens, the
Overview page renders, and CI publishes a `v0.0.1-preview.1` package.

### 2.1 Repo skeleton

Create the structure described in `docs/research/addon-design.md` §3.1. Concretely:

```
src/
├── GraphSearchtools/
│   ├── Abstractions/
│   ├── Cms12/
│   ├── Cms13/
│   ├── Components/
│   ├── Configuration/
│   ├── Helpers/
│   ├── Infrastructure/
│   ├── Localization/
│   ├── Menu/
│   ├── Permissions/
│   ├── Services/
│   ├── Tools/
│   │   └── Overview/
│   ├── Views/
│   │   ├── Shared/_SearchtoolsLayout.cshtml
│   │   ├── Shared/Cms13/_SearchtoolsLayout.cshtml
│   │   └── Overview/Index.cshtml
│   ├── lang/{en,da,sv,no,de,fi,fr,es,nl,ja,zh-CN}.xml
│   ├── modules/_protected/GraphSearchtools/
│   │   ├── module.config
│   │   └── ClientResources/
│   │       ├── css/graphsearchtools.css
│   │       └── js/{graphsearchtools.js, components.js}
│   ├── build/net8.0/UmageAI.Optimizely.GraphSearchTools.targets
│   ├── build/net10.0/UmageAI.Optimizely.GraphSearchTools.targets
│   └── GraphSearchtools.csproj
├── GraphSearchtools.SampleSite/        ← Alloy CMS 12 demo
├── GraphSearchtools.SampleSiteCms13/   ← Alloy CMS 13 demo
└── GraphSearchtools.Tests/             ← multi-target
GraphSearchtools.slnx
```

### 2.2 Files to copy verbatim from EditorPowertools

Copy these files, then run the §1 search-and-replace across the new tree:

- `src/EditorPowertools/EditorPowertools.csproj` → `src/GraphSearchtools/GraphSearchtools.csproj`
  - Change `<PackageId>` to `UmageAI.Optimizely.GraphSearchTools`.
  - Change `<Description>` to "Graph search tooling for Optimizely CMS — pinned results, synonyms, query playground, and relevancy tuning."
  - Change `<PackageTags>` to `Optimizely;CMS;Graph;Search;Synonyms;PinnedResults;Relevancy;Autocomplete`.
  - Drop `CsvHelper` and `ClosedXML` `<PackageReference>`s — search-log export can use a plain CSV writer if/when needed.
  - Module-zip `<Target>` repoint to `modules\_protected\GraphSearchtools`.
  - Rename the two `.targets` outputs to `UmageAI.Optimizely.GraphSearchTools.targets`.
- `src/EditorPowertools/build/{net8.0,net10.0}/*.targets`
- `src/EditorPowertools/Configuration/EditorPowertoolsOptions.cs` →
  `Configuration/GraphSearchtoolsOptions.cs` (then extend per §2.5).
- `src/EditorPowertools/Configuration/FeatureToggles.cs` — strip every flag,
  add `Overview` only for now; subsequent phases add per-tool flags.
- `src/EditorPowertools/Infrastructure/ServiceCollectionExtensions.cs` →
  `Infrastructure/ServiceCollectionExtensions.cs` (`AddGraphSearchtools`).
  Keep the framework wiring; remove every tool-specific service registration.
- `src/EditorPowertools/Infrastructure/ApplicationBuilderExtensions.cs` →
  `UseGraphSearchtools`, `MapGraphSearchtools`. Remove the SignalR hub mapping
  — not needed.
- `src/EditorPowertools/Infrastructure/RequireAjaxAttribute.cs`
- `src/EditorPowertools/Localization/UiStringsController.cs` and
  `Localization/UiStringsProvider.cs` — change loc paths to `/graphsearchtools/ui/...`.
- `src/EditorPowertools/Menu/EditorPowertoolsMenuProvider.cs` →
  `Menu/GraphSearchtoolsMenuProvider.cs`. Trim down to the Overview entry only;
  add per-tool `MenuItem`s in later phases.
- `src/EditorPowertools/Permissions/{EditorPowertoolsPermissions,FeatureAccessChecker}.cs`
  → `Permissions/{GraphSearchtoolsPermissions,FeatureAccessChecker}.cs`.
- `src/EditorPowertools/Components/*` (content picker, content-type picker) — verbatim,
  rename CSS/JS prefix only.
- `src/EditorPowertools/Helpers/*` — keep what's framework-level (Paths, JSON, route helpers); discard tool-specific helpers.
- `src/EditorPowertools/Services/UserPreferencesService.cs` — verbatim.
- `src/EditorPowertools/Views/Shared/_PowertoolsLayout.cshtml` → `Views/Shared/_SearchtoolsLayout.cshtml`. Substitute:
  - Header text → "Graph Search Tools".
  - Header SVG → search/lens icon (replace inline SVG; an example is below).
  - About link → `GraphSearchtools/About`.
  - Bottom JS bootstrap (`window.EPT_*` → `window.GST_*`).
  - Bundle file names → `graphsearchtools.js` / `components.js`.
- `src/EditorPowertools/Views/Shared/Cms13/_PowertoolsLayout.cshtml` → analogous Cms13 variant.
- `src/EditorPowertools/Tools/Overview/*` — verbatim, but starts as an **empty grid** until tools are added.
- `src/EditorPowertools/modules/_protected/EditorPowertools/module.config` — port per `addon-design.md` §3.3 (drop `signalr.min.js`).
- `src/EditorPowertools/modules/_protected/EditorPowertools/ClientResources/{css,js}/*` —
  - `editorpowertools.css` → `graphsearchtools.css`, run `s/ept-/gst-/g` and `s/--ept-/--gst-/g`.
  - `editorpowertools.js` → `graphsearchtools.js`, rename `EPT` namespace to `GST`, `EPT_*` globals to `GST_*`. Keep `fetchJson`, `postJson`, `showLoading`, `showEmpty`, `openDialog`, `createTable`, `downloadCsv`, `icons`, `contentPicker`, `contentTypePicker` API surface intact.
  - `components.js` (shared content picker / content-type picker logic) — verbatim with same renames.
  - **Drop**: `signalr.min.js`, `active-editors.js`, any tool-specific JS that won't apply.
- `src/EditorPowertools/lang/*.xml` (11 files) — keep file structure; replace top-level
  `<editorpowertools>` element with `<graphsearchtools>`, replace `<EditorPowertools>` permission group element with `<GraphSearchtools>`, remove all per-tool entries, leave only:
  - `/graphsearchtools/menu/title` = "Graph Search Tools"
  - `/graphsearchtools/menu/overview` = "Overview"
  - `/graphsearchtools/menu/about` = "About"
  - `/graphsearchtools/about/...` (description, version, links)
  - `/graphsearchtools/permissions/...` for the policy display name.
  Each phase adds its own loc keys. English (`en.xml`) is the base; the other 10 languages are mechanical translations and can be filled with English placeholders initially, marked `TODO_TRANSLATE`.

### 2.3 Sample sites

- Copy `src/EditorPowertools.SampleSite/` → `src/GraphSearchtools.SampleSite/` (Alloy CMS 12).
- Copy `src/EditorPowertools.SampleSiteCms13/` → `src/GraphSearchtools.SampleSiteCms13/` (Alloy CMS 13).
- Replace the `<ProjectReference>` with the new csproj path.
- In each sample's `appsettings.json`, replace the `CodeArt:EditorPowertools` block with:
  ```json
  {
    "CodeArt": {
      "GraphSearchtools": {
        "authorizedRoles": ["WebAdmins", "Administrators"],
        "features": { "overview": true }
      }
    },
    "Optimizely": {
      "ContentGraph": {
        "GatewayAddress": "https://cg.optimizely.com",
        "AppKey": "",
        "Secret": "",
        "SingleKey": ""
      }
    }
  }
  ```
  Sample-site graph keys are filled per-environment via `dotnet user-secrets` — never committed.
- In `Startup.cs`, replace `services.AddEditorPowertools(...)` / `app.UseEditorPowertools()`
  with the new equivalents.

### 2.4 Tests

- Copy `src/EditorPowertools.Tests/` skeleton (csproj + test base class only) → `src/GraphSearchtools.Tests/`.
- Drop every existing test class — they belong to discontinued tools.
- Add a single smoke test: `AddGraphSearchtools_RegistersExpectedServices` (verifies
  options binding, menu provider, FeatureAccessChecker, layout virtual-path remap,
  ProtectedModuleOptions entry).

### 2.5 `GraphSearchtoolsOptions`

Per `docs/research/addon-design.md` §6:

```csharp
namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

public class GraphSearchtoolsOptions
{
    public FeatureToggles Features { get; set; } = new();
    public bool CheckPermissionForEachFeature { get; set; }
    public string[] AuthorizedRoles { get; set; } = ["WebAdmins", "Administrators"];

    /// <summary>
    /// Optional override. When unset, the addon reads from the host's
    /// Optimizely:ContentGraph section.
    /// </summary>
    public GraphConnectionOptions? Graph { get; set; }

    /// <summary>
    /// Content type fully qualified names that the search picker / search console
    /// should query. Defaults to `_Page` and `_Content` (vanilla CMS) so the addon
    /// works out of the box without configuration.
    /// </summary>
    public string[] SearchableContentTypes { get; set; } = ["_Page"];
}

public class GraphConnectionOptions
{
    public string? GatewayAddress { get; set; }   // e.g. https://cg.optimizely.com
    public string? AppKey { get; set; }
    public string? Secret { get; set; }
    public string? SingleKey { get; set; }
}
```

### 2.6 Distribution

- Copy `.github/workflows/publish.yml` from EditorPowertools verbatim, retarget paths.
- Tag `v0.0.1-preview.1` on first green build to validate the pipeline end-to-end:
  build for both TFMs, test, pack, attach `.nupkg` to a GitHub release. **Do not push
  to nuget.org** — Optimizely's feed picks it up automatically.
- Local pre-flight: `dotnet test` (no `--no-build`, per the existing CLAUDE.md note).

### 2.7 Phase 0 acceptance

- Install the package on a fresh Alloy CMS 12 site → "Graph Search Tools" appears in
  the global nav → clicking it opens the Overview page (empty grid) inside CMS chrome.
- Repeat on CMS 13.
- `dotnet test` is green on both TFMs.
- `Connectivity Tester` is the next thing built (Phase 1) so editors can see
  green/red on the connection before any tool tries to call Graph.

---

## 3. Phase 1 — Editorial parity (Pinned + Synonyms)

**Goal:** Editors can manage **pinned results** and **synonyms** through a polished
CMS-shell UX. This is the v0.1 release and the immediate replacement for the
third-party Blazor add-on for these two surfaces.

### 3.1 What to copy from the functional seed

Source files (rename-only port — strip the originating namespace from filenames,
class names, paths, and comments):

| Source path | Target path | Rename to |
|---|---|---|
| `NTI.WebExtensions/GraphSearch/GraphSearchController.cs` | `src/GraphSearchtools/Tools/Pinned/PinnedController.cs` (and split out the synonym route to `Tools/Synonyms/SynonymsController.cs`) | new namespace + new policy `[Authorize(Policy = "codeart:graphsearchtools")]` |
| `NTI.WebExtensions/GraphSearch/GraphSearchApiController.cs` | split into `Tools/Pinned/PinnedApiController.cs`, `Tools/Synonyms/SynonymsApiController.cs`, `Tools/Sites/SitesApiController.cs`, `Tools/Content/ContentLookupApiController.cs` | strip role-based `[Authorize(Roles = ...)]`, replace with policy `[Authorize(Policy = "codeart:graphsearchtools")]` + `_accessChecker.HasAccess()` per action; add `[RequireAjax]` to POST/PUT/DELETE; never expose `ex.Message` in error responses |
| `NTI.WebExtensions/GraphSearch/GraphSearchManagementService.cs` | `src/GraphSearchtools/Services/GraphAdminClient.cs` | becomes the single Tier-3 abstraction (`IGraphAdminClient`) so CMS-13 SDK swap is contained |
| `NTI.WebExtensions/GraphSearch/GraphSearchModels.cs` | `src/GraphSearchtools/Services/GraphModels.cs` | namespace-only rename |
| `NTI.WebExtensions/GraphSearch/GraphSearchViewModel.cs` | absorbed into per-tool view models under `Tools/{Pinned,Synonyms}/Models/` | namespace-only rename |
| `NTI.WebExtensions.Views/Views/GraphSearch/Index.cshtml` | split into `Views/Pinned/Index.cshtml` and `Views/Synonyms/Index.cshtml` | drop the Health tab entirely (Phase 4 if revived); split the ~1000-line vanilla JS into `pinned.js` + `synonyms.js` under the module's `ClientResources/js/`; rewrite the inline CSS to use `gst-*` classes from `graphsearchtools.css`; switch layout to `_SearchtoolsLayout.cshtml` |
| `NTI.WebExtensions/ServiceCollectionExtensions.cs` | merge into the new `AddGraphSearchtools` extension | register `services.AddHttpClient<IGraphAdminClient, GraphAdminClient>()` |
| `NTI.WebExtensions.Tests/GraphSearch/GraphSearchManagementServiceTests.cs` | `src/GraphSearchtools.Tests/GraphAdminClientTests.cs` | keep both pinned tests: Basic-auth header + `text/plain` synonyms PUT body. These are contracts with Optimizely Graph and must not regress. |

### 3.2 Generalisations required during the port

These are the **must-fix** items flagged in `docs/research/nti-graphsearch-source.md` §6.
None can be deferred:

1. **Sites enumeration** — replace any `StartPage.LanguageSitesOrDefault()` style
   call with a generic helper that uses `ISiteDefinitionRepository` /
   `LanguageBranchRepository` to enumerate language-site pairs. Lives at
   `Helpers/LanguageSiteEnumerator.cs`. CMS-12/13 differences (if any) handled
   per the multi-target tiering rules in EditorPowertools' `CLAUDE.md`.
2. **Authorization** — replace `[Authorize(Roles = "Administrators,WebAdmins")]`
   with `[Authorize(Policy = "codeart:graphsearchtools")]` matching the existing
   permissions wiring. Roles are configurable through `GraphSearchtoolsOptions.AuthorizedRoles`.
3. **Content-type list in GraphQL** — the seed code hardcodes two content type names
   inside the inline GraphQL strings (`SearchContentAsync` and `ResolveByGuidsAsync`).
   Replace with a generated `_or` clause built from
   `GraphSearchtoolsOptions.SearchableContentTypes`. Default to `["_Page"]` so a
   vanilla install works.
4. **Layout** — switch the view's `Layout = "../Shared/_Layout.cshtml"` reference
   to the new `_SearchtoolsLayout.cshtml`.
5. **Health tab — drop in v0.1.** The functional seed's Health tab depends on a
   bespoke background service in a host project that is out of scope. Remove the
   Health tab from the view, but keep `[HttpGet("/api/graph-search/health")]` and
   `[HttpGet("/api/graph-search/health/log")]` returning empty snapshots when no
   `IGraphHealthStore` is registered (the existing null-safe behaviour). This keeps
   the API surface stable for an opt-in `Umage.Optimizely.GraphSearchtools.Health`
   sister package later.
6. **Configuration source** — read Graph credentials from
   `Optimizely:ContentGraph` first (so existing hosts don't double-configure), with
   `CodeArt:GraphSearchtools:Graph` as override. Encode in the
   `GraphAdminClient` constructor via a small `IGraphCredentialsResolver` shim.

### 3.3 New scaffolding for these two tools

For each new tool (one folder under `Tools/`):

- `Tools/{Tool}/{Tool}Controller.cs` — serves the Razor view; thin.
- `Tools/{Tool}/{Tool}ApiController.cs` — REST API; policy-protected; per-action
  `_accessChecker.HasAccess(...)`; `[RequireAjax]` on writes.
- `Tools/{Tool}/{Tool}Service.cs` — orchestrates `IGraphAdminClient` calls.
- `Tools/{Tool}/Models/*.cs` — DTOs (records, `JsonNamingPolicy.CamelCase`, ignore-nulls).
- `Views/{Tool}/Index.cshtml` — view; uses `_SearchtoolsLayout.cshtml`.
- `modules/_protected/GraphSearchtools/ClientResources/js/{tool}.js` — vanilla JS,
  reuses `GST.fetchJson`, `GST.postJson`, `GST.createTable`, `GST.contentPicker`.
- `lang/*.xml` keys under `/graphsearchtools/ui/{tool}/...` and
  `/graphsearchtools/menu/{tool}` and `/graphsearchtools/permissions/{tool}`.
- `Permissions/GraphSearchtoolsPermissions.cs` — add
  `new PermissionType("GraphSearchtools", "{Tool}")`.
- `Configuration/FeatureToggles.cs` — add `bool {Tool} { get; set; } = true`.
- `Menu/GraphSearchtoolsMenuProvider.cs` — add a `MenuItem` with
  `IsAvailable = ctx => featureAccessChecker.HasAccess(...)`, `SortIndex` per
  `addon-design.md` §3.7 grouping (100 between groups, 10 between siblings).
- `Tools/Overview/Index.cshtml` — add a tile.

### 3.4 Phase 1 acceptance

- A fresh sample-site install with valid Graph credentials shows the **Pinned Results**
  and **Synonyms** menu items.
- Pinned: editor can create a collection, add/edit/delete pinned items with the
  inline content picker, save individual rows, and see results pinned in a search
  query they run separately.
- Synonyms: editor can edit per-language plus Global synonym blobs, save, and
  observe behaviour in a search query against Graph.
- `dotnet test` green on both TFMs; the two upstream-contract tests (Basic-auth,
  `text/plain` synonyms PUT) are part of the suite.
- Tagged `v0.1.0`.

---

## 4. Phase 2 — Search playground (the immediate differentiator)

**Goal:** Editors and developers can interrogate Graph from within the CMS shell —
no GraphQL playground required.

New tools (each follows the §3.3 scaffolding):

| Tool | What ships | Notes |
|---|---|---|
| **Search Console** | Query box, locale picker, `_ranking` mode toggle, `_semanticWeight` slider, `_minimumScore` input, results table with `_score` and `_fulltext` snippet, facet panel. Side-by-side mode renders two configs in parallel for visual A/B. | Backed by `IGraphAdminClient.RunSearchAsync(GraphQlDoc, vars)`. Reuse `GraphAdminClient`'s GraphQL POST plumbing from Phase 1. |
| **Saved Queries** | CRUD for named queries (name + GraphQL document + variable defaults). Run-from-list reuses Search Console. | Stored via Graph saved-queries admin API. |
| **Autocomplete Tester** | Single text box → list of suggestions; per-locale toggle; optional `where` filter. | Calls `autocomplete(value, limit)` per `docs/research/optimizely-graph-site-search.md` §5. |
| **Connectivity Tester** | One-page health view: gateway reachability, AppKey/Secret valid, role permissions, content-sync-recent. Three-light panel (green/amber/red). | Probe = a tiny `Content { total }` query plus a HEAD on `/resources/synonyms`. |

### 4.1 Search Console — extra detail

The console's UI maps directly to the levers in
`docs/research/relevancy-optimization.md`:

- **Match operator + per-field boosts** — section §2 of that doc. UI offers a
  field/boost grid that compiles to the `_or` clause shown in §11's tuned query.
- **`_ranking` toggle** — `RELEVANCE | SEMANTIC | BOOST_ONLY | DOC`.
- **`_semanticWeight` slider** — float, default 0.2, range -1.0…1.0.
- **`_minimumScore` input** — number; tooltip explains it auto-activates
  ranking when set.
- **Decay/Factor builder** — collapsible panel; emits decay/factor fragments to
  paste into the query (or attach to the live console run).
- **Result row**: name, content type, language, score, `_fulltext` snippet,
  deep-link to edit-mode (when the host has CMS edit access for the result).
- **Side-by-side mode**: two columns, two control panels, shared query string.
  Diff highlight on rows where rank differs by ≥ 3 positions.

Persist control-panel state per-user via `UserPreferencesService` (carried over
from EditorPowertools framework).

Phase 2 also adds **PinAndCurate-from-result**: a "Pin this result" button on each
row that pre-fills a new pinned item using the current query phrase + selected
content. This is the editorial workflow the seed addon doesn't have today.

### 4.2 Phase 2 acceptance

- Can craft, run, save, replay, and side-by-side compare queries through the UI.
- Connectivity Tester gives an unambiguous pass/fail status for support to
  screenshot when a customer reports a search issue.
- Tagged `v0.2.0`.

---

## 5. Phase 3 — Tuning power tools

The "make levers visible" group:

| Tool | Detail |
|---|---|
| **Decay & Factor Sandbox** | Visualize Gaussian decay (`origin` / `scale` / `rate`) and `factor` modifier curves (`SQRT`/`LOG`/`RECIPROCAL`/`SQUARE`/`NONE`). Output the GraphQL fragment ready to paste. |
| **Semantic Weight Tuner** | Tiered policy (e.g. `≤2 tokens → RELEVANCE`, `≥3 tokens → SEMANTIC w=0.3`). Persist as a config bundle and emit `appsettings.json` snippet. |
| **Schema Inspector** | Per-content-type view: searchable / filterable / facetable per field. Hits Graph schema endpoint. |
| **Webhooks** | List/create/delete Graph webhooks. Edit = recreate per the upstream constraint, with a clear UI hint. |
| **Custom Data Sources** | Inspect non-CMS sources, trigger full resync per source. |
| **Request Logs** | Recent Graph queries with timing + ranking + result count. Click → replay in Search Console. |

These all reuse `IGraphAdminClient` (extended with new methods per surface) and the
table/dialog components from the framework. No new framework code.

Tagged `v0.3.0`.

---

## 6. Phase 4 — Analytics & audits

| Tool | Source |
|---|---|
| **Search Logs** | Top queries, zero-result %, low-CTR head queries, session-rephrasing pairs — the synonym-mining surface in `docs/research/relevancy-optimization.md` §6.1. Two ingestion paths: (a) the host site POSTs each public-search hit to a `GraphSearchtools` log endpoint (preferred — gives CTR), (b) we poll Graph's request log API as fallback. |
| **Index Health** | Index size by content type; missing fields (Name/Title); strings flagged unsearchable that "should be"; recent reindex deltas. |
| **Content Searchability Audit** | Local content scan: empty `Name`, missing `MainBody`, no `Tags`, sortable text fields > 1024 chars (per `optimizely-graph-site-search.md` §3 caveat). Deep-links to edit-mode. |
| **Pinned Result Coverage** | Pin overlap heatmap, expired pins, low-CTR pins, pins where target content was unpublished/deleted. |
| **Synonym Coverage** | Synonyms not used in any logged query (suggest pruning); top zero-result queries that look like missing synonyms (suggest adding). |

These tools require log capture; introduce a small `DynamicDataStore` table for
search-log ingest (same pattern EditorPowertools uses for its analysis jobs).

Tagged `v0.4.0`.

---

## 7. Phase 5 — Relevancy Lab

The most ambitious tool — build only after Search Console has matured.

- Define a "golden query set" (query + expected top-N content).
- Run the set against two configurations.
- Show NDCG@10 / MRR per query and overall, per-query winners, and a deltas table.
- Export as CSV.

Backed by repeated `Content` queries with varying `where`/`orderBy`, tracked in a
DDS-backed result store so historical runs can be compared.

Tagged `v0.5.0`.

---

## 8. Cross-cutting decisions

These apply to every phase, captured here so they don't get re-debated tool by tool.

1. **Reuse host's Graph credentials by default.** Bind both `Optimizely.ContentGraph`
   (host) and `CodeArt:GraphSearchtools:Graph` (override). Prefer override only when
   populated.
2. **Single Graph SDK abstraction (`IGraphAdminClient`).** Tier-3 internal interface
   per EditorPowertools' multi-target rules. Concrete CMS-12 implementation uses the
   raw HttpClient pattern from the seed code; CMS-13 implementation may swap to the
   official Graph SDK once it stabilises (announced March 2026 per
   `docs/research/optimizely-graph-site-search.md` §10).
3. **Authorization is policy-based, not role-based.** All controllers carry
   `[Authorize(Policy = "codeart:graphsearchtools")]`. All actions call
   `_accessChecker.HasAccess(...)` so per-feature lockdown works. Writes carry
   `[RequireAjax]`. Errors never leak `ex.Message`.
4. **Localization for every user-facing string.** No hardcoded display text in C#
   or in JS. JS reads from `window.GST_STRINGS.{tool}.{key}` populated by
   `UiStringsProvider`. English is the base; other 10 languages start as English
   placeholders marked `TODO_TRANSLATE`.
5. **No SignalR.** Phase 0 drops it entirely. Re-introduce only if a live
   indexing-status stream becomes a tool requirement (it isn't today).
6. **No build step on the front end.** Vanilla JS + CSS only, mirroring
   EditorPowertools and the seed addon. Keeps the package install small and
   eliminates a node toolchain from the contributor experience.
7. **No external CSS/JS CDN dependencies.** Everything ships embedded in the RCL.
8. **Watermark "Powered by umage.ai"** stays in the layout footer.
9. **License: MIT**, `Authors = UmageAI`, `PackageProjectUrl = https://github.com/umage-dk/Umage.Optimizely.GraphSearchtools` (or whichever org URL is settled).

---

## 9. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Optimizely Graph admin API changes break the rename-only port | Pin the two `text/plain` synonyms PUT and Basic-auth header tests as upstream contracts. They've already caught one regression in the source addon. |
| Hardcoded content types break the picker for vanilla CMS sites | Default `SearchableContentTypes = ["_Page"]` and document override clearly in README. Add a sample-site smoke test that runs the picker against an empty content tree. |
| CMS 13 Graph SDK ships with a different surface than the current REST/GraphQL path | Tier-3 abstraction (`IGraphAdminClient`) isolates the swap. CMS 12 stays on the HttpClient path indefinitely. |
| OptiGraphExtensions users carry pinned/synonym data created via the third-party tool | Both tools talk to the same Graph admin APIs and read the same data — no migration needed. Add an integration check in Phase 1 acceptance: install on a Graph instance previously managed by the third-party tool and confirm we see and edit the same pins/synonyms. |
| Search-log ingestion (Phase 4) requires host-site instrumentation we don't ship | Provide a single-method client SDK (`GraphSearchtools.Telemetry.LogQuery(...)`) in a sister package — opt-in, documented, falls back to Graph request log polling when not adopted. Decide name after Phase 3. |
| The `Health` tab from the seed addon was useful but tied to host-specific code | Defer to an opt-in `Umage.Optimizely.GraphSearchtools.Health` sister package post-v0.4. Keep the no-op endpoints in core so the API contract is stable. |

---

## 10. Definition of done per phase

Each phase ships when:

1. New tools listed for the phase work end-to-end on CMS 12 sample site.
2. Same tools work end-to-end on CMS 13 sample site.
3. `dotnet test` green on both TFMs.
4. Loc keys present in all 11 language files (English text complete; other 10
   may be `TODO_TRANSLATE` placeholders that still resolve to a sensible string).
5. `README.md` documents the new tools with a screenshot apiece.
6. A signed git tag (`vX.Y.Z`) triggers `publish.yml`, the workflow goes green,
   the `.nupkg` is attached to the GitHub release, and a separately-managed
   smoke test against a real Graph endpoint passes.

---

## 11. Out of scope (intentionally)

- A public-site search-results UI. This add-on is editor/admin only. A sister
  package `UmageAI.Optimizely.GraphSearchTools.Frontend` (typed query builder +
  log instrumentation hook) is a candidate for after Phase 2 but not part of
  this plan.
- Custom Lucene analyzers / tokenizers — not supported by the platform.
- Learned-to-rank ML re-ranking — not supported by the platform; out of scope to
  emulate client-side.
- Migration of the partner-codebase Health tab. The capability re-appears, if at
  all, as an opt-in sister package.
