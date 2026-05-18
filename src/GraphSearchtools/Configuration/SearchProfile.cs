namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Developer-declared description of one search surface in the customer
/// solution (header search, product listing, knowledge base, …). Profiles
/// scope pinned-result and synonym tuning to specific production queries so
/// marketers don't edit tenant-global data that may not be wired in.
/// </summary>
/// <remarks>
/// Construct via <see cref="SearchProfileBuilder"/> through the
/// <c>AddSearchProfile</c> registration extension. The shape mirrors the
/// design doc (<c>docs/search-profiles-design.md</c> §2.1) one-to-one.
/// </remarks>
public sealed class SearchProfile
{
    /// <summary>Stable identifier used in URLs and DDS keys; matches <c>[a-z0-9-]+</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Display name shown in the Profiles index and detail header.</summary>
    public required LocalizedString DisplayName { get; init; }

    /// <summary>Optional secondary description shown alongside the display name.</summary>
    public LocalizedString? Description { get; init; }

    /// <summary>
    /// <c>SiteDefinition.Name</c> values this profile applies to. Empty list
    /// means "all sites" (i.e. shared across the tenant).
    /// </summary>
    public IReadOnlyList<string> Sites { get; init; } = Array.Empty<string>();

    /// <summary>
    /// BCP-47 lower-case locale codes this profile applies to. Empty list
    /// means "all locales".
    /// </summary>
    public IReadOnlyList<string> Locales { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Field names searched by the production query. Used by the diagnostic
    /// runner and the search-coverage audit. Order is preserved.
    /// </summary>
    public IReadOnlyList<string> SearchedFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Resolves the pinned-results collection key for a given locale. <c>null</c>
    /// hides the per-profile Pinned tab. The lambda lets multi-site solutions
    /// declare per-site keys (e.g. <c>locale =&gt; $"corp-{locale}"</c>).
    /// </summary>
    public Func<string, string>? PinnedKeyForLocale { get; init; }

    /// <summary>Default semantic weight used by the diagnostic runner.</summary>
    public double SemanticWeight { get; init; } = 0.2;

    /// <summary>Default ranking mode used by the diagnostic runner.</summary>
    public GraphRanking Ranking { get; init; } = GraphRanking.Relevance;

    /// <summary>
    /// Path (relative to the host's content root) to the GraphQL document the
    /// production code uses for this profile. <c>null</c> disables the
    /// Try-it tab. Mutually exclusive in spirit with
    /// <see cref="GraphQLDocumentContent"/>: pick whichever matches how the
    /// host code stores its query.
    /// </summary>
    public string? GraphQLDocumentPath { get; init; }

    /// <summary>
    /// Inline GraphQL document text. Used when the production query is built
    /// in code rather than authored in a file — the host can pass the same
    /// string the runtime executes so the admin Profile detail view always
    /// reflects what's running in production. Takes precedence over
    /// <see cref="GraphQLDocumentPath"/> when both are set.
    /// </summary>
    public string? GraphQLDocumentContent { get; init; }

    /// <summary>
    /// Variables passed to <see cref="GraphQLDocumentPath"/> in addition to the
    /// runner-controlled ones (<c>q</c>, <c>locale</c>, <c>limit</c>, …).
    /// </summary>
    public IReadOnlyDictionary<string, object?> DefaultVariables { get; init; }
        = new Dictionary<string, object?>();
}
