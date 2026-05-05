// STUB: belongs to foundation agent — to be replaced at integration.
//
// Trivial in-memory registry that lets the Profiles UI compile in isolation.
// The foundation agent owns the real registry (populated at AddGraphSearchtools
// time, includes the synthesised Generic profile per §5).

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

public interface ISearchProfileRegistry
{
    IReadOnlyList<SearchProfile> All { get; }
    SearchProfile? Get(string key);
    IEnumerable<SearchProfile> ForSite(string siteName);
}

/// <summary>
/// Empty default registry — the foundation agent will replace this with the
/// real implementation that always synthesises the Generic profile and
/// validates registrations at startup.
/// </summary>
internal sealed class EmptySearchProfileRegistry : ISearchProfileRegistry
{
    public IReadOnlyList<SearchProfile> All { get; } = Array.Empty<SearchProfile>();
    public SearchProfile? Get(string key) => null;
    public IEnumerable<SearchProfile> ForSite(string siteName) => Array.Empty<SearchProfile>();
}
