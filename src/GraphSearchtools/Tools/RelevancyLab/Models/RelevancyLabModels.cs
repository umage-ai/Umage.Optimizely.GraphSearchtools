using System.Text.Json.Serialization;
using EPiServer.Data;
using EPiServer.Data.Dynamic;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab.Models;

// ──────────────────────────────────────────────────────────────────
//   API DTOs (camelCase via [JsonPropertyName] for JS interop)
// ──────────────────────────────────────────────────────────────────

/// <summary>
/// One expected hit for a phrase in a golden set. The pair
/// <c>(ContentLink, Weight)</c> drives the NDCG@10 ideal-DCG calculation —
/// higher-weighted hits at the top of the actual result list earn more.
/// </summary>
public sealed class ExpectedHit
{
    /// <summary>Identifier of the expected content (typically the GuidValue
    /// from <c>ContentLink</c>, but free-form so callers can match on either
    /// numeric Id or guid).</summary>
    [JsonPropertyName("contentLink")]
    public string ContentLink { get; set; } = string.Empty;

    /// <summary>Relevance weight (1 = baseline). NDCG ideal ordering is by
    /// descending Weight; values &lt;= 0 are treated as 0 for ideal-DCG.</summary>
    [JsonPropertyName("weight")]
    public int Weight { get; set; } = 1;
}

/// <summary>One <c>(phrase, expected top-N)</c> pair in a golden set.</summary>
public sealed class GoldenItem
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; set; } = string.Empty;

    [JsonPropertyName("expectedTop")]
    public List<ExpectedHit> ExpectedTop { get; set; } = new();
}

/// <summary>
/// A named bundle of golden phrases for a single locale. Persisted to DDS
/// (see <see cref="GoldenSetRecord"/>) and shipped to the client as-is.
/// </summary>
public sealed class GoldenSet
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>BCP-47 lowercase locale; empty = no locale constraint.</summary>
    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("items")]
    public List<GoldenItem> Items { get; set; } = new();
}

/// <summary>Ranking knobs the run engine forwards to the query runner.</summary>
public sealed class RankingConfig
{
    /// <summary>
    /// Mirrors Graph's <c>_ranking</c> argument. The
    /// <see cref="JsonStringEnumConverter"/> attribute lets the JS layer post
    /// PascalCase strings (<c>"Relevance"</c>, <c>"Semantic"</c>, …) without
    /// needing a global ASP.NET Core JSON option override — same trick used by
    /// <c>HealthStatus</c> in <c>Tools/Health/HealthModels.cs</c>.
    /// </summary>
    [JsonPropertyName("ranking")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public GraphRanking Ranking { get; set; } = GraphRanking.Relevance;

    [JsonPropertyName("semanticWeight")]
    public double SemanticWeight { get; set; } = 0.2;

    /// <summary>Optional <c>_minimumScore</c>; null = unset.</summary>
    [JsonPropertyName("minScore")]
    public double? MinScore { get; set; }
}

/// <summary>Per-phrase scoring snapshot for a single run.</summary>
public sealed class QueryEval
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; set; } = string.Empty;

    /// <summary>The <c>ContentLink</c>-shaped IDs of the actual top-N results.</summary>
    [JsonPropertyName("actualTop")]
    public List<string> ActualTop { get; set; } = new();

    [JsonPropertyName("ndcg10")]
    public double Ndcg10 { get; set; }

    [JsonPropertyName("mrr")]
    public double Mrr { get; set; }

    /// <summary>Total hit count Graph reported for the phrase.</summary>
    [JsonPropertyName("hits")]
    public int Hits { get; set; }

    /// <summary>Surface error if the run failed for this phrase (rest of the
    /// run still completes — we record the failure inline).</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>One historical run of a golden set against a ranking config.</summary>
public sealed class Run
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("goldenSetId")]
    public Guid GoldenSetId { get; set; }

    /// <summary>Snapshot of the golden set's name at run-time, for display
    /// when the source set has been renamed/deleted later.</summary>
    [JsonPropertyName("goldenSetName")]
    public string GoldenSetName { get; set; } = string.Empty;

    [JsonPropertyName("at")]
    public DateTime At { get; set; }

    [JsonPropertyName("config")]
    public RankingConfig Config { get; set; } = new();

    [JsonPropertyName("perQuery")]
    public List<QueryEval> PerQuery { get; set; } = new();

    /// <summary>Mean NDCG@10 over <see cref="PerQuery"/>.</summary>
    [JsonPropertyName("ndcg10")]
    public double Ndcg10 { get; set; }

    /// <summary>Mean MRR over <see cref="PerQuery"/>.</summary>
    [JsonPropertyName("mrr")]
    public double Mrr { get; set; }
}

// ──────────────────────────────────────────────────────────────────
//   Comparison view DTOs
// ──────────────────────────────────────────────────────────────────

/// <summary>Side-by-side diff for one phrase across two runs.</summary>
public sealed class CompareEntry
{
    [JsonPropertyName("phrase")]
    public string Phrase { get; set; } = string.Empty;

    [JsonPropertyName("ndcgA")]
    public double NdcgA { get; set; }

    [JsonPropertyName("ndcgB")]
    public double NdcgB { get; set; }

    [JsonPropertyName("ndcgDelta")]
    public double NdcgDelta { get; set; }

    [JsonPropertyName("mrrA")]
    public double MrrA { get; set; }

    [JsonPropertyName("mrrB")]
    public double MrrB { get; set; }

    [JsonPropertyName("mrrDelta")]
    public double MrrDelta { get; set; }
}

/// <summary>Aggregate result returned by <c>GET /api/relevancylab/compare</c>.</summary>
public sealed class CompareResult
{
    [JsonPropertyName("runA")]
    public Run? RunA { get; set; }

    [JsonPropertyName("runB")]
    public Run? RunB { get; set; }

    [JsonPropertyName("entries")]
    public List<CompareEntry> Entries { get; set; } = new();

    [JsonPropertyName("ndcgDelta")]
    public double NdcgDelta { get; set; }

    [JsonPropertyName("mrrDelta")]
    public double MrrDelta { get; set; }
}

/// <summary>Body of <c>POST /api/relevancylab/run</c>.</summary>
public sealed class RunRequest
{
    [JsonPropertyName("goldenSetId")]
    public Guid GoldenSetId { get; set; }

    [JsonPropertyName("config")]
    public RankingConfig Config { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────
//   DDS persistence rows. The list-shaped fields are stored as JSON in a
//   single column so DDS schema evolution stays trivial — same pattern as
//   SemanticTuningPolicyRecord.
// ──────────────────────────────────────────────────────────────────

[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_RelevancyLab_GoldenSets")]
public class GoldenSetRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex]
    public Guid SetId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [EPiServerDataIndex]
    public string Locale { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>JSON-serialised <see cref="GoldenSet.Items"/>.</summary>
    public string ItemsJson { get; set; } = "[]";
}

[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_RelevancyLab_Runs")]
public class RunRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex]
    public Guid RunId { get; set; }

    [EPiServerDataIndex]
    public Guid GoldenSetId { get; set; }

    public string GoldenSetName { get; set; } = string.Empty;

    [EPiServerDataIndex]
    public DateTime At { get; set; }

    /// <summary>JSON-serialised <see cref="Run.Config"/>.</summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>JSON-serialised <see cref="Run.PerQuery"/>.</summary>
    public string PerQueryJson { get; set; } = "[]";

    public double Ndcg10 { get; set; }
    public double Mrr { get; set; }
}
