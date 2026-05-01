using EPiServer.DataAbstraction;
using EPiServer.Web;
using UmageAI.Optimizely.GraphSearchTools.Services;

// CMS 13 renames ISiteDefinitionRepository to IApplicationRepository and
// SiteDefinition to Application. The old types still resolve via DI in CMS 13,
// and a Tier-3 abstraction is planned when more code starts depending on this.
// Until then, suppress the obsolete warnings here only.
#pragma warning disable CS0618

namespace UmageAI.Optimizely.GraphSearchTools.Helpers;

/// <summary>
/// Enumerates language-site pairs without making any assumption about the host
/// site's content model. Replaces the seed code's <c>StartPage.LanguageSitesOrDefault()</c>
/// helper, which depended on a host-specific page type.
/// </summary>
public sealed class LanguageSiteEnumerator
{
    private readonly ISiteDefinitionRepository _siteDefinitions;
    private readonly ILanguageBranchRepository _languageBranches;

    public LanguageSiteEnumerator(
        ISiteDefinitionRepository siteDefinitions,
        ILanguageBranchRepository languageBranches)
    {
        _siteDefinitions = siteDefinitions;
        _languageBranches = languageBranches;
    }

    public IReadOnlyList<SiteInfo> Enumerate()
    {
        var sites = _siteDefinitions.List().ToList();
        var enabledBranches = _languageBranches.ListEnabled().ToList();

        // Single-site or no language branches: return one row per enabled language.
        if (sites.Count == 0)
        {
            return enabledBranches
                .Where(b => !string.IsNullOrWhiteSpace(b.LanguageID))
                .Select(b => new SiteInfo
                {
                    Title = b.Name ?? b.LanguageID,
                    LanguageCode = b.LanguageID,
                    CollectionKey = b.LanguageID
                })
                .ToList();
        }

        var results = new List<SiteInfo>();
        foreach (var site in sites)
        {
            var siteBranches = ResolveSiteLanguages(site, enabledBranches);
            foreach (var branch in siteBranches)
            {
                if (string.IsNullOrWhiteSpace(branch.LanguageID)) continue;

                var title = sites.Count == 1
                    ? branch.Name ?? branch.LanguageID
                    : $"{site.Name} ({branch.LanguageID})";

                results.Add(new SiteInfo
                {
                    Title = title,
                    LanguageCode = branch.LanguageID,
                    CollectionKey = branch.LanguageID
                });
            }
        }
        return results;
    }

    private static IEnumerable<LanguageBranch> ResolveSiteLanguages(
        SiteDefinition site,
        IReadOnlyList<LanguageBranch> enabledBranches)
    {
        // Hosts may declare per-language hostnames; if so, those are the canonical site languages.
        var hostLanguages = site.Hosts
            .Where(h => h.Language != null && !string.IsNullOrEmpty(h.Language.Name))
            .Select(h => h.Language!.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hostLanguages.Count > 0)
        {
            return enabledBranches.Where(b =>
                hostLanguages.Contains(b.LanguageID, StringComparer.OrdinalIgnoreCase));
        }

        return enabledBranches;
    }
}
