using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

/// <summary>
/// DDS-persisted saved query — a named bundle of Search Console settings the
/// editor wants to re-run later. Stored locally rather than upstream so it
/// works without admin write access to Graph.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SavedQueries")]
public class SavedQueryRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The search phrase. May be a literal string; placeholders are not interpolated server-side.</summary>
    public string Query { get; set; } = string.Empty;

    public string? Locale { get; set; }

    public string Ranking { get; set; } = "RELEVANCE";

    public double SemanticWeight { get; set; } = 0.2;

    public double? MinimumScore { get; set; }

    public int Limit { get; set; } = 25;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed record SavedQueryDto(
    string Id,
    string Name,
    string Description,
    string Query,
    string? Locale,
    string Ranking,
    double SemanticWeight,
    double? MinimumScore,
    int Limit,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SavedQueryPayload
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;
    public string? Locale { get; init; }
    public string Ranking { get; init; } = "RELEVANCE";
    public double SemanticWeight { get; init; } = 0.2;
    public double? MinimumScore { get; init; }
    public int Limit { get; init; } = 25;
}
