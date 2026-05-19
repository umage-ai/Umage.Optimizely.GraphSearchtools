using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs.Models;

/// <summary>
/// Camel-cased projection of the reader's <c>PhraseAggregate</c> for the three
/// phrase-level cards (top, zero-result, low-CTR). Decoupled from the reader
/// type so the wire shape can evolve independently.
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

    /// <summary>Click-through rate — sessions with a click at rank 1..3 over total; in <c>[0, 1]</c>.</summary>
    [JsonPropertyName("ctr")]
    public double Ctr { get; init; }

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("channelKey")]
    public string ChannelKey { get; init; } = string.Empty;
}

/// <summary>
/// Camel-cased projection of one row from the per-instance forensic ring,
/// used by the live-tail card.
/// </summary>
/// <remarks>
/// The aggregate-first design (v0.5) drops several columns the legacy raw
/// table carried — <c>Site</c>, <c>TopResultId</c>, <c>DurationMs</c>,
/// <c>Ranking</c>, <c>Source</c> — because the new ingest beacon doesn't
/// require the host to send them. The card now shows search vs click as
/// distinct rows (<c>Kind</c>) and the click rank when present.
/// </remarks>
public sealed record SearchLogRawRow
{
    [JsonPropertyName("at")]
    public DateTime At { get; init; }

    /// <summary>"search" or "click".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("phrase")]
    public string Phrase { get; init; } = string.Empty;

    [JsonPropertyName("locale")]
    public string Locale { get; init; } = string.Empty;

    [JsonPropertyName("channelKey")]
    public string ChannelKey { get; init; } = string.Empty;

    /// <summary>Set on search events; null on click events.</summary>
    [JsonPropertyName("resultCount")]
    public int? ResultCount { get; init; }

    /// <summary>Set on click events (1-based rank); null on search events.</summary>
    [JsonPropertyName("clickRank")]
    public int? ClickRank { get; init; }

    /// <summary>Originating instance — useful when scaling out to multiple nodes.</summary>
    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;
}
