# Public API Surface (Pre-1.0)

The list of types this addon publishes as `public`, with a one-line rationale
per group. **Pre-1.0 NuGet — public types are contracts we'll honour.** Default
visibility is `internal`. Adding a new `public` type without an entry here is a
review-blocker; removing one is a breaking change.

The plan is: stake out this contract first, then sweep everything not listed
here to `internal` in a follow-up PR. The agent survey behind the sweep (see
the PR description for the visibility PR) found 113 public types; this doc
keeps ~51 of them public and demotes the rest.

---

## Tier 1: Required for every integrator

This is what every consuming `Startup.cs` calls. Stable; we should treat any
change here as an SDK bump.

```csharp
services.AddGraphSearchtools(options => { … })          // Infrastructure
    .AddSearchChannel("alloy-search", c => c             // Configuration
        .DisplayName("/graphsearchtools/channels/site/name")
        .SearchedFields("Name", "MainBody")
        .SemanticBlend(0.3, GraphRanking.Semantic)
        .UsesPinnedKey("alloy-{locale}"));

endpoints.MapGraphSearchtools();                         // Infrastructure
```

| Type | File | Why public |
|---|---|---|
| `ServiceCollectionExtensions` (static) | `Infrastructure/` | Registration entry point |
| `ApplicationBuilderExtensions` (static) | `Infrastructure/` | Holds `MapGraphSearchtools()` |
| `IGraphSearchtoolsBuilder` | `Configuration/` | Return type of `AddGraphSearchtools`; chain target for `AddSearchChannel` |
| `GraphSearchtoolsOptions` + nested `SavedQueriesOptions`, `GraphConnectionOptions` | `Configuration/` | Bound from `UmageAI:GraphSearchTools` config section |
| `FeatureToggles` | `Configuration/` | Nested in `GraphSearchtoolsOptions.Features` |
| `LocalTelemetryOptions` | `Configuration/` | Nested in `GraphSearchtoolsOptions.Telemetry` |
| `SearchChannelRegistrationExtensions` (static) | `Configuration/` | The `AddSearchChannel` extension |
| `SearchChannelBuilder` | `Configuration/` | Fluent builder body |
| `SearchChannel` | `Configuration/` | Built object — exposed on `IGraphSearchtoolsBuilder.Channels` |
| `LocalizedString` | `Configuration/` | Implicit `string` conversion drives the fluent builder |
| `GraphRanking` (enum) | `Configuration/` | Passed as a value in `.SemanticBlend(weight, GraphRanking.Semantic)` |
| `ITelemetrySink` + `SearchEvent`, `ClickEvent` | `Abstractions/` | The hot-path write seam; host SDKs call `Record()` per page render |

---

## Tier 2: Extension points (opt-in)

Integrators that don't use these never see them. Public because the contract
must be substitutable.

| Type | Used to… |
|---|---|
| `UseExternalTelemetryReader<TReader>` (extension) | Swap the local DDS-backed telemetry pipeline for a customer adapter (App Insights, Mixpanel, Matomo). |
| `ITelemetryReader` | The interface the customer adapter implements. |
| `TelemetryQuery`, `PhraseAggregate`, `DailyAggregate`, `RawEvent` | The query and result shapes the reader must speak. |
| `IGraphAdminClient` | Override the default REST/GraphQL admin client (rare — only relevant for hosts proxying Graph through a different transport). |
| `IGraphCredentialsResolver` + `GraphCredentials` | Override how Graph credentials are resolved (e.g. from a secrets vault instead of `IConfiguration`). |
| `ISearchChannelRegistry` | Enumerate registered channels at runtime from host code (e.g. a custom diagnostics page). |
| `RequireAjaxAttribute` | Available if a host wants to apply the same anti-CSRF rule to its own controllers. |

---

## Tier 3: Programmatic data seeding (rare)

These are public only because the SampleSite's `ScreenshotSeedController` uses
them to populate demo pinned items and synonyms via the admin client. Real
integrators rarely touch these; most operations happen through the admin UI.

| Type | Notes |
|---|---|
| `PinnedItemPayload`, `PinnedCollectionPayload`, `PinnedCollectionResult`, `PinnedItemResult`, `PinnedCollectionUpdatePayload` | Inputs/outputs of `IGraphAdminClient` pinned methods |
| `SynonymsQuery`, `SynonymsRequest`, `SynonymsResponse` | Inputs/outputs of `IGraphAdminClient` synonym methods |
| `ContentSearchHit`, `SiteInfo` | Returned by `IGraphAdminClient.SearchContentAsync` / `ResolveByGuidsAsync` and `LanguageSiteEnumerator` |
| `GraphSearchApiException` | Thrown by `IGraphAdminClient` when the upstream returns non-2xx |

If we eventually decide programmatic seeding is not a supported scenario,
these can all collapse to `internal`. For now they stay public to keep the
SampleSite honest.

---

## Tier 4: Framework reflection requires public

These are public because Optimizely / ASP.NET / EPiServer's reflection finds
them by visibility, not because integrators write code against the type names.
Don't internalise without checking the framework's discovery rules.

