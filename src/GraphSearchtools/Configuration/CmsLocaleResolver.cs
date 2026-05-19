using EPiServer.DataAbstraction;
using EPiServer.Web;

// CMS 13 renames ISiteDefinitionRepository to IApplicationRepository and
// SiteDefinition to Application. The old types still resolve via DI in
// CMS 13, and a Tier-3 abstraction is planned when more code starts
// depending on these. Until then, suppress the obsolete warnings here
// only — mirrors the pragma in Helpers/LanguageSiteEnumerator.cs.
#pragma warning disable CS0618

namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Resolves a channel's effective locale list at request time, either from
/// its static <see cref="SearchChannel.Locales"/> declaration or — when
/// <see cref="SearchChannel.LocalesFromCmsLanguages"/> is set — from the
/// CMS's enabled language branches, optionally narrowed by the channel's
/// declared <see cref="SearchChannel.Sites"/>.
///
/// This is the single place ChannelsService consults for locales, so
/// "static list" and "derive from CMS" channels look identical to the
/// view-model layer above it.
/// </summary>
public sealed class CmsLocaleResolver
{
    private readonly ISiteDefinitionRepository _siteDefinitions;
    private readonly ILanguageBranchRepository _languageBranches;

    public CmsLocaleResolver(
        ISiteDefinitionRepository siteDefinitions,
        ILanguageBranchRepository languageBranches)
    {
        _siteDefinitions = siteDefinitions;
        _languageBranches = languageBranches;
    }

    public IReadOnlyList<string> Resolve(SearchChannel channel)
    {
        if (channel == null) return Array.Empty<string>();
        if (!channel.LocalesFromCmsLanguages)
        {
            return channel.Locales ?? Array.Empty<string>();
        }

        var enabled = _languageBranches.ListEnabled()
            .Where(b => !string.IsNullOrWhiteSpace(b.LanguageID))
            .ToList();
        if (enabled.Count == 0) return Array.Empty<string>();

        // Channel didn't declare sites → broadcast all enabled languages.
        if (channel.Sites == null || channel.Sites.Count == 0)
        {
            return enabled
                .Select(b => b.LanguageID!.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        // Channel scoped to specific sites → union of those sites' host-
        // language declarations. Sites with no explicit host-language config
        // fall back to "all enabled" (matches LanguageSiteEnumerator).
        var sites = _siteDefinitions.List()
            .Where(s => channel.Sites.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (sites.Count == 0)
        {
            return Array.Empty<string>();
        }

        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in sites)
        {
            foreach (var branch in ResolveSiteLanguages(site, enabled))
            {
                if (!string.IsNullOrWhiteSpace(branch.LanguageID))
                {
                    union.Add(branch.LanguageID!.ToLowerInvariant());
                }
            }
        }
        return union.ToList();
    }

    private static IEnumerable<LanguageBranch> ResolveSiteLanguages(
        SiteDefinition site,
        IReadOnlyList<LanguageBranch> enabledBranches)
    {
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
