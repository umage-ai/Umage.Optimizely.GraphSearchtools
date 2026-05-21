# GraphSearchtools

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Marketer-facing admin tooling for **Optimizely Graph** site-search on Optimizely CMS 12 and CMS 13. Pinned-result curation, synonym management, a per-channel try-it playground, and click-through / zero-result insights — all integrated into the CMS shell. Distributed as the NuGet package `UmageAI.Optimizely.GraphSearchTools`.

![Overview Dashboard](docs/screenshots/01-overview.webp)

## Tools

Tools are grouped in the Graph Search Tools section of the CMS shell.

### Tuning

| Tool | Description |
|------|-------------|
| **Search channels** | Per-surface tuning index. Each registered search channel gets a detail page with KPI strip, Try-it live preview, and Pinned / Synonyms / Settings tabs scoped to that surface. |
| **Pinned results** | Tenant-global browser for every pinned-result collection. Filter by collection or locale; jump to the owning channel with one click. |
| **Synonyms** | Replacement and equivalent synonym rules per locale. Changes hit Optimizely Graph immediately; an activity column shows last-30-days impact. |

### Analytics

| Tool | Description |
|------|-------------|
| **Insights** | Cross-channel dashboard. Top phrases, zero-result candidates, low-CTR phrases. Filter by channel and locale; switch between 1h / 24h / 7d / 30d windows. |
| **Search-log telemetry** | Public ingest beacon (`POST /api/telemetry/searchlog`) plus a self-contained DDS sink that powers every analytics surface in the addon — no external pipeline required. |

### Coverage audits (inside Channel Detail)

| Audit | Description |
|------|-------------|
| **Pinned coverage** | Surface unpublished / deleted pin targets, expired pins, low-CTR pins, and pins with no recent search activity. |
| **Synonym coverage** | Cross-references saved synonyms against the search-log table to surface unused entries and zero-result phrases that look like missing synonyms. |

## Screenshots

### Overview

![Overview](docs/screenshots/01-overview.webp)

Dashboard of all Graph Search Tools, with quick-access cards for each surface.

### Search channels

![Search channels](docs/screenshots/02-channels.webp)

Index of every registered channel. Filter by site or locale. Activity sparkline and totals come from the search-log table so you can spot dormant surfaces at a glance.

### Channel detail

![Channel detail](docs/screenshots/03-channel-detail.webp)

KPI strip (searches / CTR / zero-result) plus a Try-it live preview that runs the same GraphQL document your production storefront fires. The right pane swaps between **Insights** (per-channel top phrases / zero-result / low-CTR) and **Pinned / Synonyms / Settings** tabs without losing the preview state.

### Insights

![Insights](docs/screenshots/04-insights.webp)

Cross-channel marketer dashboard. Switch tabs between Top phrases, Zero-result phrases, and Low-CTR phrases; filter by channel, locale, and time window. Click any phrase to jump straight to pinning or synonym creation.

### Pinned results

![Pinned results](docs/screenshots/05-pinned.webp)

Tenant-global pin browser. One row per `(phrase, collection, locale)` with the resolved channel link, item count, and last-30-days activity from search logs.

### Synonyms

![Synonyms](docs/screenshots/06-synonyms.webp)

Replacement and equivalent rules per locale, with last-30-days activity. Graph synonyms live in a tenant-global pool, so this surface is channel-agnostic by design.

## Search channels

A **search channel** is a developer-declared description of one search surface in the customer solution (header search, product listing, knowledge base, …). Channels are registered at startup and scope the marketer-facing Pinned editor to the keys the production GraphQL query actually consumes — so a pinned result added in the CMS shell is guaranteed to fire in the live query rather than disappearing into a tenant-global collection nobody reads.

Register channels with the fluent `AddSearchChannel(...)` extension on the builder returned by `AddGraphSearchtools`:

