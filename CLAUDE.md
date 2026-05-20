# GraphSearchtools

An Optimizely CMS 12 / 13 add-on that pairs the framework of `Umage.Optimizely.EditorPowertools`
with admin tooling for **Optimizely Graph** (pinned results, synonyms, query playground,
relevancy tuning). Distributed as NuGet package `UmageAI.Optimizely.GraphSearchTools`.

## Project Structure

- `src/GraphSearchtools/` - Plugin class library (Razor SDK, multi-target net8.0/net10.0)
- `src/GraphSearchtools.SampleSite/` - Alloy CMS 12 demo site
- `src/GraphSearchtools.SampleSiteCms13/` - Alloy CMS 13 demo site
- `src/GraphSearchtools.Tests/` - xUnit tests, multi-target
- `docs/personas.md` - who we're designing for; read before UI/UX work
- `docs/design-system.md` - shared UI patterns + components; read before UI work
- `docs/research/` - Optimizely Graph reference docs (capabilities, relevancy, auth)

## Tech Stack

- .NET 8 (CMS 12) / .NET 10 (CMS 13) — single multi-target package
- UI: Vanilla JS + CSS only, no build step
- Razor class library for embedded views and static assets

## Build & Run

```bash
dotnet build                                                # Build solution
dotnet run --project src/GraphSearchtools.SampleSite        # Run CMS 12 demo
dotnet run --project src/GraphSearchtools.SampleSiteCms13   # Run CMS 13 demo
dotnet test                                                 # Run tests on both TFMs
```

Pre-release local verification: `dotnet test` (no `--no-build`).

### Iterating on UI assets against a running SampleSite

EPiServer serves module assets from the **consumer's content root** (e.g.
`src/GraphSearchtools.SampleSite/modules/_protected/GraphSearchtools/ClientResources/`),
not from `bin/`. The NuGet `.targets` seeds that location on first build
and *doesn't* refresh on subsequent edits to the addon's source.

When live-editing JS/CSS while a SampleSite process is running, mirror
your changes from
`src/GraphSearchtools/modules/_protected/GraphSearchtools/ClientResources/`
into the SampleSite's content-root copy. Browser refresh picks them up
without a server restart. Don't bother copying to `bin/...` — that path
is unused for static file serving.

## Release & distribution

- Pushing a tag matching `v*` triggers `.github/workflows/publish.yml`, which builds, tests
  (both TFMs), packs, and attaches the `.nupkg` to a GitHub release.
- Distribution channel is the Optimizely NuGet feed, which pulls from the GitHub release.
  Do not push to nuget.org directly.

## Key Patterns (inherited from EditorPowertools framework)

- **Registration**: `services.AddGraphSearchtools(...)` + `app.UseGraphSearchtools()` +
  `endpoints.MapGraphSearchtools()`.
- **Options**: `GraphSearchtoolsOptions` bound from `CodeArt:GraphSearchtools` config section.
- **Permissions**: Three-layer (feature toggles + auth policy `codeart:graphsearchtools` +
  optional EPiServer `PermissionType` per tool).
- **Tool structure**: Each tool in `Tools/{ToolName}/` with Service + ApiController + view.
- **Menu**: `GraphSearchtoolsMenuProvider` uses `Paths.ToResource()` for controller routes.
- **Static files**: Go in `modules/_protected/GraphSearchtools/ClientResources/`,
  referenced via `Paths.ToClientResource()`.
- **CMS Shell integration**: Layout uses `@ClientResources.RenderResources("ShellCore")`,
  `@Html.CreatePlatformNavigationMenu()`, `@Html.ApplyPlatformNavigation()` (CMS 12);
  `<platform-navigation>` / `<platform-navigation-wrapper>` on CMS 13.
- **Data**: DynamicDataStore for persistence.
- **JS namespace**: `GST.*` / `window.GST_STRINGS`. CSS prefix `gst-`, vars `--gst-*`.

## Conventions

- No static state — everything via DI.
- Nullable reference types enabled.
- Controllers return JSON APIs; UI is vanilla JS, not server-rendered.
- Each tool has a corresponding PermissionType and FeatureToggle.
- **JS paths**: Never hardcode API paths. Use `window.GST_BASE_URL + '/endpoint'`.
- **Security**: All controllers must have `[Authorize(Policy = "codeart:graphsearchtools")]`,
  all actions must call `_accessChecker.HasAccess()`, POST/PUT/DELETE endpoints must have
  `[RequireAjax]`, error responses must not expose `ex.Message`.
- **Design system**: Shared visual patterns live in `docs/design-system.md`. Before adding a
  new shared partial / JS helper / `.gst-*` class, check the catalogue. **No new shared
  component without an entry; no entry change without user confirm.** Local one-off styles
  inside a single tool are fine.

## Localization

All user-facing strings MUST go through Optimizely's localization system — never hardcode
display text in C# or JS.

- **Language files**: `src/GraphSearchtools/lang/*.xml` (11 languages: en, da, sv, no, de,
  fi, fr, es, nl, ja, zh-CN). English is the base; other 10 start as `TODO_TRANSLATE`
  placeholders that fall back to English text.
- **String path convention**: `/graphsearchtools/{area}/{key}` —
  e.g. `/graphsearchtools/menu/overview`, `/graphsearchtools/about/version`.
- **In services/controllers**: Inject `LocalizationService` and call
  `_localization.GetString("/graphsearchtools/path/key")`.
- **In JS**: Read from `window.GST_STRINGS.{section}.{key}`, populated by `UiStringsProvider`.

# Multi-targeting: CMS 12 (.NET 8) + CMS 13 (.NET 10)

Same tiering rules as EditorPowertools:

| Situation | Action |
|---|---|
| 1–10 lines differ in one place | Tier 1 `#if OPTIMIZELY_CMS13` |
| A whole file is >30% `#if` blocks | Tier 2 (TFM-specific files in `Cms12/` or `Cms13/`) |
| A subsystem changed architecture between CMS 12 and 13 | Tier 3 (internal interface in `Abstractions/`) |
| A dependency only exists in one version | Conditional `<PackageReference>` in csproj |

Compile symbols: `OPTIMIZELY_CMS12` (net8.0), `OPTIMIZELY_CMS13` (net10.0).
