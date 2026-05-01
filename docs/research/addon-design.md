# GraphSearchtools — Add-on Design Report

How to build `Umage.Optimizely.GraphSearchtools` as a sibling of `Umage.Optimizely.EditorPowertools`. The framework, packaging, and UX are copied 1:1 from EditorPowertools; only the tool set changes — instead of editor utilities, this add-on delivers tools focused on **Optimizely Graph search optimization**.

Research date: 2026-04-30. Sources for the Graph capabilities/relevancy material live in [optimizely-graph-site-search.md](./optimizely-graph-site-search.md), [relevancy-optimization.md](./relevancy-optimization.md), and [sources.md](./sources.md).

---

## 1. Why copy EditorPowertools verbatim

EditorPowertools is a mature Optimizely add-on:

- Multi-targets CMS 12 (.NET 8) and CMS 13 (.NET 10) from a single library.
- Ships as a Razor Class Library NuGet package (`UmageAI.Optimizely.EditorPowerTools`) with embedded views, lang files, JS, CSS, and a zipped `_protected` module deployed via `.targets`.
- Has a worked-out three-layer permission model (feature toggles + auth policy + per-feature `PermissionType`).
- Has a complete shell-integrated UX: the same chrome as the CMS shell (`ShellCore`, `CreatePlatformNavigationMenu`, `ApplyPlatformNavigation`), an Overview tool grid, a shared sidebar header, modal dialogs, sortable tables, content/content-type pickers, JS string bundle, dialog system, and a "Powered by umage.ai" watermark.
- Has a release pipeline that publishes via tag → GitHub Actions → Optimizely NuGet feed.

Re-deriving any of this for a search add-on would burn weeks. The cost of cloning is paying attention to the namespace/key replacements; the payoff is a same-day production-quality add-on.

## 2. Naming conventions and the search-and-replace map

When forking, replace these tokens consistently across code, build, and docs. The right column is the new value.

| EditorPowertools (source) | GraphSearchtools (this add-on) |
|---|---|
| Repo `Umage.Optimizely.EditorPowertools` | `Umage.Optimizely.GraphSearchtools` |
| NuGet `UmageAI.Optimizely.EditorPowerTools` | `UmageAI.Optimizely.GraphSearchTools` |
| .NET namespace root `UmageAI.Optimizely.EditorPowerTools` | `UmageAI.Optimizely.GraphSearchTools` |
| Project `EditorPowertools.csproj` | `GraphSearchtools.csproj` |
| Solution `EditorPowertools.slnx` | `GraphSearchtools.slnx` |
| Sample sites `EditorPowertools.SampleSite[Cms13]` | `GraphSearchtools.SampleSite[Cms13]` |
| Test project `EditorPowertools.Tests` | `GraphSearchtools.Tests` |
| Module folder `modules/_protected/EditorPowertools/` | `modules/_protected/GraphSearchtools/` |
| Menu base `MenuPaths.Global + "/cms/editorpowertools"` | `MenuPaths.Global + "/cms/graphsearchtools"` |
| Auth policy `codeart:editorpowertools` | `codeart:graphsearchtools` |
| Config section `CodeArt:EditorPowertools` | `CodeArt:GraphSearchtools` |
| `PermissionType` category string `"EditorPowertools"` | `"GraphSearchtools"` |
| Loc string root `/editorpowertools/...` | `/graphsearchtools/...` |
| JS globals `window.EPT_*`, `EPT.*`, `EPT_STRINGS` | `window.GST_*`, `GST.*`, `GST_STRINGS` |
| CSS prefix `ept-`, root vars `--ept-*` | `gst-`, `--gst-*` |
| Layout file `_PowertoolsLayout.cshtml` | `_SearchtoolsLayout.cshtml` |
| Shell title "Editor Powertools" | "Graph Search Tools" |
| DI extensions `AddEditorPowertools` / `UseEditorPowertools` / `MapEditorPowertools` | `AddGraphSearchtools` / `UseGraphSearchtools` / `MapGraphSearchtools` |
| `ProtectedModuleOptions` `Name = "EditorPowertools"` | `Name = "GraphSearchtools"` |
| Watermark "umage.ai" link | unchanged |

