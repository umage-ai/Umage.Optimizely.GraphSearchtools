# Release Notes

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

Then wire up `services.AddGraphSearchtools(...)`, `app.UseGraphSearchtools()`, and
`endpoints.MapGraphSearchtools()` in your CMS host, and set
`UmageAI:GraphSearchTools:AuthorizedRoles` in `appsettings.json`.
