using System.Reflection;
using System.Text.RegularExpressions;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Fluent builder for <see cref="SearchChannel"/>. Used inside the
/// <c>AddSearchChannel(key, configure)</c> registration extension. Validation
/// (key format) runs in <see cref="Build"/>.
/// </summary>
public sealed class SearchChannelBuilder
{
    private static readonly Regex KeyPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    private readonly string _key;
    private LocalizedString? _displayName;
    private LocalizedString? _description;
    private List<string> _sites = new();
    private List<string> _locales = new();
    private bool _localesFromCms;
    private List<string> _searchedFields = new();
    private Func<string, string>? _pinnedKeyForLocale;
    private double _semanticWeight = 0.2;
    private GraphRanking _ranking = GraphRanking.Relevance;
    private string? _graphQLDocumentPath;
    private string? _graphQLDocumentContent;
    private Dictionary<string, object?> _defaultVariables = new(StringComparer.Ordinal);

    public SearchChannelBuilder(string key)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>The key the builder was constructed with — exposed for diagnostics.</summary>
    public string Key => _key;

    public SearchChannelBuilder DisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public SearchChannelBuilder DisplayName(LocalizedString displayName)
    {
        _displayName = displayName;
        return this;
    }

    public SearchChannelBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public SearchChannelBuilder Description(LocalizedString description)
    {
        _description = description;
        return this;
    }

    public SearchChannelBuilder Sites(params string[] sites)
    {
        _sites = (sites ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
        return this;
    }

    public SearchChannelBuilder Locales(params string[] locales)
    {
        _locales = (locales ?? Array.Empty<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .ToList();
        // Explicit list wins over CMS-derived; clearing the flag here keeps
        // "last call wins" intuitive when a registration mixes both methods.
        _localesFromCms = false;
        return this;
    }

    /// <summary>
    /// Resolve locales from the CMS's enabled language branches at request
    /// time instead of from a static list. When the channel also declares
    /// <see cref="Sites(string[])"/>, the resolver narrows to languages
    /// served by those sites (via the sites' host-language declarations).
    /// Use this when content editors enable languages through CMS Admin
    /// without a developer redeploy — the new language surfaces here
    /// automatically. Mutually exclusive with explicit <see cref="Locales(string[])"/>;
    /// last call wins.
    /// </summary>
    public SearchChannelBuilder LocalesFromCmsLanguages()
    {
        _locales.Clear();
        _localesFromCms = true;
        return this;
    }

    public SearchChannelBuilder SearchedFields(params string[] fields)
    {
        _searchedFields = (fields ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .ToList();
        return this;
    }

    /// <summary>
    /// Sets the pinned-key formula. If <paramref name="keyOrTemplate"/> contains
    /// <c>{locale}</c> the placeholder is substituted at call time; otherwise the
    /// same key is returned for every locale.
    /// </summary>
    public SearchChannelBuilder UsesPinnedKey(string keyOrTemplate)
    {
        if (string.IsNullOrWhiteSpace(keyOrTemplate))
        {
            _pinnedKeyForLocale = null;
            return this;
        }

        var template = keyOrTemplate;
        if (template.Contains("{locale}", StringComparison.Ordinal))
        {
            _pinnedKeyForLocale = locale => template.Replace("{locale}", locale ?? string.Empty, StringComparison.Ordinal);
        }
        else
        {
            _pinnedKeyForLocale = _ => template;
        }
        return this;
    }

    public SearchChannelBuilder UsesPinnedKey(Func<string, string> formula)
    {
        _pinnedKeyForLocale = formula ?? throw new ArgumentNullException(nameof(formula));
        return this;
    }

    public SearchChannelBuilder SemanticBlend(double weight, GraphRanking ranking)
    {
        _semanticWeight = Math.Clamp(weight, -1.0, 1.0);
        _ranking = ranking;
        return this;
    }

    public SearchChannelBuilder GraphQLDocument(string path)
    {
        _graphQLDocumentPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        return this;
    }

    /// <summary>
    /// Registers the GraphQL document by value. Use this when the production
    /// query is built in code (string interpolation, fluent builder, etc.) so
    /// the admin sees the exact query the runtime executes — no static
    /// <c>.graphql</c> file to drift from the live code.
    /// </summary>
    public SearchChannelBuilder GraphQLDocumentInline(string content)
    {
        _graphQLDocumentContent = string.IsNullOrWhiteSpace(content) ? null : content;
        return this;
    }

    /// <summary>
    /// Convenience overload: reflect over an anonymous object's properties and
    /// add each as a default variable. Lets callers write
    /// <c>.Variables(new { limit = 20, contentType = "Article" })</c>.
    /// </summary>
    public SearchChannelBuilder Variables(object anonymous)
    {
        if (anonymous == null) return this;
        foreach (var prop in anonymous.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            _defaultVariables[prop.Name] = prop.GetValue(anonymous);
        }
        return this;
    }

    public SearchChannelBuilder Variables(IDictionary<string, object?> variables)
    {
        if (variables == null) return this;
        foreach (var kv in variables)
        {
            _defaultVariables[kv.Key] = kv.Value;
        }
        return this;
    }

    /// <summary>
    /// Materializes the configured channel. Throws <see cref="InvalidOperationException"/>
    /// when the key fails the <c>[a-z0-9-]+</c> validation, matching the design
    /// doc's startup-time guarantee.
    /// </summary>
    public SearchChannel Build()
    {
        if (string.IsNullOrEmpty(_key) || !KeyPattern.IsMatch(_key))
        {
            throw new InvalidOperationException(
                $"Search channel key '{_key}' is invalid. Channel key must match [a-z0-9-]+.");
        }

        return new SearchChannel
        {
            Key = _key,
            DisplayName = _displayName ?? LocalizedString.Literal(_key),
            Description = _description,
            Sites = _sites.AsReadOnly(),
            Locales = _locales.AsReadOnly(),
            LocalesFromCmsLanguages = _localesFromCms,
            SearchedFields = _searchedFields.AsReadOnly(),
            PinnedKeyForLocale = _pinnedKeyForLocale,
            SemanticWeight = _semanticWeight,
            Ranking = _ranking,
            GraphQLDocumentPath = _graphQLDocumentPath,
            GraphQLDocumentContent = _graphQLDocumentContent,
            DefaultVariables = new Dictionary<string, object?>(_defaultVariables, StringComparer.Ordinal)
        };
    }
}
