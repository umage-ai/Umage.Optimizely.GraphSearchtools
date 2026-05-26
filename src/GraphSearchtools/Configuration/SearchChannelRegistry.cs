namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Default <see cref="ISearchChannelRegistry"/>. Snapshots the registered
/// <see cref="SearchChannel"/>s at construction time and serves read-only
/// views on top.
/// </summary>
internal sealed class SearchChannelRegistry : ISearchChannelRegistry
{
    private readonly IReadOnlyList<SearchChannel> _all;
    private readonly Dictionary<string, SearchChannel> _byKey;

    public SearchChannelRegistry(IEnumerable<SearchChannel> channels)
    {
        var registered = (channels ?? Array.Empty<SearchChannel>()).ToList();
        _all = registered.AsReadOnly();
        _byKey = registered.ToDictionary(p => p.Key, StringComparer.Ordinal);
    }

    public IReadOnlyList<SearchChannel> All => _all;

    public SearchChannel? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return _byKey.TryGetValue(key, out var channel) ? channel : null;
    }

    public IEnumerable<SearchChannel> ForSite(string siteName)
    {
        foreach (var channel in _all)
        {
            if (channel.Sites.Count == 0)
            {
                yield return channel;
                continue;
            }

            if (!string.IsNullOrEmpty(siteName) &&
                channel.Sites.Any(s => string.Equals(s, siteName, StringComparison.OrdinalIgnoreCase)))
            {
                yield return channel;
            }
        }
    }
}
