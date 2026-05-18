using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

public record PinnedCollectionPayload
{
    public string Key { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

public record PinnedCollectionUpdatePayload
{
    public string? Key { get; init; }
    public bool? IsActive { get; init; }
}

public record PinnedCollectionResult
{
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

