# Optimizely Graph Authentication — CMS Integration Reference

A practical reference for how Optimizely Graph authenticates traffic, how an
Optimizely CMS host wires those credentials, and what choices apply to an
add-on like `Umage.Optimizely.GraphSearchtools` that talks to Graph on
behalf of editors.

Captured 2026-05-01. Replaces the implicit "AppKey + Secret + SingleKey, that's
it" assumption in earlier docs with the full picture, including HMAC, JWT, and
the unchanged credential model under the new CMS 13 Query SDK.

---

## 1. The credential model

A Graph instance issues a **fixed triple** of credentials per project per
environment, plus optional JWT support layered on top. There is no per-user
key surface, no scoped token issuance API, and no separate indexer key.

| Credential | Type | Scope | Surface |
|---|---|---|---|
| `AppKey` | Public half of an HMAC pair, doubles as Basic-auth username | Full admin + read + write across all RBAC roles | Authoring, indexing, admin endpoints |
| `Secret` | Secret half of the HMAC pair, doubles as Basic-auth password | Same as AppKey, never leaves the server | Same |
| `SingleKey` | Static public read key | Read-only, **only** content marked published + RBAC-readable by `r:Everyone` | Public site queries |
| OIDC JWT (optional) | Bearer token from OptiID or a generic OIDC provider | RBAC-filtered per `cg-username` / `cg-roles` claims | Authenticated frontends, BFF proxies |

The first three are what `Optimizely:ContentGraph` carries. JWT is configured
separately at the GraphClient level and is the path Optimizely steers
authenticated frontends toward.

The pair `AppKey:Secret` is the same secret used in two ways: **Basic** auth
sends `base64(AppKey:Secret)` in the `Authorization` header; **HMAC** auth
signs each request with the same pair. Both modes accept the same key
material.

## 2. Where keys come from

- **Optimizely DXP customers (PaaS or SaaS)** — the triple is provisioned
  with the project. PaaS customers retrieve it from the **PaaS Portal**;
  SaaS from the dashboard. DXP-deployed sites get the configuration injected
  by the platform — Optimizely's docs explicitly say *not* to commit the
  `Optimizely:ContentGraph` block when deploying through PaaS.
- **Self-hosted / non-DXP** — keys are issued by **Optimizely Support** on
  request. There is no CLI or self-service issuance flow.
- **Per environment** — production, preproduction, integration, and local
  dev each have their own triple. Keys are not shared across environments
  by design.

## 3. Authentication on the wire

Three authentication schemes work against Graph today; pick by endpoint and
caller.

### 3.1 Basic — `Authorization: Basic base64(AppKey:Secret)`

Default for admin REST and the indexer. Simple to implement, easy to
debug. Sent verbatim by the seed `GraphSearchManagementService` and by every
admin endpoint in the wider community ecosystem (e.g. `OptiGraphExtensions`).
The synonyms PUT endpoint takes a `text/plain` body, not JSON — the body **is**
the rule blob. This is a contract, not a quirk.

### 3.2 HMAC — `Authorization: epi-hmac {appKey}:{timestamp}:{nonce}:{signature}`

Same key material, signed per-request. Optimizely's public docs name the
header format but do not publish the canonical signing string or hash
algorithm — implementers either use Optimizely's SDK (which does the signing
internally) or fall back to Basic. In practice, Basic is dominant in the
community ecosystem.

### 3.3 SingleKey — `?auth={SingleKey}` or `Authorization: epi-single {key}`

Read-only, intended for browser code. Returns only **published** content
that is **readable by `r:Everyone`**; drafts, previous versions, expired
items, and restricted content are silently filtered out. This is Graph's
"anon key" — analogous to Supabase's anon key or Firebase's web API key.

### 3.4 OIDC JWT (CMS 13 Query SDK and onward)

Authenticated browser sessions can call Graph as the logged-in user by
presenting a Bearer JWT. Graph filters results by RBAC using the
`cg-username` and `cg-roles` claims. Configured via the new
`AddGraphContentClient(...).WithDisplayFilters()` registration in CMS 13
(see §6); the credential model under the hood is unchanged.

## 4. CMS-side wiring (host indexes content into Graph)

The CMS pushes content into Graph via the `Optimizely.ContentGraph.Cms`
NuGet package.

```csharp
// Startup.cs — Configure
services.AddContentGraph(o => Configuration.GetSection("Optimizely:ContentGraph").Bind(o));
// CMS 13 ordering: AddContentGraph() must come BEFORE AddContentManager().
```

```jsonc
// appsettings.json
{
  "Optimizely": {
    "ContentGraph": {
      "GatewayAddress": "https://cg.optimizely.com",
      "AppKey":         "<env-specific>",
      "Secret":         "<env-specific>",
      "SingleKey":      "<env-specific>",

      // Optional, but commonly set:
      "AllowSendingLog":            true,
      "ContentVersionSyncMode":     "AllVersions",
      "SyncReferencingContents":    true,
      "EnablePreviewTokens":        true,
      "BufferedIndexingGracePeriod": "00:00:30",
      "MaxBatchSize":               50,
      "ExtractMedia":               true,
      "PreventFieldCollision":      true
    }
  }
}
```

