using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Validates the fluent <see cref="SearchProfileBuilder"/>. Pinned cases:
/// key validation matches the design doc's <c>[a-z0-9-]+</c> contract; the
/// pinned-key template substitutes <c>{locale}</c> at call time.
/// </summary>
public class SearchProfileBuilderTests
{
    [Theory]
    [InlineData("Site-Search")]   // uppercase
    [InlineData("site_search")]    // underscore
    [InlineData("")]               // empty
    [InlineData("site search")]    // whitespace
    [InlineData("site/search")]    // slash
    public void Build_RejectsInvalidKey(string key)
    {
        var act = () => new SearchProfileBuilder(key)
            .DisplayName("X")
            .Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*[a-z0-9-]+*");
    }

    [Fact]
    public void Build_AcceptsValidKey_AndStoresFields()
    {
        var profile = new SearchProfileBuilder("site-search")
            .DisplayName("/graphsearchtools/profiles/site-search/name")
            .Description("Header search")
            .Sites("corporate", "blog")
            .Locales("EN", "da", "sv")
            .SearchedFields("Name", "TeaserText")
            .UsesPinnedKey("site-{locale}")
            .SemanticBlend(0.4, GraphRanking.Semantic)
            .GraphQLDocument("Queries/SiteSearch.graphql")
            .Variables(new { limit = 20, contentType = "Article" })
            .Build();

        profile.Key.Should().Be("site-search");
        profile.DisplayName.IsKey.Should().BeTrue();
        profile.DisplayName.Value.Should().Be("/graphsearchtools/profiles/site-search/name");
        profile.Description!.IsKey.Should().BeFalse();
        profile.Description.Value.Should().Be("Header search");
        profile.Sites.Should().Equal("corporate", "blog");
        profile.Locales.Should().Equal("en", "da", "sv");
        profile.SearchedFields.Should().Equal("Name", "TeaserText");
        profile.PinnedKeyForLocale.Should().NotBeNull();
        profile.SemanticWeight.Should().BeApproximately(0.4, 1e-9);
        profile.Ranking.Should().Be(GraphRanking.Semantic);
        profile.GraphQLDocumentPath.Should().Be("Queries/SiteSearch.graphql");
        profile.DefaultVariables.Should().ContainKey("limit").WhoseValue.Should().Be(20);
        profile.DefaultVariables.Should().ContainKey("contentType").WhoseValue.Should().Be("Article");
    }

    [Fact]
    public void UsesPinnedKey_StringTemplate_SubstitutesLocale()
    {
        var profile = new SearchProfileBuilder("site-search")
            .DisplayName("X")
            .UsesPinnedKey("site-{locale}")
            .Build();

        profile.PinnedKeyForLocale.Should().NotBeNull();
        profile.PinnedKeyForLocale!("en").Should().Be("site-en");
        profile.PinnedKeyForLocale("da").Should().Be("site-da");
    }

    [Fact]
    public void UsesPinnedKey_ConstantTemplate_ReturnsSameValueForEveryLocale()
    {
        var profile = new SearchProfileBuilder("kb-search")
            .DisplayName("X")
            .UsesPinnedKey("kb")
            .Build();

        profile.PinnedKeyForLocale!("en").Should().Be("kb");
        profile.PinnedKeyForLocale("da").Should().Be("kb");
    }

    [Fact]
    public void UsesPinnedKey_LambdaForm_IsHonoured()
    {
        var profile = new SearchProfileBuilder("multi-site")
            .DisplayName("X")
            .UsesPinnedKey(locale => $"corp-{locale}-pinned")
            .Build();

        profile.PinnedKeyForLocale!("sv").Should().Be("corp-sv-pinned");
    }

    [Fact]
    public void DisplayName_ImplicitConversion_TreatsLeadingSlashAsKey()
    {
        LocalizedString asKey = "/graphsearchtools/profiles/x/name";
        LocalizedString asLiteral = "Plain label";

        asKey.IsKey.Should().BeTrue();
        asLiteral.IsKey.Should().BeFalse();
    }

    [Fact]
    public void SemanticBlend_ClampsWeightToValidRange()
    {
        var profile = new SearchProfileBuilder("p")
            .DisplayName("X")
            .SemanticBlend(2.5, GraphRanking.Semantic)
            .Build();

        profile.SemanticWeight.Should().Be(1.0);
    }
}
