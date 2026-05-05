using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

public record PinnedCollectionPayload
{
    public string Title { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public record PinnedCollectionUpdatePayload
{
    public string? Title { get; init; }
    public string? Key { get; init; }
    public bool? IsActive { get; init; }
}

public record PinnedCollectionResult
{
    public string Title { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string Id { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public string UpdatedAt { get; init; } = string.Empty;
}

public record PinnedItemPayload
{
    public string Phrases { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public string? Language { get; init; }
    public double? Priority { get; init; }
    public bool? IsActive { get; init; }
}

public record PinnedItemResult
{
    public string Phrases { get; init; } = string.Empty;
    public string TargetKey { get; init; } = string.Empty;
    public string? Language { get; init; }
    public double Priority { get; init; }
    public bool IsActive { get; init; }
    public string Id { get; init; } = string.Empty;
    public string CollectionId { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Optional expiry stamp — when set, Graph stops applying the pin past this
    /// moment. Surfaced for the Phase 4 Pinned Result Coverage audit, which
    /// flags pins whose <see cref="EffectiveTo"/> is in the past so editors
    /// can prune stale rows. Null when the upstream API doesn't provide one
    /// (the field is best-effort: not every Graph build returns it, in which
    /// case the audit simply won't surface "Expired" issues for that tenant).
    /// </summary>
    public DateTime? EffectiveTo { get; init; }
}

public record SynonymsQuery
{
    [JsonPropertyName("language_routing")]
    public string? LanguageRouting { get; init; }

    [JsonPropertyName("source_routing")]
    public string? SourceRouting { get; init; }

    [JsonPropertyName("synonym_slot")]
    public string? Slot { get; init; }
}

public record SynonymsRequest
{
    public string Content { get; init; } = string.Empty;
    public string? LanguageRouting { get; init; }
    public string? SourceRouting { get; init; }
    public string? Slot { get; init; }
}

public record SynonymsResponse
{
    public string Content { get; init; } = string.Empty;
}

public record SiteInfo
{
    public string Title { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string CollectionKey { get; init; } = string.Empty;
}

public record ContentSearchHit
{
    public string Name { get; init; } = string.Empty;
    public string ContentGuid { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────────
// Webhooks (Phase 3) — `POST {gateway}/api/webhooks`, Basic auth.
//
// The on-the-wire shape is a small `{ request: { url, method, headers } }`
// object: every doc'd Optimizely Graph webhook example wraps the delivery
// metadata under `request` so the tenant can express the outbound HTTP shape
// (URL + method + headers) without conflating it with the registration's own
// metadata (id, createdAt, disabled). Filters are not always populated on a
// fresh tenant; we surface them as opaque-string when present.
//
// TODO: verify against the live Optimizely Graph webhook admin API. Public
// docs link: https://docs.developers.optimizely.com/platform-optimizely/docs/manage-webhooks
// — the doc page shows the registration body but doesn't fully spec the
// response. Real-world hosts can adjust the DTOs if Graph rejects.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Outbound HTTP shape of a webhook delivery — what the customer's endpoint
/// will see when Graph fires. Modelled as a record so it round-trips cleanly
/// through both <see cref="WebhookPayload"/> (request body to Graph) and
/// <see cref="WebhookResult"/> (response body from Graph).
/// </summary>
public record WebhookRequestShape
{
    public string Url { get; init; } = string.Empty;
    public string? Method { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Body POSTed to <c>/api/webhooks</c> when registering a new webhook.
/// </summary>
public record WebhookPayload
{
    [JsonPropertyName("request")]
    public WebhookRequestShape Request { get; init; } = new();

    /// <summary>
    /// Optional event filter. Graph's webhook filter language is documented
    /// loosely; we pass the raw object through so editors can paste in a
    /// JSON expression and have it forwarded verbatim.
    /// </summary>
    [JsonPropertyName("filters")]
    public object? Filters { get; init; }
}

/// <summary>
/// Webhook record returned by the admin API. The Graph response carries a
/// generated <c>id</c> plus echoes the registration body and may flag the
/// hook as disabled (e.g. after repeated 5xx deliveries).
/// </summary>
public record WebhookResult
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("request")]
    public WebhookRequestShape Request { get; init; } = new();

    /// <summary>
    /// Raw JSON token of whatever filter the tenant registered. Kept as
    /// <see cref="object"/> so we don't lock the API down to one filter shape
    /// (Graph accepts both flat key-value maps and nested expressions).
    /// </summary>
    [JsonPropertyName("filters")]
    public object? Filters { get; init; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────────
// Custom Data Sources (Phase 3) — `GET {gateway}/api/datasources`,
// `POST {gateway}/api/datasources/{name}/sync`. Basic auth, same shape as
// pinned/synonyms/webhooks.
//
// TODO: verify against Optimizely Graph data-sources API docs. The endpoint
// shape and field names below mirror the conventions used elsewhere in the
// admin surface and the hints in research/optimizely-graph-site-search.md.
// Real-world hosts can adjust the DTOs if Graph rejects.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Read-only projection of a registered data source. The Custom Data Sources
/// tool only displays + triggers resync, so the DTO carries no editable
/// fields. <see cref="Status"/> is left as a free-form string — Graph's set
/// of values isn't fully enumerated in public docs and we'd rather pass an
/// unknown value through than swallow it.
/// </summary>
public record DataSourceResult
{
    // TODO: verify against Optimizely Graph data-sources API docs.
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    // TODO: verify against Optimizely Graph data-sources API docs.
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    // TODO: verify against Optimizely Graph data-sources API docs.
    [JsonPropertyName("itemCount")]
    public int? ItemCount { get; init; }

    // TODO: verify against Optimizely Graph data-sources API docs.
    [JsonPropertyName("lastSyncedAt")]
    public DateTime? LastSyncedAt { get; init; }

    // TODO: verify against Optimizely Graph data-sources API docs.
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────────
// Request Logs (Phase 3) — `GET {gateway}/api/requestlogs?take={take}`,
// Basic auth (AppKey:Secret) like pinned/synonyms/webhooks.
//
// TODO: verify against the live Optimizely Graph admin API. There is no
// publicly-documented schema for the request-log endpoint; the field names
// below mirror the conventions used by Graph's other admin surfaces (see
// `docs/research/graph-authentication.md` §7) and the dashboard fields visible
// in the OptiGraphExtensions UI. Production hosts can adjust the property
// names below or the response-envelope probe in the `GraphAdminClient` if the
// live API returns a different shape.
//
// Properties parsed from the GraphQL document body at display time:
//   - `_ranking` mode (RELEVANCE | SEMANTIC | BOOST_ONLY | DOC) is extracted
//     client-side via regex if not surfaced in the response.
//   - `ResultCount` is parsed from the response body when present; else null.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One entry from the Graph request-log endpoint. Field names mirror what
/// Graph's other admin surfaces serialise as (camelCase). Optional fields are
/// nullable so the DTO survives a partial response — the tool's UI deals with
/// missing values explicitly rather than coercing to defaults.
/// </summary>
public record RequestLogEntryResult
{
    /// <summary>Server-assigned id (used as a stable React-style key).</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>UTC timestamp of when Graph processed the query.</summary>
    [JsonPropertyName("at")]
    public DateTime At { get; init; }

    /// <summary>HTTP method (POST for normal queries, GET for persisted).</summary>
    [JsonPropertyName("method")]
    public string Method { get; init; } = "POST";

    /// <summary>Operation name from the GraphQL document, e.g. <c>SiteSearch</c>.</summary>
    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    /// <summary>Raw GraphQL document the caller sent.</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>Variables JSON; preserved as-is so the tool can re-stringify on replay.</summary>
    [JsonPropertyName("variables")]
    public string? Variables { get; init; }

    /// <summary>HTTP status code Graph returned for the request.</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>Wall-clock duration on the gateway, milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }

    /// <summary>Number of items returned. May be absent — parsed best-effort.</summary>
    [JsonPropertyName("resultCount")]
    public int? ResultCount { get; init; }

    /// <summary>Ranking mode extracted from the document, e.g. <c>SEMANTIC</c>.</summary>
    [JsonPropertyName("ranking")]
    public string? Ranking { get; init; }

    /// <summary>Caller IP recorded by the gateway, when available.</summary>
    [JsonPropertyName("callerIp")]
    public string? CallerIp { get; init; }

    /// <summary>User-Agent header from the request, when available.</summary>
    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }
}

// ──────────────────────────────────────────────────────────────────────────────
// Index Inspector (Phase 4) — read-only projection of how the Graph index is
// populated per content type, plus a surfaced count of items missing the basic
// editorial fields (Name / Title). Powers the Phase 4 §6 "Index size by
// content type; missing fields (Name/Title)" surface from
// docs/implementation-plan.md.
//
// Pulled from the content GraphQL endpoint — auth is the SingleKey query
// param, same as Health's index-population probe. The schema may or may not
// expose `Content { types { name count } }`; if it doesn't, the client falls
// back to one query per type listed in
// GraphSearchtoolsOptions.SearchableContentTypes and aggregates the counts.
//
// TODO: verify schema — the `types` field on Content isn't part of the public
// GraphQL surface we have on hand, and the per-type `where: { Name: { exists:
// false } }` filter is best-effort. Real-world hosts can adjust the
// per-type fallback query or the missing-field detection heuristic if Graph's
// schema differs from what we assumed.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One row in the per-content-type index breakdown. <see cref="MissingTeaserCount"/>
/// and <see cref="MissingMainBodyCount"/> are nullable because not every
/// content type carries these fields — when the schema doesn't expose them,
/// the inspector leaves the column empty rather than reporting a misleading 0.
/// </summary>
public record ContentTypeIndexRow
{
    /// <summary>Fully-qualified content-type name as Graph reports it (e.g. <c>StandardPage</c>).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Total items of this type in the index across all locales / versions.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>
    /// Items of this type with no <c>Name</c> field set. Populated when the
    /// per-type missing-fields scan is supported; null when not.
    /// </summary>
    [JsonPropertyName("missingNameCount")]
    public int? MissingNameCount { get; init; }

    /// <summary>
    /// Items of this type with no <c>Teaser</c> / summary text. Optional —
    /// only some content types have a teaser field at all.
    /// </summary>
    [JsonPropertyName("missingTeaserCount")]
    public int? MissingTeaserCount { get; init; }

    /// <summary>
    /// Items of this type with no <c>MainBody</c> field set. Optional —
    /// only some content types carry a main body.
    /// </summary>
    [JsonPropertyName("missingMainBodyCount")]
    public int? MissingMainBodyCount { get; init; }
}

/// <summary>
/// Snapshot of the Graph index population captured at a point in time. The
/// captured-at stamp lets the UI show "captured 30s ago" rather than relying
/// on the request response time.
/// </summary>
public record IndexInspectionResult
{
    /// <summary>Total items across every content type, all locales / versions.</summary>
    [JsonPropertyName("totalItems")]
    public int TotalItems { get; init; }

    /// <summary>Per-content-type breakdown. Sorted by caller; the service returns it count-desc.</summary>
    [JsonPropertyName("perContentType")]
    public List<ContentTypeIndexRow> PerContentType { get; init; } = new();

    /// <summary>
    /// Aggregate count of items missing the <c>Name</c> field across every
    /// content type. Mirrors the sum of <see cref="ContentTypeIndexRow.MissingNameCount"/>
    /// where populated; surfaced as a top-line stat so editors don't have to
    /// scan the table to spot trouble.
    /// </summary>
    [JsonPropertyName("missingNameCount")]
    public int MissingNameCount { get; init; }

    /// <summary>
    /// Aggregate count of items missing the <c>Title</c> / equivalent display
    /// field. Currently set to <c>0</c> when the schema doesn't expose a
    /// canonical Title field; the per-type rows carry the granular signal.
    /// </summary>
    [JsonPropertyName("missingTitleCount")]
    public int MissingTitleCount { get; init; }

    /// <summary>UTC moment when the inspection ran. Used by the UI for the freshness label.</summary>
    [JsonPropertyName("capturedAt")]
    public DateTime CapturedAt { get; init; }
}