Keep EditorPowertools' MIT license, Authors `UmageAI`, and PackageProjectUrl pattern (with the new repo URL).

## 3. Framework pieces to copy 1:1

These files map directly. Only the namespace, route prefix, and string keys need updating.

### 3.1 Project layout

```
src/
├── GraphSearchtools/                          ← Razor Class Library (.csproj)
│   ├── Abstractions/                          ← cross-CMS interfaces (e.g. IGraphAdminAdapter)
│   ├── Cms12/                                 ← compiled only for net8.0
│   ├── Cms13/                                 ← compiled only for net10.0
│   ├── Configuration/
│   │   ├── GraphSearchtoolsOptions.cs
│   │   └── FeatureToggles.cs
│   ├── Infrastructure/
│   │   ├── ServiceCollectionExtensions.cs     ← AddGraphSearchtools
│   │   ├── ApplicationBuilderExtensions.cs    ← UseGraphSearchtools, MapGraphSearchtools
│   │   └── RequireAjaxAttribute.cs
│   ├── Localization/
│   │   ├── UiStringsController.cs
│   │   └── UiStringsProvider.cs
│   ├── Menu/
│   │   └── GraphSearchtoolsMenuProvider.cs
│   ├── Permissions/
│   │   ├── GraphSearchtoolsPermissions.cs
│   │   └── FeatureAccessChecker.cs
│   ├── Components/                            ← shared backend pickers
│   ├── Helpers/
│   ├── Services/
│   ├── Tools/
│   │   └── {Tool}/                            ← one folder per tool
│   │       ├── {Tool}ApiController.cs
│   │       ├── {Tool}Service.cs
│   │       └── Models/{Tool}Dtos.cs
│   ├── Views/
│   │   ├── Shared/_SearchtoolsLayout.cshtml
│   │   ├── Shared/Cms13/_SearchtoolsLayout.cshtml
│   │   ├── Overview/Index.cshtml
│   │   └── {Tool}/Index.cshtml                ← one per tool
│   ├── lang/                                  ← en/da/sv/no/de/fi/fr/es/nl/ja/zh-CN
│   ├── modules/_protected/GraphSearchtools/
│   │   ├── module.config
│   │   └── ClientResources/
│   │       ├── css/graphsearchtools.css
│   │       └── js/graphsearchtools.js + components.js + per-tool.js
│   ├── build/net8.0/UmageAI.Optimizely.GraphSearchTools.targets
│   └── build/net10.0/UmageAI.Optimizely.GraphSearchTools.targets
├── GraphSearchtools.SampleSite/               ← Alloy CMS 12 demo site
├── GraphSearchtools.SampleSiteCms13/          ← Alloy CMS 13 demo site
└── GraphSearchtools.Tests/                    ← multi-target test project
```

### 3.2 csproj (Razor SDK, multi-target)

Copy `EditorPowertools.csproj` verbatim and adapt:

- `<PackageId>UmageAI.Optimizely.GraphSearchTools</PackageId>`
- `Description` and `PackageTags` for Graph use case (`Optimizely;Graph;Search;Relevancy;Synonyms;PinnedResults`).
- Module zip target points at `modules\_protected\GraphSearchtools`.
- `.targets` files renamed to `UmageAI.Optimizely.GraphSearchTools.targets`.
- `EmbeddedResource Include="lang\*.xml"` unchanged.
- Multi-target `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` and `OPTIMIZELY_CMS12` / `OPTIMIZELY_CMS13` define-constants unchanged.
- The `Compile Remove="Cms13\**\*.cs"` / `Cms12\**\*.cs` patterns unchanged.
- The `Cms13\_SearchtoolsLayout.cshtml` virtual-path remap pattern unchanged.

