using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RequestLogs.Models;

/// <summary>
/// Camel-cased projection of
/// <see cref="UmageAI.Optimizely.GraphSearchTools.Services.RequestLogEntryResult"/>
/// for the <c>RequestLogsApi/List</c> response. Field-for-field mirror today
/// — the wrapper exists so a future Graph schema drift doesn't ripple through
/// to the JS without a deliberate edit here.
/// </summary>
public sealed record RequestLogEntrySummary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("at")]
    public DateTime At { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = "POST";

    [JsonPropertyName("operation")]
    public string? Operation { get; init; }

    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("variables")]
    public string? Variables { get; init; }

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }

    [JsonPropertyName("resultCount")]
    public int? ResultCount { get; init; }

    [JsonPropertyName("ranking")]
    public string? Ranking { get; init; }

    [JsonPropertyName("callerIp")]
    public string? CallerIp { get; init; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; init; }
}
