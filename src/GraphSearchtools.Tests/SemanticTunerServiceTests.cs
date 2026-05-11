using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 3 — pure-function coverage of <see cref="SemanticTunerService"/>'s
/// validation and tier-pick logic. The DDS read/write paths are not exercised
/// here (they no-op outside an Optimizely host); the
/// <see cref="SemanticTunerApiControllerTests"/> guards the controller wiring.
/// </summary>
public class SemanticTunerServiceTests
{
    [Fact]
    public void TierForTokenCount_PicksTheMatchingRange()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 3, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };

        SemanticTunerService.TierForTokenCount(policy, 1)!.Ranking.Should().Be(GraphRanking.Relevance);
        SemanticTunerService.TierForTokenCount(policy, 2)!.Ranking.Should().Be(GraphRanking.Relevance);
        SemanticTunerService.TierForTokenCount(policy, 3)!.Ranking.Should().Be(GraphRanking.Semantic);
        SemanticTunerService.TierForTokenCount(policy, 99)!.Ranking.Should().Be(GraphRanking.Semantic);
    }

    [Fact]
    public void TierForTokenCount_ReturnsNull_WhenNoTierCovers()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 2, MaxTokens = 4, Ranking = GraphRanking.Semantic, SemanticWeight = 0.2 }
            }
        };

        SemanticTunerService.TierForTokenCount(policy, 0).Should().BeNull();
        SemanticTunerService.TierForTokenCount(policy, 1).Should().BeNull();
        SemanticTunerService.TierForTokenCount(policy, 5).Should().BeNull();
    }

    [Fact]
    public void TierForTokenCount_ReturnsNull_OnEmptyPolicy()
    {
        SemanticTunerService.TierForTokenCount(new SemanticTuningPolicy(), 1).Should().BeNull();
    }

    [Fact]
    public void TierForTokenCount_ReturnsNull_OnNegativeInput()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 0, MaxTokens = null, Ranking = GraphRanking.Relevance }
            }
        };
        SemanticTunerService.TierForTokenCount(policy, -1).Should().BeNull();
    }

    [Fact]
    public void Validate_AllowsEmptyPolicy()
    {
        SemanticTunerService.Validate(new SemanticTuningPolicy()).Should().BeNull();
    }

    [Fact]
    public void Validate_AllowsCanonicalTwoTierPolicy()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 3, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };

        SemanticTunerService.Validate(policy).Should().BeNull();
    }

    [Fact]
    public void Validate_RejectsOverlappingRanges()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = 3, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 },
                new() { MinTokens = 3, MaxTokens = 5, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 }
            }
        };

        SemanticTunerService.Validate(policy).Should().Contain("Overlapping");
    }

    [Fact]
    public void Validate_RejectsOpenEndedTierBeforeLast()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 0.3 },
                new() { MinTokens = 10, MaxTokens = 20, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 }
            }
        };

        SemanticTunerService.Validate(policy).Should().Contain("Open-ended");
    }

    [Fact]
    public void Validate_RejectsNegativeMinTokens()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = -1, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 }
            }
        };
        SemanticTunerService.Validate(policy).Should().Contain("MinTokens");
    }

    [Fact]
    public void Validate_RejectsMaxLessThanMin()
    {
        var policy = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 5, MaxTokens = 2, Ranking = GraphRanking.Relevance, SemanticWeight = 0.0 }
            }
        };
        SemanticTunerService.Validate(policy).Should().Contain("MaxTokens");
    }

    [Fact]
    public void Validate_RejectsWeightOutOfRange()
    {
        var tooHigh = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = 1.5 }
            }
        };
        SemanticTunerService.Validate(tooHigh).Should().Contain("SemanticWeight");

        var negative = new SemanticTuningPolicy
        {
            Tiers = new List<SemanticTier>
            {
                new() { MinTokens = 1, MaxTokens = null, Ranking = GraphRanking.Semantic, SemanticWeight = -0.1 }
            }
        };
        SemanticTunerService.Validate(negative).Should().Contain("SemanticWeight");
    }
}
