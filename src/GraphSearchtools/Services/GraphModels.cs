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
