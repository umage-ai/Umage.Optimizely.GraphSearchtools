using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Tools.Channels;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Covers <see cref="ChannelsService"/>'s GraphQL-document inference helpers
/// that feed the Channel detail page's Ranking pill. These are the only
/// source for the pill since the addon stopped carrying configured defaults
/// on <c>SearchChannel</c>, so the null and missing-argument branches matter.
/// </summary>
public class ChannelsServiceInferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractRanking_ReturnsNullForMissingDocument(string? document)
    {
        ChannelsService.ExtractRanking(document).Should().BeNull();
    }

    [Fact]
    public void ExtractRanking_ReturnsNullWhenDocumentHasNoRankingArgument()
    {
        const string doc = """
            query SiteSearch($phrase: String!) {
              Content(where: { _fulltext: { match: $phrase } }) {
                items { Name }
              }
            }
            """;

        ChannelsService.ExtractRanking(doc).Should().BeNull();
    }

    [Fact]
    public void ExtractRanking_ReadsRankingFromOrderByBlock()
    {
        const string doc = """
            query SiteSearch($phrase: String!) {
              Content(
                where: { _fulltext: { match: $phrase } },
                orderBy: { _ranking: SEMANTIC, _minimumScore: 2.5 }
              ) {
                items { Name }
              }
            }
            """;

        ChannelsService.ExtractRanking(doc).Should().Be("SEMANTIC");
    }

    [Fact]
    public void ExtractRanking_FirstMatchWinsAcrossBranches()
    {
        // Mirrors the captured-representative-document shape where the
        // phrase placeholder has already been substituted so only the
        // active branch carries an orderBy with _ranking.
        const string doc = """
            orderBy: { _ranking: BOOST_ONLY }
            orderBy: { _ranking: SEMANTIC }
            """;

        ChannelsService.ExtractRanking(doc).Should().Be("BOOST_ONLY");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractSemanticWeight_ReturnsNullForMissingDocument(string? document)
    {
        ChannelsService.ExtractSemanticWeight(document).Should().BeNull();
    }

    [Fact]
    public void ExtractSemanticWeight_ReturnsNullWhenDocumentHasNoSemanticWeightArgument()
    {
        const string doc = "orderBy: { _ranking: SEMANTIC, _minimumScore: 2.5 }";

        ChannelsService.ExtractSemanticWeight(doc).Should().BeNull();
    }

    [Theory]
    [InlineData("orderBy: { _ranking: SEMANTIC, _semanticWeight: 0.3 }", 0.3)]
    [InlineData("orderBy: { _semanticWeight: 1 }", 1.0)]
    [InlineData("orderBy: { _semanticWeight: -0.5 }", -0.5)]
    public void ExtractSemanticWeight_ParsesNumberInInvariantCulture(string document, double expected)
    {
        ChannelsService.ExtractSemanticWeight(document).Should().BeApproximately(expected, 1e-9);
    }
}
