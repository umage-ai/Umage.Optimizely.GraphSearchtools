namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Default <see cref="ISearchProfileRegistry"/>. Snapshots the registered
/// <see cref="SearchProfile"/>s at construction time and serves read-only
/// views on top.
/// </summary>
public sealed class SearchProfileRegistry : ISearchProfileRegistry
{
    private readonly IReadOnlyList<SearchProfile> _all;
    private readonly Dictionary<string, SearchProfile> _byKey;

    public SearchProfileRegistry(IEnumerable<SearchProfile> profiles)
    {
        var registered = (profiles ?? Array.Empty<SearchProfile>()).ToList();
        _all = registered.AsReadOnly();
        _byKey = registered.ToDictionary(p => p.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<SearchProfile> All => _all;

    public SearchProfile? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _byKey.TryGetValue(key, out var profile) ? profile : null;
    }

    public IEnumerable<SearchProfile> ForSite(string siteName)
    {
        foreach (var profile in _all)
        {
            if (profile.Sites.Count == 0)
            {
                yield return profile;
                continue;
            }

            if (!string.IsNullOrEmpty(siteName) &&
                profile.Sites.Any(s => string.Equals(s, siteName, StringComparison.OrdinalIgnoreCase)))
            {
                yield return profile;
            }
        }
    }
}
