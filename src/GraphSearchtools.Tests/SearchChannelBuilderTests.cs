using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Validates the fluent <see cref="SearchChannelBuilder"/>. Pinned cases:
/// key validation matches the design doc's <c>[a-z0-9-]+</c> contract; the
/// pinned-key template substitutes <c>{locale}</c> at call time.
/// </summary>
public class SearchChannelBuilderTests
{
    [Theory]
    [InlineData("Site-Search")]   // uppercase
    [InlineData("site_search")]    // underscore
    [InlineData("")]               // empty
    [InlineData("site search")]    // whitespace
    [InlineData("site/search")]    // slash
    public void Build_RejectsInvalidKey(string key)
    {
        var act = () => new SearchChannelBuilder(key)
            .DisplayName("X")
            .Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*[a-z0-9-]+*");
    }

    [Fact]
    public void Build_AcceptsValidKey_AndStoresFields()
    {
        var channel = new SearchChannelBuilder("site-search")
            .DisplayName("/graphsearchtools/channels/site-search/name")
            .Description("Header search")
            .Sites("corporate", "blog")
            .Locales("EN", "da", "sv")
            .SearchedFields("Name", "TeaserText")
            .UsesPinnedKey("site-{locale}")
            .GraphQLDocument("Queries/SiteSearch.graphql")
            .Variables(new { limit = 20, contentType = "Article" })
            .Build();

        channel.Key.Should().Be("site-search");
        channel.DisplayName.IsKey.Should().BeTrue();
        channel.DisplayName.Value.Should().Be("/graphsearchtools/channels/site-search/name");
        channel.Description!.IsKey.Should().BeFalse();
        channel.Description.Value.Should().Be("Header search");
        channel.Sites.Should().Equal("corporate", "blog");
        channel.Locales.Should().Equal("en", "da", "sv");
        channel.SearchedFields.Should().Equal("Name", "TeaserText");
        channel.PinnedKeyForLocale.Should().NotBeNull();
        channel.GraphQLDocumentPath.Should().Be("Queries/SiteSearch.graphql");
        channel.DefaultVariables.Should().ContainKey("limit").WhoseValue.Should().Be(20);
        channel.DefaultVariables.Should().ContainKey("contentType").WhoseValue.Should().Be("Article");
    }

    [Fact]
    public void UsesPinnedKey_StringTemplate_SubstitutesLocale()
    {
        var channel = new SearchChannelBuilder("site-search")
            .DisplayName("X")
            .UsesPinnedKey("site-{locale}")
            .Build();

        channel.PinnedKeyForLocale.Should().NotBeNull();
        channel.PinnedKeyForLocale!("en").Should().Be("site-en");
        channel.PinnedKeyForLocale("da").Should().Be("site-da");
    }

    [Fact]
    public void UsesPinnedKey_ConstantTemplate_ReturnsSameValueForEveryLocale()
    {
        var channel = new SearchChannelBuilder("kb-search")
            .DisplayName("X")
            .UsesPinnedKey("kb")
            .Build();

        channel.PinnedKeyForLocale!("en").Should().Be("kb");
        channel.PinnedKeyForLocale("da").Should().Be("kb");
    }

    [Fact]
    public void UsesPinnedKey_LambdaForm_IsHonoured()
    {
        var channel = new SearchChannelBuilder("multi-site")
            .DisplayName("X")
            .UsesPinnedKey(locale => $"corp-{locale}-pinned")
            .Build();

        channel.PinnedKeyForLocale!("sv").Should().Be("corp-sv-pinned");
    }

    [Fact]
    public void DisplayName_ImplicitConversion_TreatsLeadingSlashAsKey()
    {
        LocalizedString asKey = "/graphsearchtools/channels/x/name";
        LocalizedString asLiteral = "Plain label";

        asKey.IsKey.Should().BeTrue();
        asLiteral.IsKey.Should().BeFalse();
    }

    [Fact]
    public void LocalesFromCmsLanguages_SetsFlag_AndClearsStaticList()
    {
        var channel = new SearchChannelBuilder("p")
            .DisplayName("X")
            .Locales("en", "sv")          // explicit first…
            .LocalesFromCmsLanguages()    // …then opt into CMS derivation
            .Build();

        channel.LocalesFromCmsLanguages.Should().BeTrue();
        channel.Locales.Should().BeEmpty(
            "the CMS-derivation flag wins — keeping the static list would mask which mode is active");
    }

    [Fact]
    public void Locales_AfterLocalesFromCmsLanguages_RevertsToExplicit()
    {
        // Last call wins: a developer flips to CMS, then changes their mind
        // and pins back to an explicit list.
        var channel = new SearchChannelBuilder("p")
            .DisplayName("X")
            .LocalesFromCmsLanguages()
            .Locales("en", "sv")
            .Build();

        channel.LocalesFromCmsLanguages.Should().BeFalse();
        channel.Locales.Should().Equal("en", "sv");
    }
}
