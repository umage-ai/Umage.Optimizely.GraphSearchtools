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
    string? FullTextSnippet,
    /// <summary>
    /// Heuristic-resolved URL/path used by the SERP-style preview to render
    /// a Google-ish "result row". Tries common field names (<c>Url</c>,
    /// <c>RelativePath</c>, <c>Path</c>, <c>Slug</c>) and falls back to null
    /// when none are projected by the registered profile's GraphQL document.
    /// </summary>
    string? Url,
    /// <summary>
    /// Raw GraphQL item payload as returned by Graph, JSON-pretty-printed.
    /// Surfaced in the "show JSON" detail toggle so editors can inspect any
    /// field the registered profile projects, even ones the SERP card
    /// doesn't render.
    /// </summary>
    string? Raw,
    /// <summary>
    /// True when this hit is pinned in the active profile's collection AND
    /// the pin's phrase matches the preview phrase that produced this hit.
    /// Stamped server-side by <c>ProfilesService.RunPreviewAsync</c> after
    /// running the query — the renderer doesn't need to reverse-engineer the
    /// pin/organic relationship from the editor's local state.
    /// </summary>
    bool Pinned);

public sealed record RunnerResult(
    int TotalCount,
    long DurationMs,
    string GraphQuery,
    IReadOnlyList<RunnerHit> Hits);
