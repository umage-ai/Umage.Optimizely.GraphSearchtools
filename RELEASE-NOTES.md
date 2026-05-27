# Release Notes

## v0.2.0

### Fixes

- **Pinned target names now resolve correctly on both CMS 12 and CMS 13.** The pinned-target name resolver was returning "Content not in index" for every entry on CMS 13 (Graph stores `_metadata.key` in 32-char "N" form while pinned targets are persisted as canonical hyphenated GUIDs, so the `in:` filter never matched) and 400-ing on CMS 12 against modern V2 Graph tenants. `ResolveByGuidsAsync` now normalises GUIDs to "N" form on send and back to canonical "D" form on parse, and uses the V2 `_Content` schema on both CMS versions.

### Changes

- **Insights window set unified across the top-level dashboard and the per-channel Insights tab.** Both surfaces now expose **24h / 7d / 30d** with **7d** as the default. The top-level Insights toolbar gains the **24h** option; the Channel > Insights segmented control drops **1h** and its default moves from 24h to 7d.

### Performance

- **`JsonSerializerOptions` hoisted to `static readonly` fields** in five hot-path services so it's allocated once per process rather than per request. Closes a code-quality finding on the public telemetry ingest endpoint and the same pattern in four sibling services.

## v0.1.0

First public release of GraphSearchtools for Optimizely CMS 12 and CMS 13.

A marketer-facing admin surface for **Optimizely Graph** that lives inside the CMS shell:
curate pinned results, manage synonyms, audit coverage, and read the search signals coming
back from the site — all without leaving the editor.

### Tools

- **Pinned Results** — Per-channel collections of pinned phrases. Add, reorder, schedule,
  and retire pins; collection-shell create/rename/delete is a separate authority scope so
  campaign provisioning stays with operators while pin curation stays with marketers.

- **Synonyms** — Global synonym rule editor (Graph's synonym pool is tenant-global,
  language-keyed). View and edit are split so read-only stakeholders can audit without
  edit rights.

- **Search Channels** — Inventory of configured search channels with a per-channel detail
  view: KPI strip, top phrases, zero-result candidates, low-CTR queries, pinned items in
  scope, and synonym coverage signals. Three marketer tools (Pinned, Synonyms, Insights)
  appear both as top-level menu entries and as scoped tabs inside Channel Detail.

- **Insights** — Read-only analytics umbrella: dashboard with top phrases, zero-result
  candidates, synonym-coverage signals, and a recent-activity strip. Per-channel Insights
  tab on Channel Detail. Pinned Coverage and Synonym Coverage audits surface
  unpublished/deleted pin targets, expired pins, unused synonyms, and zero-result phrases
  that look like missing synonyms.

- **Overview** — Landing dashboard with cards gated by per-user permissions; first stop
  for anyone opening the tool.

### Infrastructure

- Multi-target `net8.0` (CMS 12) / `net10.0` (CMS 13) packaging from a single csproj;
  Razor SDK with embedded views and a `modules/_protected/` zip dropped into the
  consumer's content root on first build via NuGet `.targets`.
- Three-layer permission model: `Features` toggle (whether the tool is wired) +
  `umageai:graphsearchtools` auth policy (who's in the door) + per-tool `PermissionType`
  (what they can do). `PermissionSeeder` grants every permission to `AuthorizedRoles` on
  first boot so a fresh install never locks anyone out.
- DynamicDataStore-backed persistence — no separate database setup required.
- Public telemetry beacon (`POST /api/telemetry/searchlog`) feeds the search-log
  analytics; configurable and gateable per environment.
- Localization across 11 languages (en, da, sv, no, de, fi, fr, es, nl, ja, zh-CN);
  English is canonical, others fall back to English until translated.
- Vanilla JS + CSS (`GST.*` namespace, `--gst-*` CSS vars) — no build step in the addon.

### Requirements

- Optimizely CMS 12 (`EPiServer.CMS` 12.\*) on .NET 8, **or**
- Optimizely CMS 13 (`EPiServer.CMS` 13.\*) on .NET 10
- Optimizely Graph tenant with `AppKey`, `Secret`, and `SingleKey` credentials
- A host project that already boots against the CMS

### Installation

See [`docs/integrator/quickstart.md`](docs/integrator/quickstart.md) for the 10-minute
end-to-end walkthrough. In short:

```bash
dotnet add package UmageAI.Optimizely.GraphSearchTools
```

Then wire up `services.AddGraphSearchtools(...)` and
`endpoints.MapGraphSearchtools()` in your CMS host, and set
`UmageAI:GraphSearchTools:AuthorizedRoles` in `appsettings.json`.
