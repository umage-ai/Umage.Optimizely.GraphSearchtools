using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks.Models;

/// <summary>
/// Camel-cased projection of <see cref="UmageAI.Optimizely.GraphSearchTools.Services.WebhookResult"/>
/// for the <c>WebhooksApi/List</c> response. The on-the-wire Graph DTO and the
/// API DTO carry the same shape today, but the wrapper insulates the JS from
/// any future Graph schema drift.
/// </summary>
public sealed record WebhookSummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; init; } = "POST";

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }

    [JsonPropertyName("filters")]
    public object? Filters { get; init; }

    [JsonPropertyName("disabled")]
    public bool Disabled { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; init; }
}

/// <summary>
/// Payload accepted by <c>WebhooksApi/Create</c>. Method and headers are
/// optional — Graph defaults to POST and no extra headers when these are
/// omitted. Filters are passed through as an opaque token; Graph is the
/// authority on what's accepted.
/// </summary>
public sealed record WebhookCreateRequest
{
    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("method")]
    public string? Method { get; init; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; init; }

    [JsonPropertyName("filters")]
    public object? Filters { get; init; }
}
