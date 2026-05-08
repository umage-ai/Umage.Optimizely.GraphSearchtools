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

