using EPiServer.Framework.Localization;

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// A display string that can be either a literal piece of text or a key into
/// the Optimizely localization system. Channels use this for
/// <see cref="SearchChannel.DisplayName"/> and <see cref="SearchChannel.Description"/>
/// so developers can register channels with hard-coded labels in small solutions
/// or with localized labels in multi-language ones — without having to choose at
/// type-definition time.
/// </summary>
/// <remarks>
/// Use the factory methods <see cref="Literal(string)"/> and <see cref="Key(string)"/>
/// to construct instances. <see cref="Resolve(LocalizationService)"/> and
/// <see cref="Resolve(LocalizationService, string)"/> turn the value into the
/// final display string.
/// </remarks>
public sealed class LocalizedString
{
    private readonly string _value;
    private readonly bool _isKey;

    private LocalizedString(string value, bool isKey)
    {
        _value = value ?? string.Empty;
        _isKey = isKey;
    }

    /// <summary>True when the underlying value is a localization key (e.g. <c>/graphsearchtools/...</c>).</summary>
    public bool IsKey => _isKey;

    /// <summary>The raw underlying value — either the literal text or the loc key.</summary>
    public string Value => _value;

    /// <summary>Constructs a literal string that will not be looked up.</summary>
    public static LocalizedString Literal(string value) => new(value ?? string.Empty, isKey: false);

    /// <summary>Constructs a localization-key reference, e.g. <c>/graphsearchtools/channels/site/name</c>.</summary>
    public static LocalizedString Key(string key) => new(key ?? string.Empty, isKey: true);

    /// <summary>
    /// Resolves the value via <paramref name="localization"/>. Literal values pass
    /// through unchanged. Keys are resolved with the localization service; if the
    /// service is null the raw key is returned.
    /// </summary>
    public string Resolve(LocalizationService? localization)
    {
        if (!_isKey || localization == null) return _value;
        return localization.GetString(_value, _value);
    }

    /// <summary>
    /// Resolves the value via <paramref name="localization"/>, returning
    /// <paramref name="fallback"/> when the localization service can't find the
    /// key (rather than the raw key text).
    /// </summary>
    public string Resolve(LocalizationService? localization, string fallback)
    {
        if (!_isKey) return _value;
        if (localization == null) return fallback;
        return localization.GetString(_value, fallback);
    }

    /// <summary>
    /// Implicit conversion from <see cref="string"/>. Strings that look like
    /// localization paths (start with <c>/</c>) are treated as keys; all others
    /// are treated as literals. This keeps the fluent builder concise:
    /// <c>.DisplayName("/graphsearchtools/channels/site/name")</c> Just Works.
    /// </summary>
    public static implicit operator LocalizedString(string value)
    {
        if (string.IsNullOrEmpty(value)) return Literal(string.Empty);
        return value.StartsWith('/') ? Key(value) : Literal(value);
    }

    public override string ToString() => _value;
}
