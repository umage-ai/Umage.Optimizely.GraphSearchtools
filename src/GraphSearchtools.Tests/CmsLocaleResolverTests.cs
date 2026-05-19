using EPiServer.DataAbstraction;
using EPiServer.Web;
using FluentAssertions;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

// Mirrors the pragma in CmsLocaleResolver — the EPiServer 12 types we use
// are obsolete in 13 but resolve at runtime; the tests run on net8/net10
// against both.
#pragma warning disable CS0618

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Covers the two modes of <see cref="CmsLocaleResolver"/>: static-list
/// passthrough, and CMS-derivation narrowed by the channel's declared
/// sites.
/// </summary>
public class CmsLocaleResolverTests
{
    [Fact]
    public void Resolve_StaticChannel_ReturnsLocalesUnchanged()
    {
        var channel = new SearchChannelBuilder("c")
            .DisplayName("X")
            .Locales("en", "da")
            .Build();

        var resolver = NewResolver(
            sites: Array.Empty<SiteDefinition>(),
            languages: Array.Empty<LanguageBranch>());

        resolver.Resolve(channel).Should().Equal("en", "da");
    }

    [Fact]
    public void Resolve_CmsChannelWithoutSites_ReturnsAllEnabledLanguages()
    {
        var channel = new SearchChannelBuilder("c")
            .DisplayName("X")
            .LocalesFromCmsLanguages()
            .Build();

        var resolver = NewResolver(
            sites: Array.Empty<SiteDefinition>(),
            languages: new[]
            {
                Branch("en"),
                Branch("sv"),
                Branch("fi"),
            });

        resolver.Resolve(channel).Should().BeEquivalentTo(new[] { "en", "sv", "fi" });
    }

    [Fact]
    public void Resolve_CmsChannelWithUnknownSite_ReturnsEmpty()
    {
        // Channel scoped to a site that doesn't exist — no overlap, so the
        // resolver returns empty rather than falling through to "all
        // enabled" (which would silently broaden the channel's reach).
        var channel = new SearchChannelBuilder("c")
            .DisplayName("X")
            .Sites("non-existent")
            .LocalesFromCmsLanguages()
            .Build();

        var resolver = NewResolver(
            sites: new[] { Site("corporate", hostLangs: new[] { "en" }) },
            languages: new[] { Branch("en"), Branch("sv") });

        resolver.Resolve(channel).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_CmsChannelScopedToSiteWithoutHostLanguages_FallsBackToAllEnabled()
    {
        // Single-host sites that don't declare per-language hostnames serve
        // every enabled branch. The resolver mirrors the existing
        // LanguageSiteEnumerator behaviour.
        var channel = new SearchChannelBuilder("c")
            .DisplayName("X")
            .Sites("corporate")
            .LocalesFromCmsLanguages()
            .Build();

        var resolver = NewResolver(
            sites: new[] { Site("corporate", hostLangs: Array.Empty<string>()) },
            languages: new[] { Branch("en"), Branch("sv") });

        resolver.Resolve(channel).Should().BeEquivalentTo(new[] { "en", "sv" });
    }

    [Fact]
    public void Resolve_CmsChannelScopedToSiteWithHostLanguages_FiltersToThoseLanguages()
    {
        var channel = new SearchChannelBuilder("c")
            .DisplayName("X")
            .Sites("corporate")
            .LocalesFromCmsLanguages()
            .Build();

        var resolver = NewResolver(
            sites: new[] { Site("corporate", hostLangs: new[] { "en", "fi" }) },
            languages: new[] { Branch("en"), Branch("sv"), Branch("fi") });

        resolver.Resolve(channel).Should().BeEquivalentTo(new[] { "en", "fi" });
    }

    // ── helpers ────────────────────────────────────────────────────────

    private static CmsLocaleResolver NewResolver(
        IReadOnlyList<SiteDefinition> sites,
        IReadOnlyList<LanguageBranch> languages)
    {
        var siteRepo = new Mock<ISiteDefinitionRepository>();
        siteRepo.Setup(s => s.List()).Returns(sites);

        var langRepo = new Mock<ILanguageBranchRepository>();
        // ListEnabled() returns IList<LanguageBranch>; pass a fresh List<>
        // copy so Moq's setup matches the runtime signature on both TFMs.
        langRepo.Setup(r => r.ListEnabled()).Returns(new List<LanguageBranch>(languages));

        return new CmsLocaleResolver(siteRepo.Object, langRepo.Object);
    }

    private static LanguageBranch Branch(string id)
        => new LanguageBranch(new System.Globalization.CultureInfo(id)) { Enabled = true };

    private static SiteDefinition Site(string name, string[] hostLangs)
    {
        var def = new SiteDefinition { Name = name };
        foreach (var lang in hostLangs)
        {
            def.Hosts.Add(new HostDefinition
            {
                Name = $"{name}-{lang}.example",
                Language = new System.Globalization.CultureInfo(lang)
            });
        }
        return def;
    }
}
