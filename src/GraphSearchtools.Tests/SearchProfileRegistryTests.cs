using EPiServer.DataAbstraction;
using EPiServer.Web;
using FluentAssertions;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

#pragma warning disable CS0618 // ISiteDefinitionRepository — see LanguageSiteEnumerator.

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Validates the <see cref="SearchProfileRegistry"/> contract: Generic always
/// present, key lookup includes both registered and Generic, ForSite filters
/// honour the empty-Sites-means-all rule from the design doc.
/// </summary>
public class SearchProfileRegistryTests
{
    [Fact]
    public void Registry_AlwaysIncludesGeneric()
    {
        var registry = BuildRegistry();
        registry.All.Should().ContainSingle()
            .Which.Key.Should().Be("generic");
    }

    [Fact]
    public void Registry_PutsRegisteredProfilesBeforeGeneric()
    {
        var registry = BuildRegistry(
            BuildProfile("site-search"),
            BuildProfile("kb-search"));

        registry.All.Select(p => p.Key)
            .Should().Equal("site-search", "kb-search", "generic");
    }

    [Fact]
    public void Registry_GetByKey_FindsRegistered_AndGeneric()
    {
        var site = BuildProfile("site-search");
        var registry = BuildRegistry(site);

        registry.Get("site-search").Should().BeSameAs(site);
        registry.Get("generic").Should().NotBeNull();
        registry.Get("generic")!.IsGeneric.Should().BeTrue();
        registry.Get("missing").Should().BeNull();
        registry.Get(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Registry_DropsHostRegisteredGenericProfile()
    {
        // Defensive: a host that managed to construct a profile with key "generic"
        // shouldn't be able to mask the synthesised one. The constructor filters them out.
        var hostGeneric = new SearchProfile
        {
            Key = "generic",
            DisplayName = LocalizedString.Literal("Host generic"),
            Sites = Array.Empty<string>(),
            Locales = Array.Empty<string>(),
            SearchedFields = Array.Empty<string>(),
            DefaultVariables = new Dictionary<string, object?>()
        };

        var registry = BuildRegistry(hostGeneric);
        registry.Get("generic")!.DisplayName.Value.Should().NotBe("Host generic");
    }

    [Fact]
    public void Registry_ForSite_FiltersByDeclaredSites_AndIncludesEmptyDeclarations()
    {
        var corp = BuildProfile("corp-search", sites: new[] { "corporate" });
        var support = BuildProfile("kb-search", sites: new[] { "support" });
        var shared = BuildProfile("shared-search"); // empty Sites = applies everywhere

        var registry = BuildRegistry(corp, support, shared);

        var corpProfiles = registry.ForSite("corporate").Select(p => p.Key).ToList();
        corpProfiles.Should().Contain("corp-search");
        corpProfiles.Should().Contain("shared-search");
        corpProfiles.Should().Contain("generic");
        corpProfiles.Should().NotContain("kb-search");

        var supportProfiles = registry.ForSite("support").Select(p => p.Key).ToList();
        supportProfiles.Should().Contain("kb-search");
        supportProfiles.Should().Contain("shared-search");
        supportProfiles.Should().NotContain("corp-search");

        // Site name lookup is case-insensitive (SiteDefinition.Name comparisons are).
        registry.ForSite("CORPORATE").Select(p => p.Key).Should().Contain("corp-search");
    }

    [Fact]
    public void Registry_ForSite_EmptyOrNullSiteName_OnlyReturnsAllSitesProfiles()
    {
        var corp = BuildProfile("corp-search", sites: new[] { "corporate" });
        var shared = BuildProfile("shared-search");

        var registry = BuildRegistry(corp, shared);

        var keys = registry.ForSite("").Select(p => p.Key).ToList();
        keys.Should().Contain("shared-search");
        keys.Should().Contain("generic");
        keys.Should().NotContain("corp-search");
    }

    private static SearchProfile BuildProfile(string key, string[]? sites = null, string[]? locales = null)
    {
        return new SearchProfileBuilder(key)
            .DisplayName(key)
            .Sites(sites ?? Array.Empty<string>())
            .Locales(locales ?? Array.Empty<string>())
            .Build();
    }

    private static SearchProfileRegistry BuildRegistry(params SearchProfile[] profiles)
    {
        var sites = new Mock<ISiteDefinitionRepository>(MockBehavior.Loose);
        sites.Setup(s => s.List()).Returns(Array.Empty<SiteDefinition>());

        var languages = new Mock<ILanguageBranchRepository>(MockBehavior.Loose);
        languages.Setup(l => l.ListEnabled()).Returns(Array.Empty<LanguageBranch>());

        return new SearchProfileRegistry(profiles, sites.Object, languages.Object);
    }
}
