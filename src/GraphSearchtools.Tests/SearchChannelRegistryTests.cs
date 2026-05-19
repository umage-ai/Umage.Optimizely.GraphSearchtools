using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Validates the <see cref="SearchChannelRegistry"/> contract: key lookup
/// returns registered channels, ForSite filters honour the empty-Sites-
/// means-all rule from the design doc.
/// </summary>
public class SearchChannelRegistryTests
{
    [Fact]
    public void Registry_EmptyWhenNoChannelsRegistered()
    {
        var registry = BuildRegistry();
        registry.All.Should().BeEmpty();
    }

    [Fact]
    public void Registry_PreservesRegistrationOrder()
    {
        var registry = BuildRegistry(
            BuildChannel("site-search"),
            BuildChannel("kb-search"));

        registry.All.Select(p => p.Key)
            .Should().Equal("site-search", "kb-search");
    }

    [Fact]
    public void Registry_GetByKey_FindsRegistered()
    {
        var site = BuildChannel("site-search");
        var registry = BuildRegistry(site);

        registry.Get("site-search").Should().BeSameAs(site);
        registry.Get("missing").Should().BeNull();
        registry.Get(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Registry_ForSite_FiltersByDeclaredSites_AndIncludesEmptyDeclarations()
    {
        var corp = BuildChannel("corp-search", sites: new[] { "corporate" });
        var support = BuildChannel("kb-search", sites: new[] { "support" });
        var shared = BuildChannel("shared-search"); // empty Sites = applies everywhere

        var registry = BuildRegistry(corp, support, shared);

        var corpChannels = registry.ForSite("corporate").Select(p => p.Key).ToList();
        corpChannels.Should().Contain("corp-search");
        corpChannels.Should().Contain("shared-search");
        corpChannels.Should().NotContain("kb-search");

        var supportChannels = registry.ForSite("support").Select(p => p.Key).ToList();
        supportChannels.Should().Contain("kb-search");
        supportChannels.Should().Contain("shared-search");
        supportChannels.Should().NotContain("corp-search");

        // Site name lookup is case-insensitive (SiteDefinition.Name comparisons are).
        registry.ForSite("CORPORATE").Select(p => p.Key).Should().Contain("corp-search");
    }

    [Fact]
    public void Registry_ForSite_EmptyOrNullSiteName_OnlyReturnsAllSitesChannels()
    {
        var corp = BuildChannel("corp-search", sites: new[] { "corporate" });
        var shared = BuildChannel("shared-search");

        var registry = BuildRegistry(corp, shared);

        var keys = registry.ForSite("").Select(p => p.Key).ToList();
        keys.Should().Contain("shared-search");
        keys.Should().NotContain("corp-search");
    }

    private static SearchChannel BuildChannel(string key, string[]? sites = null, string[]? locales = null)
    {
        return new SearchChannelBuilder(key)
            .DisplayName(key)
            .Sites(sites ?? Array.Empty<string>())
            .Locales(locales ?? Array.Empty<string>())
            .Build();
    }

    private static SearchChannelRegistry BuildRegistry(params SearchChannel[] channels)
    {
        return new SearchChannelRegistry(channels);
    }
}
