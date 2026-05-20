# Quickstart

End-to-end: install the package, register a **search channel**, and watch a
telemetry event land in the **Insights** tool. About 10 minutes from a green
CMS host.

You will need:

- An Optimizely CMS 12 (`net8.0`) or CMS 13 (`net10.0`) host project that
  already boots.
- Optimizely Graph credentials (`AppKey`, `Secret`, `SingleKey`). If you
  don't have these yet, follow Optimizely's
  [Graph install guide](https://docs.developers.optimizely.com/content-management-system/docs/install-and-configure-optimizely-graph-on-your-site).

## 1. Install the package

```bash
dotnet add package UmageAI.Optimizely.GraphSearchTools
```

## 2. Configure Graph and roles

In `appsettings.json`:

```json
{
  "$schema": "https://raw.githubusercontent.com/umage-ai/Umage.Optimizely.GraphSearchtools/main/src/GraphSearchtools/schemas/graphsearchtools.schema.json",
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
      "AuthorizedRoles": ["WebAdmins", "Administrators"]
    }
  }
}
```

The `$schema` line gives VS Code (and AI agents reading the config)
autocomplete and validation for everything under `UmageAI:GraphSearchTools`
and `Optimizely:ContentGraph`. It is optional — the addon does not read it.
The same schema file is packed inside the `.nupkg` at `schemas/` for
offline reference.

The add-on reuses the host's `Optimizely:ContentGraph` block by default.
Override it per environment under `UmageAI:GraphSearchTools:Graph` if you
need a separate set of credentials for the admin tools.

## 3. Wire it up

```csharp
// Startup.cs / Program.cs
services.AddGraphSearchtools(o =>
        Configuration.GetSection("UmageAI:GraphSearchTools").Bind(o))
    .AddSearchChannel("site-search", c => c
        .DisplayName("Site search")
        .Locales("en")
        .SearchedFields("Name", "MetaDescription", "MainBody")
        .UsesPinnedKey("site-{locale}"));

// in Configure(...):
app.UseGraphSearchtools();
app.UseEndpoints(endpoints =>
{
    endpoints.MapGraphSearchtools();
    // your other endpoint mappings
});
```

The fluent channel builder is documented in
[`SearchChannelBuilder.cs`](../../src/GraphSearchtools/Configuration/SearchChannelBuilder.cs).
The minimal shape above is enough for the rest of this guide; richer
configuration (semantic blend, multi-site scoping, GraphQL document
binding, dynamic pinned-key formulas) is covered there.

## 4. Verify the CMS shell integration

Sign in to `/EPiServer` as a user in one of the `AuthorizedRoles`. A
**GraphSearchtools** node should appear in the platform navigation with
**Overview**, **Channels**, **Pinned**, **Synonyms**, **Insights**, and
**Telemetry** (health) underneath.

Open **Channels** → the channel you just registered. The detail page
confirms the key, the locales, and the searched fields the add-on knows
about.

## 5. Send a telemetry event

The add-on exposes a public ingest endpoint that accepts search and click
events from anywhere — browser, server, smoke test. It is rate-limited
(20 requests/sec per IP, 4 KB body cap by default) and requires no
authentication. Post one event to prove the pipeline is live:

```bash
curl -X POST http://localhost:5000/api/telemetry/searchlog \
  -H "Content-Type: application/json" \
  -d '{
    "kind": "search",
    "phrase": "hello world",
    "channelKey": "site-search",
    "locale": "en",
    "resultCount": 3
  }'
```

A `204 No Content` means the event was queued. The local sink flushes to
DDS every 60 seconds; within a minute the phrase appears under
**Insights → Top phrases**. Send a click event for the same phrase to
populate CTR:

```bash
curl -X POST http://localhost:5000/api/telemetry/searchlog \
  -H "Content-Type: application/json" \
  -d '{
    "kind": "click",
    "phrase": "hello world",
    "channelKey": "site-search",
    "locale": "en",
    "rank": 1
  }'
```

If you see no data after a minute, check the **Telemetry** health page —
it surfaces queue depth, drop counts, and sink configuration.

## 6. Verify the install programmatically

For agents and CI smoke tests, the addon exposes a JSON health endpoint
that returns the same diagnostics the addon logs at boot:

```bash
curl -u admin:... http://localhost:5000/EPiServer/cms/graphsearchtools/Overview/Health
```

```json
{
  "version": "0.2.5",
  "status": "ok",
  "diagnostics": [],
  "channels": [
    { "key": "site-search", "displayName": "Site search", "locales": ["en"], "...": "..." }
  ],
  "credentials": {
    "source": "Optimizely:ContentGraph (host config)",
    "gatewayAddress": "https://cg.optimizely.com",
    "appKeyConfigured": true,
    "secretConfigured": true,
    "singleKeyConfigured": true
  },
  "features": { "Overview": true, "...": "..." },
  "telemetry": { "sink": "local", "queueDepth": 0, "queueCapacity": 65536, "dropped": 0 }
}
```

The endpoint is behind the same `umageai:graphsearchtools` policy as the
rest of the admin tools — auth as a user in one of the `AuthorizedRoles`.
Use `status` (`ok` / `warnings` / `misconfigured`) and `diagnostics[]` as
the source of truth when verifying a fresh install; don't infer health
from the absence of a 404. The addon also writes the same findings to
`ILogger` at boot under the `GraphSearchtoolsStartupValidator` category.

## You now have

- A registered search channel marketers can curate against.
- A working telemetry pipeline with read-side aggregates (**Insights**)
  and live forensics (**SearchLogs**).
- Three diagnostic surfaces: **Overview** (smoke), **Telemetry** (health),
  **Channels** (binding).

## What's next

Pick the integration pattern that matches your host:

- [Server-rendered ASP.NET](pattern-server-rendered.md) — Razor pages /
  MVC controllers that render search results on the server.
- [Headless deployment](pattern-headless.md) — SPA or external SSR
  frontend that queries Optimizely Graph directly.
- [Forwarding telemetry to a 3rd-party tool](pattern-third-party-telemetry.md)
  — fan out events to Application Insights, GA, Matomo, Mixpanel, etc.

Pinned-result curation, synonym pools, and relevancy tuning are thin UIs
over Optimizely Graph's native features. The behaviour your marketers
observe in the addon's tools mirrors what Optimizely's own
[Graph documentation](https://docs.developers.optimizely.com/) describes
for the underlying APIs — read it alongside this guide.
