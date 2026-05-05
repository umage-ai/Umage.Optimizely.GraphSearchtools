// STUB: belongs to foundation agent — to be replaced at integration.
//
// Minimal shape per docs/search-profiles-design.md §2.1, just enough for the
// Profiles UI scaffolding to compile against in isolation. The foundation
// agent's real implementation will replace this file with the validating
// builder + registry; do not extend this stub with logic.

using EPiServer.Framework.Localization;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Marketer-facing description of one search surface in the host solution.
/// See docs/search-profiles-design.md §2 for the full contract.
/// </summary>
public sealed class SearchProfile
{
    public string Key { get; init; } = string.Empty;
    public LocalizedString DisplayName { get; init; } = LocalizedString.Literal(string.Empty);
    public LocalizedString? Description { get; init; }
    public IReadOnlyList<string> Sites { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Locales { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchedFields { get; init; } = Array.Empty<string>();
    public string? SynonymSlot { get; init; }
    public Func<string, string>? PinnedKeyForLocale { get; init; }
    public double SemanticWeight { get; init; }
    public GraphRanking Ranking { get; init; } = GraphRanking.Relevance;
    public string? GraphQLDocumentPath { get; init; }
    public IReadOnlyDictionary<string, object?> DefaultVariables { get; init; }
        = new Dictionary<string, object?>();
}

/// <summary>
/// Mirrors Optimizely Graph's <c>_ranking</c> enum.
/// </summary>
public enum GraphRanking
{
    Relevance,
    Semantic,
    BoostOnly,
    Doc
}

/// <summary>
/// String that may be either a literal display value (developer typed it) or
/// an Optimizely localization key that resolves at request time.
/// </summary>
public sealed class LocalizedString
{
    private readonly string _value;
    private readonly bool _isKey;

    private LocalizedString(string value, bool isKey)
    {
        _value = value;
        _isKey = isKey;
    }

    /// <summary>Treat the string as a literal — return it as-is.</summary>
    public static LocalizedString Literal(string value) => new(value ?? string.Empty, isKey: false);

    /// <summary>Treat the string as an Optimizely loc key — resolve via <see cref="LocalizationService"/>.</summary>
    public static LocalizedString Key(string key) => new(key ?? string.Empty, isKey: true);

    /// <summary>
    /// Convenience: if the string starts with '/' it's treated as a loc key,
    /// otherwise as a literal. Matches the developer ergonomic expected by the
    /// fluent builder in §2.
    /// </summary>
    public static LocalizedString Auto(string value)
        => string.IsNullOrEmpty(value) ? Literal(string.Empty)
            : value.StartsWith('/') ? Key(value) : Literal(value);

    public string Resolve(LocalizationService localization)
    {
        if (!_isKey) return _value;
        if (string.IsNullOrEmpty(_value)) return string.Empty;
        return localization?.GetString(_value, _value) ?? _value;
    }

    public string Raw => _value;
    public bool IsKey => _isKey;
}
