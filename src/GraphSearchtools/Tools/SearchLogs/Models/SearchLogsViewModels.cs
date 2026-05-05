using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs.Models;

/// <summary>
/// Camel-cased projection of <see cref="UmageAI.Optimizely.GraphSearchTools.Services.SearchLogAggregateRow"/>
/// for the three phrase-level cards (top, zero-result, low-CTR). The wire-shape
/// stays decoupled from the DDS-side record so we can evolve the analytics
/// surface without touching the foundation service.
/// </summary>
public sealed record SearchLogPhraseRow
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; init; } = string.Empty;

    [JsonPropertyName("hits")]
    public int Hits { get; init; }

    /// <summary>Fraction of sessions whose ResultCount was 0; in <c>[0, 1]</c>.</summary>
    [JsonPropertyName("zeroResultRate")]
    public double ZeroResultRate { get; init; }

    /// <summary>Click-through rate — sessions with TopResultRank ≤ 3 over total; in <c>[0, 1]</c>.</summary>
    [JsonPropertyName("ctr")]
    public double Ctr { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("profileKey")]
    public string ProfileKey { get; init; } = string.Empty;
}

/// <summary>
/// Camel-cased projection of <see cref="UmageAI.Optimizely.GraphSearchTools.Services.SearchLogEntry"/>
/// for the recent-raw-events card. <c>Id</c> is omitted — the UI doesn't need it
/// and exposing the DDS Identity isn't useful to a client.
/// </summary>
public sealed record SearchLogRawRow
{
    [JsonPropertyName("at")]
    public DateTime At { get; init; }

    [JsonPropertyName("phrase")]
    public string Phrase { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("site")]
    public string Site { get; init; } = string.Empty;

    [JsonPropertyName("profileKey")]
    public string ProfileKey { get; init; } = string.Empty;

    [JsonPropertyName("resultCount")]
    public int ResultCount { get; init; }

    [JsonPropertyName("topResultRank")]
    public int? TopResultRank { get; init; }

    [JsonPropertyName("topResultId")]
    public string TopResultId { get; init; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }

    [JsonPropertyName("ranking")]
    public string Ranking { get; init; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;
}
