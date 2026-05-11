# GraphSearchtools

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Graph search tooling for Optimizely CMS 12 and CMS 13. A CMS-shell-integrated admin
area that exposes pinned-result curation, synonym management, a query playground,
and relevancy-tuning tools — all built on Optimizely Graph.

Distributed as the NuGet package `UmageAI.Optimizely.GraphSearchTools`.

## Status

Pre-release. Phase 0 (framework fork) is in progress. Tools land per the
phased plan in `docs/implementation-plan.md`.

## Install

```bash
dotnet add package UmageAI.Optimizely.GraphSearchTools
```

```csharp
// Startup.cs
services.AddGraphSearchtools(o => Configuration.GetSection("CodeArt:GraphSearchtools").Bind(o));
app.UseGraphSearchtools();
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
  "CodeArt": {
    "GraphSearchtools": {
      "AuthorizedRoles": ["WebAdmins", "Administrators"],
      "Features": { "Overview": true }
    }
  }
}
```

The add-on reuses the host's `Optimizely:ContentGraph` credentials by default; the
`CodeArt:GraphSearchtools:Graph` block can override them per-environment.

## Search Profiles (v0.2.5)

A **search profile** is a developer-declared description of one search surface
in the customer solution (header search, product listing, knowledge base, …).
Profiles are registered at startup and scope the marketer-facing Pinned editor
to the keys the production GraphQL query actually consumes — so a pinned
result added in the CMS shell is guaranteed to fire in the live query rather
than disappearing into a tenant-global collection nobody reads.

Register profiles with the fluent `AddSearchProfile(...)` extension on the
builder returned by `AddGraphSearchtools`. The Alloy CMS 12 sample registers
two profiles:

```csharp
// src/GraphSearchtools.SampleSite/Startup.cs
services.AddGraphSearchtools(options =>
    {
        // Configure options here or in appsettings.json under "CodeArt:GraphSearchtools"
    })
    .AddSearchProfile("alloy-search", p => p
        .DisplayName("Alloy site search")
        .Description("Header search across the Alloy demo content.")
        .Locales("en")
        .SearchedFields("Name", "MetaDescription", "MainBody")
        .UsesPinnedKey("alloy-{locale}")
        .SemanticBlend(0.3, GraphRanking.Semantic)
        .GraphQLDocument("Queries/AlloySearch.graphql"))
    .AddSearchProfile("alloy-products", p => p
        .DisplayName("Product cards")
        .Description("Pinned recommendations for the product/teaser surface.")
        .Locales("en")
        .SearchedFields("Name", "TeaserText")
        .UsesPinnedKey("alloy-products-{locale}"));
```

| Builder method | What it does |
|---|---|
| `DisplayName(string\|LocalizedString)` | Display name shown on the Profiles index and detail header. Accepts a literal string or a localization key. |
| `Sites(params string[])` | `SiteDefinition.Name` values this profile applies to. Empty list (default) means "all sites". |
| `Locales(params string[])` | BCP-47 language codes (lowercased). Drives the locale picker in the Pinned editor. Empty list means "all locales". |
| `SearchedFields(params string[])` | Field names searched by the production query. Used by the Try-it side panel and the search-coverage audit. |
| `UsesPinnedKey(string \| Func<string,string>)` | The Graph pinned-collection key formula. A string template may contain `{locale}`, e.g. `"alloy-{locale}"`; pass a `Func<string,string>` for fully dynamic per-locale keys. |
| `SemanticBlend(double weight, GraphRanking ranking)` | Default semantic weight (clamped to -1.0…1.0) and ranking mode used by the Try-it side panel. |
| `GraphQLDocument(string path)` | Path (relative to the host's content root) to the GraphQL document the production code uses for this surface. Optional — when omitted the Try-it side panel is disabled but Pinned editing still works. |
| `Variables(object \| IDictionary<string,object?>)` | Default variables passed to the GraphQL document in addition to the runner-controlled ones (`q`, `locale`, `limit`, …). Accepts an anonymous object or a dictionary. |

When no profiles are registered the addon synthesises a single **Generic**
profile so zero-config installs keep working: the Profiles index shows one
row, and the Pinned tab on it falls back to the legacy free-form
collection-name editor. Generic stays present alongside any registered
profiles and acts as the catchment for orphan collections that don't match a
declared `UsesPinnedKey` formula.

Pinned results are scoped to profiles. Marketers reach the editor at
`/EPiServer/cms/graphsearchtools/profiles/{key}`; the legacy
`/EPiServer/cms/graphsearchtools/Pinned` URL 301-redirects to the Profiles
index for hosts that linked directly to it. Synonyms and Saved Queries remain
top-level tools with their existing Phase 1 / Phase 2 UX — Graph admin
synonyms are tenant-global and aren't scoped to profiles.

For the design rationale, data model, and migration path, see
[`docs/search-profiles-design.md`](docs/search-profiles-design.md).

## License

MIT — see [LICENSE](LICENSE).

Powered by [umage.ai](https://umage.ai).
