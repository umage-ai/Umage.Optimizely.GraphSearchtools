using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources.Models;

/// <summary>
/// Camel-cased projection of <see cref="UmageAI.Optimizely.GraphSearchTools.Services.DataSourceResult"/>
/// for the <c>CustomDataSourcesApi/List</c> response. The on-the-wire Graph
/// DTO and the API DTO carry the same shape today, but the wrapper insulates
/// the JS from any future Graph schema drift.
/// </summary>
public sealed record DataSourceSummary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("itemCount")]
    public int? ItemCount { get; init; }

    [JsonPropertyName("lastSyncedAt")]
    public DateTime? LastSyncedAt { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;
}
