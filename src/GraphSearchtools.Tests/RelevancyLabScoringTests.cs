using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 5 — pure-function coverage of the Relevancy Lab scorer. NDCG@10 and
/// MRR are purely a function of <c>(expected, actual)</c> so we don't need
/// DDS, HTTP, or even a service instance — these tests pin the maths.
/// </summary>
public class RelevancyLabScoringTests
{
    private const double Tolerance = 1e-9;

    // ──────────────────────────────────────────────────────────────────
    //   NDCG@10
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Ndcg_PerfectMatch_ScoresOne()
    {
        // Three expected hits with descending weights, returned in that exact
        // order — the scorer should report 1.0 (DCG == ideal DCG).
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 3 },
            new() { ContentLink = "b", Weight = 2 },
            new() { ContentLink = "c", Weight = 1 }
        };
        var actual = new List<string> { "a", "b", "c" };

        RelevancyLabService.ComputeNdcgAt10(expected, actual)
            .Should().BeApproximately(1.0, Tolerance);
    }

    [Fact]
    public void Ndcg_NoOverlap_ScoresZero()
    {
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 3 },
            new() { ContentLink = "b", Weight = 2 }
        };
        var actual = new List<string> { "x", "y", "z" };

        RelevancyLabService.ComputeNdcgAt10(expected, actual)
            .Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_PartialOverlap_DegradesByPosition()
    {
        // Expected ideal: a(3) at rank 1, b(2) at rank 2.
        //   ideal DCG = 3/log2(2) + 2/log2(3) = 3 + 2/1.5849... = 4.2618595...
        // Actual: a, x, b → rank 1 has weight 3, rank 3 has weight 2.
        //   DCG = 3/log2(2) + 0/log2(3) + 2/log2(4) = 3 + 0 + 1 = 4.0
        //   NDCG = 4.0 / 4.2618595... ≈ 0.93857
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 3 },
            new() { ContentLink = "b", Weight = 2 }
        };
        var actual = new List<string> { "a", "x", "b" };

        var idcg = 3.0 / Math.Log2(2) + 2.0 / Math.Log2(3);
        var dcg = 3.0 / Math.Log2(2) + 2.0 / Math.Log2(4);
        var expectedNdcg = dcg / idcg;

        RelevancyLabService.ComputeNdcgAt10(expected, actual)
            .Should().BeApproximately(expectedNdcg, Tolerance);
    }

    [Fact]
    public void Ndcg_RespectsKCutoff()
    {
        // Expected: a(3), b(2), c(1).
        // Actual K=2: a, b → DCG matches ideal at K=2 → score 1.0.
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 3 },
            new() { ContentLink = "b", Weight = 2 },
            new() { ContentLink = "c", Weight = 1 }
        };
        var actual = new List<string> { "a", "b" };

        RelevancyLabService.ComputeNdcgAtK(expected, actual, 2)
            .Should().BeApproximately(1.0, Tolerance);

        // Same actual at K=10 — still tops the K=2 ideal but the K=10 ideal
        // has all three, so it should be < 1.
        RelevancyLabService.ComputeNdcgAtK(expected, actual, 10)
            .Should().BeLessThan(1.0);
    }

    [Fact]
    public void Ndcg_EmptyExpected_ScoresZero()
    {
        var expected = new List<ExpectedHit>();
        var actual = new List<string> { "a", "b" };

        RelevancyLabService.ComputeNdcgAt10(expected, actual).Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_EmptyActual_ScoresZero()
    {
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 3 }
        };
        RelevancyLabService.ComputeNdcgAt10(expected, new List<string>()).Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_TruncatesActualBeyondK()
    {
        // Expected = {a(1)}.
        // Actual at K=10 with a hidden at position 11 — should NOT score it.
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 1 }
        };
        var actual = new List<string>
        {
            "x1","x2","x3","x4","x5","x6","x7","x8","x9","x10","a"
        };

        RelevancyLabService.ComputeNdcgAt10(expected, actual).Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_IgnoresZeroAndNegativeWeights()
    {
        // Zero / negative weights have no relevance and don't enter ideal DCG.
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 0 },
            new() { ContentLink = "b", Weight = -2 }
        };
        var actual = new List<string> { "a", "b" };

        RelevancyLabService.ComputeNdcgAt10(expected, actual).Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_KZeroOrNegative_ScoresZero()
    {
        var expected = new List<ExpectedHit> { new() { ContentLink = "a", Weight = 1 } };
        var actual = new List<string> { "a" };
        RelevancyLabService.ComputeNdcgAtK(expected, actual, 0).Should().Be(0.0);
        RelevancyLabService.ComputeNdcgAtK(expected, actual, -5).Should().Be(0.0);
    }

    [Fact]
    public void Ndcg_MatchingIsCaseInsensitive()
    {
        // GUID-form content links shouldn't fail to match because of casing.
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "AbC", Weight = 2 }
        };
        var actual = new List<string> { "abc" };

        RelevancyLabService.ComputeNdcgAt10(expected, actual)
            .Should().BeApproximately(1.0, Tolerance);
    }

    // ──────────────────────────────────────────────────────────────────
    //   MRR
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Mrr_FirstRank_IsOne()
    {
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 1 }
        };
        var actual = new List<string> { "a", "b", "c" };
        RelevancyLabService.ComputeMrr(expected, actual).Should().Be(1.0);
    }

    [Fact]
    public void Mrr_SecondRank_IsHalf()
    {
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 1 }
        };
        var actual = new List<string> { "x", "a", "b" };
        RelevancyLabService.ComputeMrr(expected, actual).Should().Be(0.5);
    }

    [Fact]
    public void Mrr_NoMatch_IsZero()
    {
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "a", Weight = 1 }
        };
        var actual = new List<string> { "x", "y", "z" };
        RelevancyLabService.ComputeMrr(expected, actual).Should().Be(0.0);
    }

    [Fact]
    public void Mrr_MultipleMatches_TakesFirstHitOnly()
    {
        // Both b and c are expected; b lands at rank 2, c at rank 3.
        // MRR uses the FIRST match → 1/2.
        var expected = new List<ExpectedHit>
        {
            new() { ContentLink = "b", Weight = 1 },
            new() { ContentLink = "c", Weight = 1 }
        };
        var actual = new List<string> { "x", "b", "c" };
        RelevancyLabService.ComputeMrr(expected, actual).Should().Be(0.5);
    }

    [Fact]
    public void Mrr_EmptyExpected_IsZero()
    {
        RelevancyLabService.ComputeMrr(new List<ExpectedHit>(), new List<string> { "a" })
            .Should().Be(0.0);
    }

    [Fact]
    public void Mrr_EmptyActual_IsZero()
    {
        var expected = new List<ExpectedHit> { new() { ContentLink = "a", Weight = 1 } };
        RelevancyLabService.ComputeMrr(expected, new List<string>()).Should().Be(0.0);
    }

    [Fact]
    public void Mrr_MatchingIsCaseInsensitive()
    {
        var expected = new List<ExpectedHit> { new() { ContentLink = "AbC", Weight = 1 } };
        var actual = new List<string> { "abc" };
        RelevancyLabService.ComputeMrr(expected, actual).Should().Be(1.0);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateSet_AcceptsCanonical()
    {
        var set = new GoldenSet
        {
            Name = "Top searches",
            Items = new()
            {
                new() { Phrase = "headphones", ExpectedTop = new() { new() { ContentLink = "g1", Weight = 3 } } }
            }
        };
        RelevancyLabService.ValidateSet(set).Should().BeNull();
    }

    [Fact]
    public void ValidateSet_RejectsMissingName()
    {
        var set = new GoldenSet { Name = "  ", Items = new() };
        RelevancyLabService.ValidateSet(set).Should().Contain("Name");
    }

    [Fact]
    public void ValidateSet_RejectsEmptyPhrase()
    {
        var set = new GoldenSet
        {
            Name = "x",
            Items = new()
            {
                new() { Phrase = "", ExpectedTop = new() }
            }
        };
        RelevancyLabService.ValidateSet(set).Should().Contain("phrase");
    }

    [Fact]
    public void ValidateSet_RejectsHitWithoutLink()
    {
        var set = new GoldenSet
        {
            Name = "x",
            Items = new()
            {
                new() { Phrase = "p", ExpectedTop = new() { new() { ContentLink = "", Weight = 1 } } }
            }
        };
        RelevancyLabService.ValidateSet(set).Should().Contain("contentLink");
    }

    // ──────────────────────────────────────────────────────────────────
    //   CSV export
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Csv_HasHeaderAndRows()
    {
        var run = new Run
        {
            PerQuery = new()
            {
                new()
                {
                    Phrase = "headphones",
                    Ndcg10 = 0.875,
                    Mrr = 0.5,
                    ActualTop = new() { "g1", "g2" }
                },
                new()
                {
                    Phrase = "comma, in phrase",
                    Ndcg10 = 0,
                    Mrr = 0,
                    ActualTop = new()
                }
            }
        };
        var csv = RelevancyLabService.ToCsv(run);

        csv.Should().StartWith("phrase,ndcg10,mrr,topResults");
        csv.Should().Contain("headphones,0.8750,0.5000,g1|g2");
        // Quote-escape the comma-bearing phrase.
        csv.Should().Contain("\"comma, in phrase\",0.0000,0.0000,");
    }
}
