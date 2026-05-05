using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector.Models;

/// <summary>
/// Camel-cased projection of the <see cref="UmageAI.Optimizely.GraphSearchTools.Services.IndexInspectionResult"/>
/// for the <c>IndexInspectorApi/Get</c> response. The Graph DTO and the API
/// DTO carry the same shape today; the wrapper insulates the JS from any
/// future schema drift on the inspector endpoint.
/// </summary>
public sealed record IndexInspectorSnapshot
{
    [JsonPropertyName("totalItems")]
    public int TotalItems { get; init; }

    [JsonPropertyName("perContentType")]
    public IReadOnlyList<IndexInspectorRow> PerContentType { get; init; } = Array.Empty<IndexInspectorRow>();

    [JsonPropertyName("missingNameCount")]
    public int MissingNameCount { get; init; }

    [JsonPropertyName("missingTitleCount")]
    public int MissingTitleCount { get; init; }

    [JsonPropertyName("capturedAt")]
    public DateTime CapturedAt { get; init; }
}

/// <summary>
/// One row in the per-content-type breakdown surfaced to the page JS. Nullable
/// missing-* counts let the UI render an empty cell when the corresponding
/// field doesn't apply to a given content type — important so editors don't
/// chase a "0 missing teasers" stat on a type that has no teaser at all.
/// </summary>
public sealed record IndexInspectorRow
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("missingNameCount")]
    public int? MissingNameCount { get; init; }

    [JsonPropertyName("missingTeaserCount")]
    public int? MissingTeaserCount { get; init; }

    [JsonPropertyName("missingMainBodyCount")]
    public int? MissingMainBodyCount { get; init; }
}