Drop the `CsvHelper` and `ClosedXML` references unless we end up exporting search logs. Add Graph-specific dependencies later (see §6).

### 3.3 Module config

`modules/_protected/GraphSearchtools/module.config`:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<module viewEngine="Razor" clientResourceRelativePath="">
  <assemblies>
    <add assembly="GraphSearchtools" />
  </assemblies>

  <clientResources>
    <add name="graphsearchtools.css" path="ClientResources/css/graphsearchtools.css" resourceType="Style" />
    <add name="graphsearchtools.js"  path="ClientResources/js/graphsearchtools.js"   resourceType="Script" />
    <add name="graphsearchtools.components" path="ClientResources/js/components.js"  resourceType="Script" />
  </clientResources>

  <dojo>
    <paths>
      <add name="graphsearchtools" path="ClientResources/js" />
    </paths>
  </dojo>

  <clientModule initializer="graphsearchtools/commands/GraphSearchtoolsCommandsInitializer">
    <moduleDependencies>
      <add dependency="CMS" type="RunAfter" />
    </moduleDependencies>
    <requiredResources>
      <add name="graphsearchtools.css" />
      <add name="graphsearchtools.js" />
    </requiredResources>
  </clientModule>
</module>
```

(SignalR is not part of the initial scope; drop the `signalr.min.js` resource. We can re-introduce it if we add a live indexing-status hub.)

### 3.4 Service registration

Mirror `ServiceCollectionExtensions.AddEditorPowertools`. Strip out tool services that won't apply (Active Editors / SignalR, Content Importer parsers, Bulk Property Editor, etc.) and replace with the Graph tool services from §5. Keep:

- `AddOptions<GraphSearchtoolsOptions>` bound to `CodeArt:GraphSearchtools`.
- `IPostConfigureOptions<AuthorizationOptions>` building the `codeart:graphsearchtools` policy from `AuthorizedRoles`.
- `FeatureAccessChecker` singleton.
- `UserPreferencesService` singleton (per-user UI state — column choice, panel layout, semantic-weight slider position, etc.).
- `IContentTypeMetadataProvider` Cms12/Cms13 split for any tool that needs to enumerate content types.
- `UiStringsProvider` scoped registration for the JS string bundle.
- `ProtectedModuleOptions` add `new ModuleDetails { Name = "GraphSearchtools" }`.

### 3.5 Application builder

`UseGraphSearchtools` and `MapGraphSearchtools` are byte-for-byte copies of the EditorPowertools versions, replacing `EditorPowertoolsMenuProvider` with `GraphSearchtoolsMenuProvider`. The conventional route pattern stays `{basePath}/{controller}/{action}/{id?}`. (No SignalR hub; no visitor-group middleware.)

### 3.6 Permissions

Three-layer model unchanged. The `[PermissionTypes]` static class lists one `PermissionType("GraphSearchtools", "<ToolName>")` per tool listed in §5. Every `Tools/{Tool}/{Tool}ApiController.cs` carries `[Authorize(Policy = "codeart:graphsearchtools")]` and calls `_accessChecker.HasAccess(...)` in each action; POST/DELETE actions add `[RequireAjax]`.

### 3.7 Menu provider

Same `IMenuProvider` shape, same `Paths.ToResource(typeof(GraphSearchtoolsMenuProvider), ...)` pattern, same `IsAvailable` per-tool feature gate. The grouping comments (`── Search & Tuning ──`, `── Editorial Curation ──`, etc.) translate to the new groupings in §5.

### 3.8 Layout & JS bootstrap

`_SearchtoolsLayout.cshtml` is a copy of `_PowertoolsLayout.cshtml` with these substitutions:

- Header logo text → "Graph Search Tools".
- Header SVG → a search/lens or graph icon.
- About link target → `GraphSearchtools/About`.
- Bottom script block exposes:
  ```js
  window.GST_BASE_URL = '@Html.Raw(Paths.ToResource(typeof(GraphSearchtoolsMenuProvider), ""))';
  window.GST_CMS_URL  = '@Html.Raw(Paths.ToResource("CMS", ""))';
  window.GST_ADMIN_URL = '@Html.Raw(Paths.ToResource("EPiServer.Cms.UI.Admin", "default"))';
  window.GST_STRINGS = @Html.Raw(JsonSerializer.Serialize(UiStrings.GetAll()));
  window.GST_CMS13 = false; // overridden in Cms13/_SearchtoolsLayout.cshtml
  ```
- Watermark unchanged.
- The JS bundle file names update to `graphsearchtools.js` and `components.js`.

The shared `GST` JS object (renamed from `EPT`) keeps the same surface: `fetchJson`, `postJson`, `showLoading`, `showEmpty`, `openDialog`, `createTable`, `downloadCsv`, `icons`, `contentPicker`, `contentTypePicker`. CSS classes update from `ept-*` to `gst-*` mechanically.

### 3.9 Localization

`/lang/{en,da,sv,no,de,fi,fr,es,nl,ja,zh-CN}.xml` carries all UI strings. Top-level element renames from `<editorpowertools>` to `<graphsearchtools>`, and the `<EditorPowertools>` permission group becomes `<GraphSearchtools>`. The `UiStringsProvider` reads `/graphsearchtools/ui/{tool}/{key}` and emits a JSON tree that the JS layer accesses as `GST_STRINGS.{tool}.{key}`. Add the help paths (`/graphsearchtools/ui/help/{tool}`) for each tool.

### 3.10 Distribution

Mirror EditorPowertools:

- Tag `vMAJOR.MINOR.PATCH` → `.github/workflows/publish.yml` builds, tests both TFMs, packs, attaches the `.nupkg` to a GitHub release.
- The Optimizely NuGet feed pulls from the GitHub release; we never push to nuget.org directly.
- The `.targets` file extracts the embedded module zip to the consuming project's `modules/_protected/GraphSearchtools/` on first build.

## 4. What can be dropped

These EditorPowertools components are unrelated to Graph search and add weight if carried over:

- **SignalR + ActiveEditors hub/services** — drop unless we later add live index-sync presence.
- **Content Importer parsers (CSV/JSON/Excel) + ClosedXML/CsvHelper** — drop. Search-log export can use plain CSV without dependencies.
- **Bulk Property Editor**, **Manage Children**, **Content Audit/Type Audit/Type Recommendations**, **Audience Manager**, **Personalization Audit**, **Activity Timeline**, **Scheduled Jobs Gantt**, **Language Audit**, **Security Audit**, **Link Checker**, **CMS Doctor**, **Visitor Group Tester**, **Content Statistics**, **Content Details widget** — none belong in a search add-on.
- The `IContentAnalyzer` + `UnifiedContentAnalysisJob` aggregation pipeline — drop. Graph-side "audits" hit Graph admin APIs, not the local CMS content tree (with one exception, the **Content Searchability Audit** in §5.3).

The remaining shared core — DI bootstrap, options, permissions, menu, localization, layout, JS utilities, content/content-type pickers, user-preferences service — is exactly what we need.

## 5. Tool set

The Graph add-on's value is in tools that wrap and extend Optimizely Graph's relevancy levers (per [`relevancy-optimization.md`](./relevancy-optimization.md) §1–9) and its admin surface (synonyms, pinned results, webhooks, saved queries, custom data sources, request logs). The third-party `OptiGraphExtensions` Blazor add-on covers a subset; the goal here is to subsume that scope and add the tuning/insight tools that nobody is shipping.

The menu groups follow the same visual pattern as EditorPowertools (Section dividers in the menu provider, `SortIndex` step of 100 between groups, 10 between siblings).

### 5.1 Search & Tuning

| Tool | Purpose | Key Graph mechanic |
|---|---|---|
| **Search Console** | Live query tester. Type a query, see ranked results with `_score` and `_fulltext` snippets, toggle locale and ranking mode, change `_semanticWeight` with a slider, change `_minimumScore`, see facet counts. Side-by-side mode for two configurations. | `match`, `_ranking`, `_score`, `_fulltext`, `_semanticWeight`, `_minimumScore`. |
| **Relevancy Lab** | Define a "golden query set" (query + expected top-N content). Run the set against two configurations. Show NDCG@10 / MRR / per-query winners. Export results. | Repeated `Content` queries with varying `where`/`orderBy`. |
| **Decay & Factor Sandbox** | Visualise the Gaussian decay curve (`origin`/`scale`/`rate`) and the `factor` modifier curves (`SQRT`/`LOG`/`RECIPROCAL`/`SQUARE`/`NONE`). Output the GraphQL fragment for the host site to paste into its query builder. | `decay`, `factor`. |
| **Semantic Weight Tuner** | Configure a tiered policy (e.g. 1-token query → `RELEVANCE`, 2-3 → `_semanticWeight: 0.2`, 4+ → `0.4`). Persist as a config bundle and as a `appsettings` snippet. | `_ranking: SEMANTIC`, `_semanticWeight`. |

### 5.2 Editorial Curation

| Tool | Purpose | Key Graph mechanic |
|---|---|---|
| **Synonyms** | CRUD synonym groups, per language and per slot. Bulk import/export. Diff against current Graph state. | Graph synonym admin API. |
| **Pinned Results** | Manage pinned-result collections. Trigger phrase + ordered content list with start/end dates. Content picker reuses the shared `GST.contentPicker`. Conflict warnings on overlapping pins. | Graph pinned-results admin API. |
| **Saved Queries** | Store named queries (name + GraphQL document + variable defaults) for reuse from the Search Console and from external tools/CI. | Graph saved-queries admin API. |
| **Autocomplete Tester** | Try the root-level `autocomplete` field per locale. Show suggestion list, tweak the `where` filter, see how punctuation/word-length caps behave. | `autocomplete(value, limit)`. |

### 5.3 Analytics & Audits

| Tool | Purpose | Source |
|---|---|---|
| **Search Logs** | Top queries (volume, CTR, zero-result %), low-CTR head queries, queries with rephrasings in same session — the synonym-mining surface from `relevancy-optimization.md` §6.1. | Graph request logs / our own log capture. |
| **Index Health** | Index size by content type, missing fields per type (no Name/Title), unsearchable strings flagged "should be searchable", recent reindex deltas. | Graph schema endpoint + content-type metadata. |
| **Content Searchability Audit** | (Local content scan.) Find content items with empty `Name`, missing `MainBody`, no `Tags`, or sortable text fields longer than 1024 chars (Graph caveat). Deep-link to edit-mode. | `IContentRepository` + `IContentTypeMetadataProvider`. |
| **Schema Inspector** | Per-content-type view of which fields are searchable / filterable / facetable in the current Graph index. | Graph schema endpoint. |
| **Pinned Result Coverage** | Pin overlap heatmap, expired pins, low-CTR pins, pins where the underlying content has been unpublished/deleted. | Pins admin API + click logs. |
| **Synonym Coverage** | Synonyms not used in any logged query (suggest pruning); top zero-result queries that look like missing synonyms (suggest adding). | Synonyms API + search logs. |

### 5.4 Operations & Admin

| Tool | Purpose | Source |
|---|---|---|
| **Webhooks** | View/create Graph webhooks. Edit-equals-recreate per the Graph constraint. | Graph webhooks admin API. |
| **Custom Data Sources** | Inspect non-CMS sources pushed to the index; trigger full resync per source. | Graph data-sources API. |
| **Request Logs** | Recent Graph queries with timing, ranking mode, and result counts; click through to replay in Search Console. | Graph request log. |
| **Reindex / Sync** | Per-content-type reindex triggers. Show last sync timestamps. | Graph indexing API. |
| **AppKey Inventory** | List configured app keys, scopes, last-used timestamps, expiring keys. | Graph app-key admin. |
| **Connectivity Tester** | One-click check: endpoint reachability, AppKey/Secret valid, role permissions adequate, content sync healthy. Returns a single green/yellow/red panel for the support team. | Graph health endpoints + a probe query. |

### 5.5 Common UX patterns reused

- **Overview tile grid** — same `gst-tool-card` layout as the EditorPowertools overview, with one card per enabled tool.
- **Page header + stats row + cards** — `gst-page-header`, `gst-stats`, `gst-card` mirror `ept-*`.
- **Modal dialogs** for drill-downs — synonym group editor, pin editor, query replay.
- **Sortable tables** for log views, schema lists, key inventories.
- **Saved user preferences** for column choice, semantic-weight slider position, last-used locale.
- **Help button per tool** populated from `/graphsearchtools/ui/help/{tool}` localisation strings.

## 6. Configuration surface

`GraphSearchtoolsOptions` mirrors `EditorPowertoolsOptions`:

```csharp
public class GraphSearchtoolsOptions
{
    public FeatureToggles Features { get; set; } = new();
    public bool CheckPermissionForEachFeature { get; set; }
    public string[] AuthorizedRoles { get; set; } = ["WebAdmins", "Administrators"];

