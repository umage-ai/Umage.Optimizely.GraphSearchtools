namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Read-only registry of <see cref="SearchProfile"/> instances. Always exposes
/// a synthesized <c>generic</c> profile in addition to whatever was registered
/// at startup, so the addon never has zero profiles to display.
/// </summary>
public interface ISearchProfileRegistry
{
    /// <summary>
    /// All profiles known to the addon — registered profiles first (in
    /// registration order) followed by the synthesized Generic catchment.
    /// </summary>
    IReadOnlyList<SearchProfile> All { get; }

    /// <summary>Look up a profile by key. Returns <c>null</c> when unknown.</summary>
    SearchProfile? Get(string key);

    /// <summary>
    /// Profiles that apply to <paramref name="siteName"/>. A profile with an
    /// empty <see cref="SearchProfile.Sites"/> list is included for every site.
    /// </summary>
    IEnumerable<SearchProfile> ForSite(string siteName);
}
