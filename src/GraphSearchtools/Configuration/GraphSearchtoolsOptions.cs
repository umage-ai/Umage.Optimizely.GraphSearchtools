namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Configuration options for Graph Search Tools.
/// Bound from appsettings section "CodeArt:GraphSearchtools" by AddGraphSearchtools().
/// </summary>
public class GraphSearchtoolsOptions
{
    /// <summary>
    /// Feature toggles to enable/disable individual tools.
    /// </summary>
    public FeatureToggles Features { get; set; } = new();

    /// <summary>
    /// When true, each feature checks the user's EPiServer permissions (Permissions For Functions)
    /// in addition to the authorization policy.
    /// </summary>
    public bool CheckPermissionForEachFeature { get; set; }

    /// <summary>
    /// Roles that have full access to all Graph Search Tools features.
    /// Default: WebAdmins, Administrators.
    /// </summary>
    public string[] AuthorizedRoles { get; set; } = ["WebAdmins", "Administrators"];

    /// <summary>
    /// Optional override for Optimizely Graph credentials. When unset, the addon reads
    /// from the host's Optimizely:ContentGraph section.
    /// </summary>
    public GraphConnectionOptions? Graph { get; set; }

    /// <summary>
    /// Content type fully qualified names that the search picker / saved-queries
    /// runner should query. Defaults to ["_Page"] so the addon works out of the
    /// box on a vanilla CMS install.
    /// </summary>
    public string[] SearchableContentTypes { get; set; } = ["_Page"];

    /// <summary>
    /// Saved Queries-specific options (incl. the runner's default GraphQL query).
    /// </summary>
    public SavedQueriesOptions SavedQueries { get; set; } = new();

    /// <summary>
    /// Semantic Weight Tuner-specific options (token-count-tiered ranking policy).
    /// </summary>
    public SemanticTuningOptions SemanticTuning { get; set; } = new();
}

/// <summary>
/// Token-count-tiered ranking policy emitted by the Semantic Weight Tuner.
/// </summary>
/// <remarks>
/// Two surfaces carry this policy: the bound configuration here (cold-start
/// default, populated by the host's <c>appsettings.json</c>) and the
/// DDS-persisted record edited via <c>SemanticTunerService</c>. They are
/// independent — the bound options act as a static fallback, while the saved
/// policy is the editable source of truth in the running app. A consumer that
/// wants the live policy should call <c>SemanticTunerService.GetPolicy()</c>
/// rather than reading these options directly.
/// </remarks>
public class SemanticTuningOptions
{
    /// <summary>
    /// Ordered tiers. The first tier whose <c>MinTokens..MaxTokens</c> range
    /// covers a query's token count wins. An open-ended last tier
    /// (<c>MaxTokens == null</c>) catches everything beyond the previous
    /// upper bound.
    /// </summary>
    public List<UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models.SemanticTier> Tiers { get; set; } = new();
}

/// <summary>
/// Options that change the behavior of the Saved Queries runner.
/// </summary>
public class SavedQueriesOptions
{
    /// <summary>
    /// Optional GraphQL query template that replaces the built-in generic Content
    /// query. When set, the addon sends this query verbatim to Graph and the UI's
    /// ranking knobs (ranking mode, semantic weight, minimum score) are ignored —
    /// the query already encodes its own ranking and filters.
    ///
    /// The query receives these variables: <c>$q</c> / <c>$query</c> (the search
    /// phrase), <c>$limit</c> (Int), <c>$locale</c> ([Locales!]), <c>$today</c>
    /// (Date, today's date in yyyy-MM-dd). Declare the ones you need; GraphQL
    /// ignores undeclared variables. Additional fixed variables can be supplied
    /// via <see cref="DefaultQueryVariables"/>.
    ///
    /// The result is parsed generically: the first object under <c>data</c>
    /// containing an <c>items</c> array is treated as the result block. Per-item
    /// fields commonly read for display: Name, ContentType, ContentLink.{Id,
    /// GuidValue}, Language.Name, _score, and a snippet field (one of _fulltext,
    /// GetExcerpt, Excerpt, Description, Url).
    /// </summary>
    public string? DefaultQuery { get; set; }

    /// <summary>
    /// Additional GraphQL variables forwarded with the configured
    /// <see cref="DefaultQuery"/>. Values are JSON-serialized as-is (strings,
    /// numbers, booleans, arrays, nested objects). Useful for parameters the
    /// query needs that don't change between runs — e.g. content-type filters
    /// or pinned-collection keys.
    /// </summary>
    public Dictionary<string, object?> DefaultQueryVariables { get; set; } = new();
}

/// <summary>
/// Optimizely Graph connection credentials. Mirrors Optimizely:ContentGraph host config.
/// </summary>
public class GraphConnectionOptions
{
    public string? GatewayAddress { get; set; }
    public string? AppKey { get; set; }
    public string? Secret { get; set; }
    public string? SingleKey { get; set; }
}
