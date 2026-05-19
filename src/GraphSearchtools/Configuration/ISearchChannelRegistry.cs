namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Read-only registry of <see cref="SearchChannel"/> instances registered by
/// the host at startup via <c>AddSearchChannel</c>.
/// </summary>
public interface ISearchChannelRegistry
{
    /// <summary>All channels known to the addon in registration order.</summary>
    IReadOnlyList<SearchChannel> All { get; }

    /// <summary>Look up a channel by key. Returns <c>null</c> when unknown.</summary>
    SearchChannel? Get(string key);

    /// <summary>
    /// Channels that apply to <paramref name="siteName"/>. A channel with an
    /// empty <see cref="SearchChannel.Sites"/> list is included for every site.
    /// </summary>
    IEnumerable<SearchChannel> ForSite(string siteName);
}