    // Add: Graph endpoint + credentials. Prefer reading Optimizely.ContentGraph
    // section from appsettings if the host already configures it; fall back to
    // GraphSearchtools-specific overrides.
    public GraphConnectionOptions Graph { get; set; } = new();
}

public class GraphConnectionOptions
{
    public string? GatewayUrl { get; set; }   // e.g. https://cg.optimizely.com
    public string? AppKey { get; set; }
    public string? Secret { get; set; }
    public string? SingleKey { get; set; }    // public single key for non-admin queries
}
```

In production, the host site likely already binds `Optimizely.ContentGraph` for the Graph SDK; `GraphSearchtools` should detect that and reuse it rather than re-prompting for credentials. Manual override remains available for per-environment switching.

`FeatureToggles` carries one `bool` per tool listed in §5, all defaulting to `true`. Per the EditorPowertools convention, every tool must:

1. Have a `bool` in `FeatureToggles`.
2. Have a `PermissionType` in `GraphSearchtoolsPermissions`.
3. Check both via `FeatureAccessChecker` in the controller and the menu's `IsAvailable`.

`appsettings.json` example:

```json
{
  "CodeArt": {
    "GraphSearchtools": {
      "checkPermissionForEachFeature": true,
      "authorizedRoles": ["WebAdmins", "Administrators", "SearchEditors"],
      "features": {
        "searchConsole": true,
        "relevancyLab": true,
        "synonyms": true,
        "pinnedResults": true,
        "searchLogs": true
      }
    }
  }
}
```

## 7. Multi-targeting strategy

CMS 12 (.NET 8) and CMS 13 (.NET 10) are supported from a single library, following the rules in `Umage.Optimizely.EditorPowertools/CLAUDE.md`:

- `<TargetFrameworks>net8.0;net10.0</TargetFrameworks>` plus `OPTIMIZELY_CMS12` / `OPTIMIZELY_CMS13` define-constants.
- Conditional `<PackageReference>` for `EPiServer.CMS` 12.* / 13.*.
- `Cms12/` and `Cms13/` folders compiled per-TFM.
- Single `_SearchtoolsLayout.cshtml` virtual path; physical CMS-13 variant lives at `Views/Shared/Cms13/_SearchtoolsLayout.cshtml`.

Optimizely's **CMS 13 Graph SDK** (announced March 2026 — see `optimizely-graph-site-search.md` §10) is the natural client on net10.0; on net8.0 we either use the older Graph SDK or fall back to a thin GraphQL client. Tier 3 abstraction (`Abstractions/IGraphAdminAdapter`) is the right place to encapsulate the SDK-version differences so shared code stays clean.

## 8. Phased delivery

Each phase is shippable by itself; later phases add tools rather than changing the framework.

### Phase 0 — Framework fork (no tools)

- Repo, csproj, multi-target setup, sample sites for CMS 12 and 13, test project.
- DI/options/permissions/menu/layout/JS bootstrap, all renamed.
- `Overview` page with an empty tile grid, About page, watermark.
- `.targets` + module zip + GitHub Actions publish workflow.
- Acceptance: install the package on a fresh Alloy site, hit the menu, see the Graph Search Tools shell render with CMS chrome.

### Phase 1 — Editorial parity (subsume OptiGraphExtensions)

- **Synonyms**, **Pinned Results**, **Saved Queries**, **Autocomplete Tester**, **Webhooks**, **Custom Data Sources**, **Request Logs**.
- Delivers the OptiGraphExtensions feature set (per `optimizely-graph-site-search.md` §9) on our UX, with first-class CMS 13 support.

### Phase 2 — Tuning power tools (the differentiator)

- **Search Console**, **Decay & Factor Sandbox**, **Semantic Weight Tuner**, **Schema Inspector**, **Connectivity Tester**.
- These give editors and developers the levers from `relevancy-optimization.md` in a UI, not buried in a GraphQL playground.

### Phase 3 — Analytics & relevancy ops

- **Search Logs** dashboard, **Index Health**, **Content Searchability Audit**, **Pinned Result Coverage**, **Synonym Coverage**.
- The mining surface for the iterative tuning loop in `relevancy-optimization.md` §10.
- Requires a small amount of log capture / aggregation (DDS-backed, same pattern as EditorPowertools `UnifiedContentAnalysisJob`).

### Phase 4 — Relevancy Lab

- Golden query sets, NDCG@10/MRR scoring, A/B compare. The most ambitious tool. Build only after the Search Console has matured.

## 9. Key decisions / open questions

1. **Reuse the host site's Graph credentials?** Recommend: yes, default. Bind both `Optimizely.ContentGraph` (host) and `CodeArt:GraphSearchtools:Graph` (override) and prefer the latter only when populated. Avoids double-configuration.
2. **Where do search logs come from?** Two paths:
   a. The host site instruments its public-search frontend to POST to a `GraphSearchtools` log endpoint (cleanest, gives us click-through-rate).
   b. We poll Graph's request log API (lower fidelity, no CTR).
   Likely both; (a) is the primary, (b) is the fallback when the host hasn't wired (a).
3. **Do we ship a search-results component for the public site?** Out of scope for this add-on (it's an editor/admin tool); but a small NuGet sister package `UmageAI.Optimizely.GraphSearchTools.Frontend` could expose a typed query builder + log instrumentation hook. Decide after Phase 2 once the tuning side stabilises.
4. **OptiGraphExtensions data migration?** If existing customers store synonyms/pins via the third-party add-on, our tools will read the same Graph admin APIs and see the same data — no migration needed. We should test against a Graph instance previously managed by OptiGraphExtensions to confirm.

## 10. Summary

Fork EditorPowertools' framework wholesale; replace its tool set with a purpose-built suite for Optimizely Graph search optimisation. The 1:1 reuse covers the entire shell, packaging, permissions, multi-targeting, localisation, and shared UI primitives — roughly 80% of EditorPowertools' code by line count. The new code is the thirteen-or-so tools that wrap Graph's relevancy levers, synonyms/pins admin, and analytics, delivered in four phases that each ship value on their own.
