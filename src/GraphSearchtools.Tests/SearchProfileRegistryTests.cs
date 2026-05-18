using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Validates the <see cref="SearchProfileRegistry"/> contract: key lookup
/// returns registered profiles, ForSite filters honour the empty-Sites-
/// means-all rule from the design doc.
/// </summary>
public class SearchProfileRegistryTests
{
    [Fact]
    public void Registry_EmptyWhenNoProfilesRegistered()
    {
        var registry = BuildRegistry();
        registry.All.Should().BeEmpty();
    }

    [Fact]
    public void Registry_PreservesRegistrationOrder()
    {
        var registry = BuildRegistry(
            BuildProfile("site-search"),
            BuildProfile("kb-search"));

        registry.All.Select(p => p.Key)
            .Should().Equal("site-search", "kb-search");
    }

    [Fact]
    public void Registry_GetByKey_FindsRegistered()
    {
        var site = BuildProfile("site-search");
        var registry = BuildRegistry(site);

        registry.Get("site-search").Should().BeSameAs(site);
        registry.Get("missing").Should().BeNull();
        registry.Get(string.Empty).Should().BeNull();
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
        return new SearchProfileRegistry(profiles);
    }
}
