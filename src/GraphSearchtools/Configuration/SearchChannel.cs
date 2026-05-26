namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Developer-declared description of one search surface in the customer
/// solution (header search, product listing, knowledge base, …). Channels
/// scope pinned-result and synonym tuning to specific production queries so
/// marketers don't edit tenant-global data that may not be wired in.
/// </summary>
/// <remarks>
/// Construct via <see cref="SearchChannelBuilder"/> through the
/// <c>AddSearchChannel</c> registration extension. The shape mirrors the
/// design doc (<c>docs/search-channels-design.md</c> §2.1) one-to-one.
/// </remarks>
public sealed class SearchChannel
{
    /// <summary>Stable identifier used in URLs and DDS keys; matches <c>[a-z0-9-]+</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Display name shown in the Channels index and detail header.</summary>
    public required LocalizedString DisplayName { get; init; }

    /// <summary>Optional secondary description shown alongside the display name.</summary>
    public LocalizedString? Description { get; init; }

    /// <summary>
    /// <c>SiteDefinition.Name</c> values this channel applies to. Empty list
    /// means "all sites" (i.e. shared across the tenant).
    /// </summary>
    public IReadOnlyList<string> Sites { get; init; } = Array.Empty<string>();

    /// <summary>
    /// BCP-47 lower-case locale codes this channel applies to. Empty list
    /// means "all locales". When <see cref="LocalesFromCmsLanguages"/> is
    /// <c>true</c> this list is ignored — locales are resolved per-request
    /// from <c>ILanguageBranchRepository.ListEnabled()</c>, optionally
    /// narrowed by <see cref="Sites"/>.
    /// </summary>
    public IReadOnlyList<string> Locales { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When <c>true</c>, locales are resolved per-request from the CMS's
    /// enabled language branches rather than the static <see cref="Locales"/>
    /// list. Set via <c>SearchChannelBuilder.LocalesFromCmsLanguages()</c>.
    /// Use this when content editors can enable new languages without a
    /// developer redeploy — turning on a new language branch in CMS Admin
    /// surfaces it in the channel without touching the registration.
    /// </summary>
    public bool LocalesFromCmsLanguages { get; init; }

    /// <summary>
    /// Field names searched by the production query. Surfaced on the
    /// Channel detail page so the admin matches what the live storefront
    /// queries. Order is preserved.
    /// </summary>
    public IReadOnlyList<string> SearchedFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Resolves the pinned-results collection key for a given locale. <c>null</c>
    /// hides the per-channel Pinned tab. The lambda lets multi-site solutions
    /// declare per-site keys (e.g. <c>locale =&gt; $"corp-{locale}"</c>).
    /// </summary>
    public Func<string, string>? PinnedKeyForLocale { get; init; }

    /// <summary>
    /// Path (relative to the host's content root) to the GraphQL document the
    /// production code uses for this channel. <c>null</c> disables the
    /// Try-it tab. Mutually exclusive in spirit with
    /// <see cref="GraphQLDocumentContent"/>: pick whichever matches how the
    /// host code stores its query.
    /// </summary>
    public string? GraphQLDocumentPath { get; init; }

    /// <summary>
    /// Inline GraphQL document text. Used when the production query is built
    /// in code rather than authored in a file — the host can pass the same
    /// string the runtime executes so the admin Channel detail view always
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