```csharp
// src/GraphSearchtools.SampleSite/Startup.cs
services.AddGraphSearchtools()
    .AddSearchChannel("alloy-search", p => p
        .DisplayName("Alloy site search")
        .Description("Header search across the Alloy demo content.")
        .LocalesFromCmsLanguages()
        .SearchedFields("Name", "MetaDescription", "MainBody")
        .UsesPinnedKey("alloy-{locale}")
        .SemanticBlend(0.3, GraphRanking.Semantic)
        .GraphQLDocument("Queries/AlloySearch.graphql"));
```

| Builder method | What it does |
|---|---|
| `DisplayName(string\|LocalizedString)` | Display name shown on the Channels index and detail header. Accepts a literal string or a localization key. |
| `Sites(params string[])` | `SiteDefinition.Name` values this channel applies to. Empty list (default) means "all sites". |
| `Locales(params string[])` / `LocalesFromCmsLanguages()` | BCP-47 language codes that drive the locale picker. Use the CMS-derived helper to track enabled language branches automatically. |
| `SearchedFields(params string[])` | Field names searched by the production query. Used by the Try-it side panel and the search-coverage audit. |
| `UsesPinnedKey(string \| Func<string,string>)` | The Graph pinned-collection key formula. String templates may contain `{locale}`, e.g. `"alloy-{locale}"`; pass a `Func<string,string>` for fully dynamic per-locale keys. |
| `SemanticBlend(double weight, GraphRanking ranking)` | Default semantic weight (clamped to -1.0…1.0) and ranking mode used by the Try-it side panel. |
| `GraphQLDocument(string path)` / `GraphQLDocumentInline(string body)` | The GraphQL document the production code uses for this surface. Optional — when omitted the Try-it side panel is disabled but Pinned editing still works. |
| `Variables(object \| IDictionary<string,object?>)` | Default variables passed to the GraphQL document in addition to the runner-controlled ones (`q`, `locale`, `limit`, …). |

When no channels are registered the addon synthesises a single **Generic** channel so zero-config installs keep working: the Channels index shows one row, and the Pinned tab on it falls back to the legacy free-form collection-name editor.

For the design rationale, data model, and migration path, see [`docs/search-channels-design.md`](docs/search-channels-design.md).

## CMS 12 and CMS 13

GraphSearchtools is multi-targeted: the same NuGet package supports both Optimizely CMS 12 (.NET 8) and CMS 13 (.NET 10). The addon talks to Optimizely Graph directly over HTTP — it does **not** depend on `Optimizely.ContentGraph.Cms` (CMS 12) or `Optimizely.Graph.Cms.ContentSources` (CMS 13), so it works against any tenant the host can reach, however the host wires up content sync.

| | CMS 12 | CMS 13 |
|---|---|---|
| Target framework | net8.0 | net10.0 |
| Host package for content sync | `Optimizely.ContentGraph.Cms` 4.x | `Optimizely.Graph.Cms.ContentSources` 13.x |
| Shell base path | `/EPiServer/CMS/` | `/Optimizely/CMS/` |
| Addon API surface | identical | identical |

The repo ships two reference sample sites — `src/GraphSearchtools.SampleSite` (CMS 12) and `src/GraphSearchtools.SampleSiteCms13` (CMS 13) — to use as templates for either platform version.

## Installation

```bash
dotnet add package UmageAI.Optimizely.GraphSearchTools
```

### Registration

In your `Startup.cs`:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // ... other services

    services.AddGraphSearchtools(options =>
        {
            // Optional: configure roles with full access
            // (defaults cover standard CMS-admin and edit-mode groups).
            options.AuthorizedRoles = ["WebAdmins", "Administrators"];

            // Optional: disable per-tool access control
            // (default: true; granular permissions via CMS admin UI).
            options.CheckPermissionForEachFeature = true;
        })
        .AddSearchChannel("alloy-search", p => p
            .DisplayName("Alloy site search")
            .LocalesFromCmsLanguages()
            .SearchedFields("Name", "MetaDescription", "MainBody")
            .UsesPinnedKey("alloy-{locale}")
            .GraphQLDocument("Queries/AlloySearch.graphql"));
}