The CMS indexer authenticates with **the same `AppKey + Secret`** the admin
APIs use. Two sync paths run side-by-side:

- **Event-driven** — `SavedContent` / `PublishedContent` / `MovedContent` /
  `DeletedContent` / `ExpiredContent` hooks push delta updates immediately.
- **Scheduled** — a CMS scheduled job named **"Optimizely Graph Content
  synchronization"** (visible in Admin → Scheduled Jobs) runs delta plus
  full reindex on demand.

There is no separate "indexer credential" — if the AppKey/Secret rotate, the
scheduled job and event hooks both stop working until config is updated.

## 5. Public/private split

- **`SingleKey` is intended to be exposable to browser JavaScript.** Embedding
  it in a published bundle is the documented usage. It cannot escalate
  to anything beyond public content reads.
- **`AppKey + Secret` must NEVER reach the browser.** Anyone with the pair
  can read drafts, write to admin APIs, mutate pinned results, replace the
  synonym blob, and register/destroy webhooks. Treat the pair like a database
  password.
- Caveat for `SingleKey`: it grants schema introspection at
  `https://cg.optimizely.com/app/graphiql?auth={SingleKey}`, so the schema
  itself is effectively public for any project that ships SingleKey to a
  browser. Teams that consider their schema sensitive proxy queries through
  a BFF instead.

## 6. CMS 13 Query SDK changes

Optimizely's March 2026 announcement introduces `Optimizely.Graph.Cms.Query`,
registered via `services.AddGraphContentClient(...)`. This is a **client-side
wrapper over the same REST/GraphQL surface** — the credential model, config
section, and key types are unchanged.

What's new:

- **Default mode** uses `SingleKey` and returns public content only.
- **`WithDisplayFilters()`** auto-detects `Thread.CurrentPrincipal` and
  switches to Basic auth + `AsUser(...)` headers (`cg-username`,
  `cg-roles`) for authenticated requests, so authenticated users can see
  drafts and restricted content.
- **`AsUser(principal)` / `WithAuth(options)`** for explicit control.

No OAuth client credentials, no managed identity, no new config keys. CMS 13
add-ons that auth via `AppKey:Secret` Basic continue to work unchanged.

## 7. Webhooks and outbound auth

Graph's *registration* surface for webhooks — `POST {gateway}/api/webhooks` —
accepts Basic or HMAC auth, identical to other admin endpoints. Graph's
webhook *delivery* signature contract is **not publicly documented**. Hosts
that accept webhooks from Graph today rely either on:

1. Network-level controls (allow-listing the gateway egress IPs), or
2. A shared secret embedded in the webhook URL itself (so a callback URL
   like `/webhooks/graph/{shared-secret}` doubles as an auth token).

Do not assume Graph signs outbound webhook deliveries.

## 8. Rotation, multi-environment, isolation

- **Rotation**: documented guidance is "rotate `SingleKey` regularly and
  treat keys as sensitive." There is no documented rotation API for
  `AppKey:Secret` — rotation requires a Support ticket.
- **What breaks on rotation**: the CMS indexer scheduled job, event-driven
  push, every add-on that caches the credentials in an `HttpClient`
  pipeline (Pinned, Synonyms, Search Console, Connectivity), and any
  frontend bundle embedding the old `SingleKey`. There is no documented
  graceful overlap window or dual-key support.
- **Multi-environment**: each CMS environment has its own Graph instance
  and its own triple. Local dev typically uses a separate "developer
  sandbox" tenant. Mixing prod credentials with a non-prod gateway (or
  vice-versa) returns 401 on every call.

## 9. What GraphSearchtools uses today

The current implementation maps onto the credential model as follows.

| Tool | Endpoint | Auth |
|---|---|---|
| Pinned (collections + items) | `{gateway}/api/pinned/...` | Basic (`AppKey:Secret`) |
| Synonyms | `{gateway}/resources/synonyms?...` | Basic (`AppKey:Secret`), `text/plain` body for PUT |
| Search Console | `{gateway}/content/v2?auth={SingleKey}` | Query-string SingleKey |
| Autocomplete Tester | `{gateway}/content/v2?auth={SingleKey}` | Query-string SingleKey |
| Connectivity Tester | All four probes | Mixed: Basic for admin, SingleKey for the content query |
| Saved Queries | DDS-local, no Graph traffic | n/a |

Credentials flow through `IGraphCredentialsResolver` (default
`GraphCredentialsResolver`), which prefers `UmageAI:GraphSearchTools:Graph`
when populated and falls back per-field to the host's
`Optimizely:ContentGraph` section. Two consequences:

- **Sites already wired for Graph need no extra config** — the addon picks
  up `AppKey`/`Secret`/`SingleKey` from the host automatically.