| Group | Discovery mechanism |
|---|---|
| `GraphSearchtoolsMenuProvider` | EPiServer `IMenuProvider` discovery |
| `GraphSearchtoolsPermissions` (static) | EPiServer `[PermissionTypes]` discovery |
| `TelemetryRetentionJob` | EPiServer `[ScheduledPlugIn]` discovery |
| `PermissionSeeder`, `GraphSearchtoolsStartupValidator`, `BucketFlusher` | `IHostedService` resolution |
| `AuditLogEntry`, `SearchLogBucket`, `SearchLogRing`, `UserPreferencesRecord`, `PermissionSeederMarker` | DDS `IDynamicData` reflection-based instantiation |

**Controllers are NOT in this list.** All `*Controller` / `*ApiController` types
are `internal sealed` and discovered by `InternalControllerFeatureProvider`
(`Infrastructure/InternalControllerFeatureProvider.cs`), which extends MVC's
default `ControllerFeatureProvider` to include internal controllers from this
assembly. The provider is registered inside `AddGraphSearchtools` via
`services.AddControllers().ConfigureApplicationPartManager(...)`. Keeping
controllers internal is what lets the per-tool services, view-models, and DTOs
they reference also stay internal (CS0051 would otherwise force the entire
transitive closure of controller parameter and return types to be public).
If you add a new controller, the provider picks it up automatically — no extra
registration step.

---

## Judgment calls (resolved)

The agent survey flagged ten types as judgment calls. Decisions:

| Type | Decision | Why |
|---|---|---|
| `IGraphSearchtoolsBuilder` (interface, one impl) | **Keep public** | The fluent-chain target every integrator names. The cost of the interface is paid once; the cost of replacing it later is paid by every consumer. |
| `ISearchChannelRegistry` (interface, one impl) | **Keep public** | Host code may want to enumerate channels for custom diagnostics. Cheap to keep. |
| `LocalizedString` | **Keep public** | Implicit `string` conversion is used in the SampleSite's `AddSearchChannel(...)` call. |
| `GraphCredentials` (record) | **Keep public** | Return type of `IGraphCredentialsResolver.Resolve`. |
| `SearchChannel` | **Keep public** | Exposed on `IGraphSearchtoolsBuilder.Channels`. |
| `ITelemetryMetrics` | **→ internal** | One impl (`LocalTelemetrySink`), one consumer (Health endpoint, same assembly). Integrator-supplied `ITelemetryReader` doesn't need it. Muratori was right — interface theater. |
| `PermissionMap` + nested `Snapshot` | **→ internal** | Razor compiles inside the assembly; no external consumer. |
| `FeatureAccessChecker` | **→ internal** | Used only by addon controllers and `PermissionMap`. |
| `StartupDiagnostics` + `Diagnostic` + `DiagnosticLevel` | **→ internal** | Consumed only as JSON via the Overview health endpoint; integrators scrape JSON, not C# types. |
| `AuditLogDto` | **→ internal** | JSON response type only. |
| `SearchLogPayload` (nested in `TelemetryApiController`) | **→ internal** | MVC model binding works against internal types in modern ASP.NET. |
| `GraphSearchApiException`, `BulkLoadCapExceededException` | **→ internal** | Thrown and caught inside the addon; no SampleSite or test consumer catches by type. |

---

## Internalised in the sweep

These were demoted to `internal` once `InternalControllerFeatureProvider` made
internal controllers viable. `InternalsVisibleTo("GraphSearchtools.Tests")` is
declared in `GraphSearchtools.csproj`, so the test project keeps full access.
Razor views compile inside the addon assembly, so `@inject` / `@model` against
internal types is fine.

**By group:**

| Group | Count | Examples |
|---|---|---|
| `Tools/*/*Service.cs` — per-tool service classes | 8 | `PinnedService`, `SynonymsService`, `ChannelsService`, `InsightsService`, `SearchLogsService`, `SynonymCoverageService`, `PinnedCoverageService`, `QueryRunnerService` |
| `Tools/*/Models/*.cs` — view-models, DTOs, response records | ~22 | `ChannelSummary`, `ChannelDetail`, `ChannelStatus`, `InsightsPhraseRow`, `PinnedCoverageResult`, `SearchLogPhraseRow`, `SynonymCoverageResult`, `RunnerRequest`/`Result`/`Hit`, etc. |
| Concrete impls behind public interfaces | 6 | `UiStringsProvider`, `AuditLogService`, `UserPreferencesService`, `GraphAdminClient`, `GraphCredentialsResolver`, `SearchChannelRegistry` |
| Internal-only helpers, validators, exceptions | ~9 | `TelemetryAbuseGuard`, `LanguageSiteEnumerator`, `CmsLocaleResolver`, `BulkLoadCapExceededException`, `PermissionMap` (+ `Snapshot`), `FeatureAccessChecker`, `StartupDiagnostics` (+ `Diagnostic`, `DiagnosticLevel`), `AuditLogDto` |
| `Services/GraphModels.cs` records not used by SampleSite seeding | ~7 | Anything in `GraphModels.cs` outside the Tier-3 list above |

Net: **public surface drops from ~113 → 45 types** (≈60% reduction).

---

## Adding a new public type

Before marking a new type `public`:

1. Decide which tier it belongs in (1–4).
2. Add the entry to this file in the same PR.
3. If it's in Tier 2, add an example to the SampleSite that exercises it.
4. If it's a concrete behind an interface, **make the concrete `internal`** —
   register both, expose only the interface.
5. If the type only exists because Razor / model binding / JSON serialisation
   needs it, it's almost certainly internal — the assembly boundary is what
   matters, not the compiler's visibility check.

If you can't write the rationale in one line, the type isn't focused enough
to be public yet.