public void Configure(IApplicationBuilder app)
{
    // ... other middleware

    app.UseGraphSearchtools();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapContent();
        endpoints.MapGraphSearchtools(); // Required: maps tool routes
    });
}
```

## Configuration

```json
{
  "Optimizely": {
    "ContentGraph": {
      "GatewayAddress": "https://cg.optimizely.com",
      "AppKey": "...",
      "Secret": "...",
      "SingleKey": "..."
    }
  },
  "UmageAI": {
    "GraphSearchTools": {
      "AuthorizedRoles": ["WebAdmins", "Administrators"],
      "CheckPermissionForEachFeature": true,
      "Features": {
        "Overview": true,
        "Channels": true,
        "Insights": true,
        "Pinned": true,
        "Synonyms": true,
        "PinnedCoverage": true,
        "SynonymCoverage": true,
        "Telemetry": true
      }
    }
  }
}
```

The addon reuses the host's `Optimizely:ContentGraph` credentials by default. Per-environment overrides can be set under `UmageAI:GraphSearchTools:Graph`; any value left blank falls back to the host's block.

## Permissions

Three-layer permission model:

1. **Feature toggles** — Enable/disable individual tools via the `Features` block in configuration. A disabled tool is hidden from the menu and its routes return 404.
2. **Role-based access** — `AuthorizedRoles` grants full access (defaults cover the standard CMS-admin and edit-mode groups).
3. **Permissions For Functions** — With `CheckPermissionForEachFeature = true` (default), each tool can be granted to specific users/roles in the CMS admin UI under "Permissions For Functions". `Pinned` and `Synonyms` split into view + edit; `Insights` is the umbrella permission for all read-only analytics surfaces. On first boot `PermissionSeeder` grants every permission to the configured `AuthorizedRoles` so a fresh install never locks anyone out.

## Scheduled Jobs

| Job | Purpose |
|-----|---------|
| **GraphSearchtools — Telemetry retention** | Trims aged buckets from the local search-log store. Runs nightly. Configure retention via `UmageAI:GraphSearchTools:Telemetry:RetentionDays` (default 90). |

Run from the CMS admin Scheduled Jobs page or trigger from the Telemetry settings tab.

## Documentation

- [Integrator quickstart](docs/integrator/quickstart.md) — install, register a search channel, and see a telemetry event in Insights.
- [Server-rendered ASP.NET pattern](docs/integrator/pattern-server-rendered.md) — inject `ITelemetrySink` and emit events from your search controller.
- [Headless deployment pattern](docs/integrator/pattern-headless.md) — beacon events from a SPA/SSR frontend to the public ingest endpoint.
- [3rd-party telemetry pattern](docs/integrator/pattern-third-party-telemetry.md) — decorate `ITelemetrySink` to fan out, or replace `ITelemetryReader` to source aggregates from your own warehouse.
- [Design system](docs/design-system.md) — shared visual patterns and components.
- [Personas](docs/personas.md) — who the addon is designed for.
- [Optimizely Graph research notes](docs/research/) — reference docs on Graph capabilities, relevancy, and authentication.

## Tech Stack

- .NET 8 / .NET 10 / C# / Optimizely CMS 12 and CMS 13 (single multi-targeting NuGet package)
- Vanilla JavaScript and CSS (no framework dependencies, no build step)
- Razor SDK class library with embedded views and static assets
- DynamicDataStore (DDS) for persistence
- Protected module integration with the CMS shell
- 11 language files included for localization (en, da, sv, no, de, fi, fr, es, nl, ja, zh-CN)

## Contributing

See [`CLAUDE.md`](CLAUDE.md) for architecture patterns and conventions.

## License

MIT — see [LICENSE](LICENSE).

Powered by [umage.ai](https://umage.ai).