- **Per-tool key override is not supported.** All tools share the same
  triple; we don't currently surface a separate read-only key for editor
  search-console preview vs admin write paths. If a customer wants to
  restrict editors to a SingleKey-only experience, the right move is to
  toggle the admin-touching tools (Pinned, Synonyms) off via feature flags
  rather than to add a second credential.

The Connectivity Tester is the canonical place to surface a credential
problem — its four probes tell support exactly which key is broken without
exposing the key itself.

## 10. Common failure modes

| Symptom | Likely cause |
|---|---|
| 401 on Pinned/Synonyms admin calls | Wrong AppKey/Secret for this environment, or whitespace/newline in the base64 of `AppKey:Secret`. Mixing prod creds with a preprod gateway. |
| 401 on Search Console / Autocomplete | `SingleKey` is wrong, expired, or rotated; frontend bundle has stale key while GraphiQL works (key already rotated). |
| Empty results from `SingleKey` queries that return content via Basic | Content is unpublished, expired, soft-deleted, or RBAC-restricted; SingleKey filters all four silently. |
| Every call 404s or times out | `GatewayAddress` mismatch (e.g. regional gateway not used) or DNS/firewall blocking egress. |
| Indexer job logs "Stop of job was called" without a manual stop | Known Graph release bug, fixed in a 2025 update — confirm the host's `Optimizely.ContentGraph.Cms` package is current. |
| Webhooks deliver but don't verify | Graph does not publicly document outbound signing; rely on URL-embedded shared secret or network controls. |

## 11. Risks & mitigations for this addon

| Risk | Mitigation |
|---|---|
| Editor accidentally sees a misleading "all green" status when only `SingleKey` works | Connectivity Tester reports four independent probes (Gateway / Admin auth / Content query / Index population) and rolls them up to the worst status, not an average. |
| Operator commits `AppKey:Secret` to source control via `appsettings.Development.json` | Sample sites ship with empty values and a README pointer to `dotnet user-secrets`. The implementation plan calls this out explicitly. |
| Add-on hits Graph with the credentials of a parallel CMS instance after a config push | `IGraphCredentialsResolver` resolves at request time, not at startup; no in-memory caching of decoded credentials. |
| Key rotation breaks running tools without warning | Connectivity Tester is the recommended first-look; Pinned/Synonyms surfaces the upstream HTTP status code (501/503) rather than swallowing the error. |
| Frontend code accidentally embeds `AppKey:Secret` instead of `SingleKey` | Only `SingleKey` is surfaced to the browser by the layout (`window.GST_BASE_URL` and `window.GST_STRINGS` only); admin credentials never cross the controller boundary. |

---

## 12. Sources

- [Optimizely Graph Authentication overview](https://docs.developers.optimizely.com/platform-optimizely/docs/authentication)
- [Basic auth](https://docs.developers.optimizely.com/platform-optimizely/docs/basic-auth) /
  [Basic auth from backend (config block)](https://docs.developers.optimizely.com/platform-optimizely/docs/basic-auth-from-backend)
- [HMAC auth](https://docs.developers.optimizely.com/platform-optimizely/docs/hmac-auth)
- [Single Key auth](https://docs.developers.optimizely.com/platform-optimizely/docs/api-single-key-auth)
- [Configure CMS 12 to send content](https://docs.developers.optimizely.com/platform-optimizely/docs/configure-cms-12-to-send-content)
- [Install and configure Optimizely Graph on your CMS site](https://docs.developers.optimizely.com/content-management-system/docs/install-and-configure-optimizely-graph-on-your-site)
- [Manage webhooks](https://docs.developers.optimizely.com/platform-optimizely/docs/manage-webhooks)
- [Introducing Optimizely CMS 13 Graph SDK — Jake Minard, March 2026](https://world.optimizely.com/blogs/jake-minard/dates/2026/3/introducing-optimizely-cms-13-graph-sdk/)
- [Optimizely Graph best practices — Jon Williams, January 2026](https://world.optimizely.com/blogs/jon-williams/dates/2026/1/optimizely-graph-best-practices---security-access-control-and-performance-optimisation/)
- [Cleaning up Content Graph webhooks — Minesh Shah, December 2025](https://world.optimizely.com/blogs/Minesh-Shah/Dates/2025/12/cleaning-up-content-graph-webhooks-in-paas-cms-scheduled-job/)
- [Authentication modes in Optimizely Graph — Kunal Shetye](https://kunalshetye.com/posts/optimizely-graph-using-appkey-appsecret/)
- [OptiGraphExtensions — `SynonymGraphSyncService.cs`](https://github.com/adayinthelifeofapro/OptiGraphExtensions/blob/master/src/OptiGraphExtensions/Features/Synonyms/Services/SynonymGraphSyncService.cs) — canonical Basic-auth admin-write pattern.
- [2025 Optimizely Graph release notes](https://support.optimizely.com/hc/en-us/articles/25432213670413-2025-Optimizely-Graph-release-notes)
