namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Read-only registry of <see cref="SearchProfile"/> instances registered by
/// the host at startup via <c>AddSearchProfile</c>.
/// </summary>
public interface ISearchProfileRegistry
{
    /// <summary>All profiles known to the addon in registration order.</summary>
    IReadOnlyList<SearchProfile> All { get; }

    /// <summary>Look up a profile by key. Returns <c>null</c> when unknown.</summary>
    SearchProfile? Get(string key);

    /// <summary>
    /// Profiles that apply to <paramref name="siteName"/>. A profile with an
    /// empty <see cref="SearchProfile.Sites"/> list is included for every site.
    /// </summary>
    IEnumerable<SearchProfile> ForSite(string siteName);
}
