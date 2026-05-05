namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

// Runner request/response types used by QueryRunnerService and the
// /SavedQueriesApi/Run endpoint that backs the Pinned tab's A/B preview.
// The user-facing Saved Queries surface (preset CRUD, runner page) was
// dropped — Graph's GraphiQL covers ad-hoc query exploration far better.

public sealed record RunnerRequest
{
    public string Query { get; init; } = string.Empty;
    public string? Locale { get; init; }

    /// <summary>RELEVANCE | SEMANTIC | BOOST_ONLY | DOC. Default RELEVANCE.</summary>
    public string Ranking { get; init; } = "RELEVANCE";

    /// <summary>Float between -1.0 and 1.0; default 0.2.</summary>
    public double SemanticWeight { get; init; } = 0.2;

    /// <summary>When set, cuts results below this score and auto-activates ranking.</summary>
    public double? MinimumScore { get; init; }

    public int Limit { get; init; } = 25;
}

public sealed record RunnerHit(
    string Name,
    string ContentType,
    string Language,
    int? ContentId,
    string ContentGuid,
    double Score,
    string? FullTextSnippet);

public sealed record RunnerResult(
    int TotalCount,
    long DurationMs,
    string GraphQuery,
    IReadOnlyList<RunnerHit> Hits);
