namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Mirrors Optimizely Graph's <c>_ranking</c> argument values. Names are
/// kept in PascalCase here and serialized to the Graph-canonical SCREAMING_CASE
/// at the boundary.
/// </summary>
public enum GraphRanking
{
    /// <summary>Default lexical ranking — BM25 over searched fields.</summary>
    Relevance,

    /// <summary>Blended lexical + semantic; pair with <see cref="SearchProfile.SemanticWeight"/>.</summary>
    Semantic,

    /// <summary>Score determined entirely by <c>boost</c>/<c>factor</c> modifiers — no relevance term.</summary>
    BoostOnly,

    /// <summary>Document-id ordering, mainly for diagnostics.</summary>
    Doc
}
