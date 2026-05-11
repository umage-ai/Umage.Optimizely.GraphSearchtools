using System.Text.Json.Serialization;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models;

/// <summary>
/// One token-count tier in a Semantic Weight Tuner policy. Each tier covers
/// the inclusive range <c>[<see cref="MinTokens"/>, <see cref="MaxTokens"/>]</c>
/// where <c>MaxTokens == null</c> means "open-ended" (matches every count
/// at or above <see cref="MinTokens"/>).
/// </summary>
/// <remarks>
/// The shape mirrors <see cref="SemanticTuningOptions"/> exactly so an editor
/// can paste the persisted JSON straight into <c>appsettings.json</c>. Both
/// surfaces use <see cref="GraphRanking"/> as a strongly-typed enum which
/// serializes as PascalCase via the configuration model.
/// </remarks>
public sealed class SemanticTier
{
    [JsonPropertyName("minTokens")]
    public int MinTokens { get; set; }

    /// <summary>
    /// Inclusive upper bound. <c>null</c> means open-ended — used for the
    /// final tier (e.g. <c>"3+ tokens → SEMANTIC w=0.3"</c>).
    /// </summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("ranking")]
    public GraphRanking Ranking { get; set; } = GraphRanking.Relevance;

    /// <summary>
    /// Semantic blend weight. Graph clamps to <c>[0.0, 1.0]</c>; we surface
    /// the same range as the slider step. Only meaningful when
    /// <see cref="Ranking"/> is <see cref="GraphRanking.Semantic"/>.
    /// </summary>
    [JsonPropertyName("semanticWeight")]
    public double SemanticWeight { get; set; }

    /// <summary>Optional editor note shown on the tier row.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// JSON DTO for the Semantic Tuner API surface. Matches the shape under
/// <c>CodeArt:GraphSearchtools:SemanticTuning</c> in <c>appsettings.json</c>
/// so the snippet emitted by the page is a literal paste of this structure.
/// </summary>
public sealed class SemanticTuningPolicy
{
    [JsonPropertyName("tiers")]
    public List<SemanticTier> Tiers { get; set; } = new();
}

/// <summary>
/// DDS-backed persistence row for the saved policy. Single-row store keyed by
/// <see cref="DefaultKey"/> — v1 has no per-tenant variation. The list of
/// tiers is serialized to JSON and stored as a single string column to keep
/// DDS schema management trivial.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SemanticTuningPolicy")]
public class SemanticTuningPolicyRecord : IDynamicData
{
    public const string DefaultKey = "default";

    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>Logical row key; v1 only ever stores <see cref="DefaultKey"/>.</summary>
    [EPiServerDataIndex]
    public string Key { get; set; } = DefaultKey;

    /// <summary>JSON-serialized <see cref="SemanticTuningPolicy"/>.</summary>
    public string TiersJson { get; set; } = "[]";

    /// <summary>Last write timestamp, UTC.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Display name of the editor that performed the last write, if known.</summary>
    public string UpdatedBy { get; set; } = string.Empty;
}
