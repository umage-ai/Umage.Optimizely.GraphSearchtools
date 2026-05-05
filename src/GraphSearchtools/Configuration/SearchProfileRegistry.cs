using EPiServer.DataAbstraction;
using EPiServer.Web;

#pragma warning disable CS0618 // ISiteDefinitionRepository (CMS 12 name) — see LanguageSiteEnumerator.

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Default <see cref="ISearchProfileRegistry"/>. Snapshots the registered
/// <see cref="SearchProfile"/>s at construction time, computes "all sites" /
/// "all locales" once for the synthesised Generic profile, and serves
/// read-only views on top.
/// </summary>
public sealed class SearchProfileRegistry : ISearchProfileRegistry
{
    private readonly IReadOnlyList<SearchProfile> _all;
    private readonly Dictionary<string, SearchProfile> _byKey;

    public SearchProfileRegistry(
        IEnumerable<SearchProfile> profiles,
        ISiteDefinitionRepository siteDefinitions,
        ILanguageBranchRepository languageBranches)
    {
        var registered = (profiles ?? Array.Empty<SearchProfile>())
            // Defensive: don't let a host accidentally register their own "generic"
            // profile — it would collide with the synthesised one.
            .Where(p => !p.IsGeneric)
            .ToList();

        var generic = BuildGeneric(siteDefinitions, languageBranches);

        var combined = new List<SearchProfile>(registered.Count + 1);
        combined.AddRange(registered);
        combined.Add(generic);
        _all = combined.AsReadOnly();

        _byKey = combined.ToDictionary(p => p.Key, StringComparer.Ordinal);
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
            // Empty Sites list means "applies everywhere" — include unconditionally.
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

    private static SearchProfile BuildGeneric(
        ISiteDefinitionRepository? siteDefinitions,
        ILanguageBranchRepository? languageBranches)
    {
        IReadOnlyList<string> sites;
        try
        {
            sites = siteDefinitions == null
                ? Array.Empty<string>()
                : siteDefinitions.List()
                    .Select(s => s.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
        catch
        {
            // Repositories can throw outside a CMS runtime (e.g. in unit tests
            // that mock interfaces). The Generic profile is meant to degrade
            // gracefully — fall back to "applies everywhere".
            sites = Array.Empty<string>();
        }

        IReadOnlyList<string> locales;
        try
        {
            locales = languageBranches == null
                ? Array.Empty<string>()
                : languageBranches.ListEnabled()
                    .Select(b => b.LanguageID)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l!.ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        }
        catch
        {
            locales = Array.Empty<string>();
        }

        return new SearchProfile
        {
            Key = "generic",
            DisplayName = LocalizedString.Key("/graphsearchtools/profiles/generic/name"),
            Description = LocalizedString.Key("/graphsearchtools/profiles/generic/desc"),
            Sites = sites,
            Locales = locales,
            SearchedFields = Array.Empty<string>(),
            PinnedKeyForLocale = null,
            SemanticWeight = 0.2,
            Ranking = GraphRanking.Relevance,
            GraphQLDocumentPath = null,
            DefaultVariables = new Dictionary<string, object?>()
        };
    }
}
