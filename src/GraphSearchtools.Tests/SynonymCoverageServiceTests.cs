using FluentAssertions;
using Moq;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 Wave 5 — guards <see cref="SynonymCoverageService"/>'s join logic:
/// detect synonym entries that never fired against logged queries (prune
/// candidates), and surface zero-result phrases as suggested adds with a
/// closest-indexed-term hint when fuzzy matching succeeds.
/// </summary>
public class SynonymCoverageServiceTests
{
    [Fact]
    public static void ExtractTriggerTerms_ParsesEquivalentRule()
    {
        var terms = SynonymCoverageService.ExtractTriggerTerms("laptop, computer, pc");

        terms.Should().BeEquivalentTo(new[] { "laptop", "computer", "pc" });
    }

    [Fact]
    public static void ExtractTriggerTerms_ParsesReplacementRule_LhsOnly()
    {
        // The RHS of a replacement rule never appears in user queries, so the
        // analyzer must only regard the LHS as a trigger. Otherwise unused-
        // detection would flag every replacement rule whose RHS isn't queried.
        var terms = SynonymCoverageService.ExtractTriggerTerms("h2o => water, agua");

        terms.Should().BeEquivalentTo(new[] { "h2o" });
    }

    [Fact]
    public static void ExtractTriggerTerms_NormalisesWhitespaceAndCase()
    {
        var terms = SynonymCoverageService.ExtractTriggerTerms("  Warranty , Guarantee ");

        terms.Should().BeEquivalentTo(new[] { "warranty", "guarantee" });
    }

    [Fact]
    public async Task AnalyzeAsync_FlagsUnusedSynonymEntries()
    {
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        // Global synonym blob: one entry that *was* queried, one that wasn't.
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("warranty, guarantee\nfoo, bar");

        var logs = new InMemorySearchLogService();
        // "warranty" was queried; "foo"/"bar" never were.
        logs.Append(NewEntry("warranty", resultCount: 5, topResultRank: 1));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.UnusedEntries.Should().ContainSingle();
        result.UnusedEntries[0].Entry.Should().Be("foo, bar");
        result.UnusedEntries[0].Language.Should().Be("Global");
    }

    [Fact]
    public async Task AnalyzeAsync_ConsidersAnyTriggerTerm_AsActive()
    {
        // Partial coverage shouldn't pester editors: as long as at least one
        // trigger term in an equivalent rule was queried, the rule is "used".
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("laptop, computer, pc");

        var logs = new InMemorySearchLogService();
        logs.Append(NewEntry("computer", resultCount: 7, topResultRank: 2));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.UnusedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_SuggestsZeroResultPhrase_NotAlreadyCovered()
    {
        // No synonyms at all → every zero-result phrase is uncovered.
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var logs = new InMemorySearchLogService();
        // 5 zero-result hits for "warranties" — should bubble up.
        for (var i = 0; i < 5; i++) logs.Append(NewEntry("warranties", resultCount: 0));
        // A non-zero phrase shouldn't appear in suggested adds.
        logs.Append(NewEntry("warranty", resultCount: 12, topResultRank: 1));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.SuggestedAdds.Should().ContainSingle(s => s.Phrase == "warranties");
        result.SuggestedAdds.Single(s => s.Phrase == "warranties").Hits.Should().Be(5);
    }

    [Fact]
    public async Task AnalyzeAsync_FiltersOutZeroResultPhrasesAlreadyInSynonymSet()
    {
        // The phrase "warranty" is *both* queried with zero results AND already
        // a synonym trigger. The analyzer must filter it out of suggested adds
        // because the editor already addressed it.
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("warranty, guarantee");

        var logs = new InMemorySearchLogService();
        for (var i = 0; i < 3; i++) logs.Append(NewEntry("warranty", resultCount: 0));
        for (var i = 0; i < 5; i++) logs.Append(NewEntry("warranties", resultCount: 0));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.SuggestedAdds.Should().NotContain(s => s.Phrase == "warranty");
        result.SuggestedAdds.Should().Contain(s => s.Phrase == "warranties");
    }

    [Fact]
    public async Task AnalyzeAsync_AnnotatesSuggestedAdd_WithClosestIndexedTerm()
    {
        // "waranty" (typo) is one Levenshtein step from "warranty" (a top
        // phrase that returned results). The analyzer should pick up the
        // similarity and surface "warranty" as the closest indexed term.
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var logs = new InMemorySearchLogService();
        // The indexed-term sample is built from top phrases that *did* return
        // results — fuel that with several "warranty" sessions.
        for (var i = 0; i < 6; i++) logs.Append(NewEntry("warranty", resultCount: 4, topResultRank: 1));
        // The zero-result phrase the analyzer should annotate.
        for (var i = 0; i < 5; i++) logs.Append(NewEntry("waranty", resultCount: 0));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        var hit = result.SuggestedAdds.SingleOrDefault(s => s.Phrase == "waranty");
        hit.Should().NotBeNull("waranty is a zero-result phrase that should bubble up");
        hit!.ClosestIndexedTerm.Should().Be("warranty");
        hit.Similarity.Should().Be(1, "waranty → warranty is a single insertion");
    }

    [Fact]
    public async Task AnalyzeAsync_PopulatesGeneratedAt_AndLogsScanned()
    {
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var logs = new InMemorySearchLogService();
        logs.Append(NewEntry("a", resultCount: 0));
        logs.Append(NewEntry("b", resultCount: 0));

        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);
        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.WindowStart.Should().BeBefore(result.GeneratedAt);
        result.LogsScanned.Should().Be(2);
    }

    [Fact]
    public async Task AnalyzeAsync_HandlesEmptySynonymBlob_AndEmptyLogs_Gracefully()
    {
        var graph = new Mock<IGraphAdminClient>(MockBehavior.Loose);
        graph.Setup(g => g.GetSynonymsAsync(It.IsAny<SynonymsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var logs = new InMemorySearchLogService();
        var service = new SynonymCoverageService(new SynonymsService(graph.Object), logs);

        var result = await service.AnalyzeAsync(CancellationToken.None);

        result.UnusedEntries.Should().BeEmpty();
        result.SuggestedAdds.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Helpers
    // ──────────────────────────────────────────────────────────────────

    private static SearchLogEntry NewEntry(
        string phrase,
        DateTime? at = null,
        int resultCount = 1,
        int? topResultRank = null,
        string profileKey = "")
        => new()
        {
            At = at ?? DateTime.UtcNow,
            Phrase = phrase,
            Locale = "en",
            Site = "corporate",
            ProfileKey = profileKey,
            ResultCount = resultCount,
            TopResultRank = topResultRank,
            Source = "host-sdk"
        };

    /// <summary>
    /// Same in-memory SearchLogService shadow used in
    /// <see cref="SearchLogServiceTests"/>. Re-declared here so this test file
    /// is self-contained — DDS isn't reachable in xUnit.
    /// </summary>
    private sealed class InMemorySearchLogService : SearchLogService
    {
        private readonly List<SearchLogEntry> _rows = new();

        public override void Append(SearchLogEntry entry)
        {
            if (entry.At == default) entry.At = DateTime.UtcNow;
            _rows.Add(entry);
        }

        public override void AppendBatch(IEnumerable<SearchLogEntry> entries)
        {
            foreach (var e in entries) Append(e);
        }

        public override IEnumerable<SearchLogEntry> ListRecent(int take = 200)
            => _rows.OrderByDescending(e => e.At).Take(take).ToList();

        public override IEnumerable<SearchLogEntry> ListSince(DateTime sinceUtc, int take = 1000)
            => _rows.Where(e => e.At >= sinceUtc).OrderByDescending(e => e.At).Take(take).ToList();

        public override IEnumerable<SearchLogEntry> ListForProfile(string profileKey, int take = 200)
            => _rows.Where(e => e.ProfileKey == profileKey).OrderByDescending(e => e.At).Take(take).ToList();

        public override IEnumerable<SearchLogAggregateRow> TopPhrases(DateTime sinceUtc, int take = 50)
            => RunAggregate(sinceUtc, _ => true, ordered => ordered.OrderByDescending(r => r.Hits)).Take(take).ToList();

        public override IEnumerable<SearchLogAggregateRow> ZeroResultPhrases(DateTime sinceUtc, int take = 50)
            => RunAggregate(sinceUtc, e => e.ResultCount == 0, ordered => ordered.OrderByDescending(r => r.Hits)).Take(take).ToList();

        public override IEnumerable<SearchLogAggregateRow> LowCtrPhrases(DateTime sinceUtc, int take = 50)
            => RunAggregate(sinceUtc, _ => true, ordered => ordered
                    .Where(r => r.Hits >= 5)
                    .OrderBy(r => r.Ctr)
                    .ThenByDescending(r => r.Hits))
                .Take(take).ToList();

        private IEnumerable<SearchLogAggregateRow> RunAggregate(
            DateTime sinceUtc,
            Func<SearchLogEntry, bool> filter,
            Func<IEnumerable<SearchLogAggregateRow>, IEnumerable<SearchLogAggregateRow>> order)
        {
            var src = _rows.Where(e => e.At >= sinceUtc).Where(filter)
                .Where(e => !string.IsNullOrWhiteSpace(e.Phrase));
            var grouped = src
                .GroupBy(e => e.Phrase.Trim().ToLowerInvariant())
                .Select(g =>
                {
                    var bucket = g.ToList();
                    var hits = bucket.Count;
                    var zeros = bucket.Count(e => e.ResultCount == 0);
                    var clicks = bucket.Count(e => e.TopResultRank.HasValue && e.TopResultRank.Value >= 1 && e.TopResultRank.Value <= CtrRankCutoff);
                    var displayPhrase = bucket
                        .GroupBy(e => e.Phrase)
                        .OrderByDescending(pg => pg.Count())
                        .First().Key;
                    return new SearchLogAggregateRow(
                        Phrase: displayPhrase,
                        Hits: hits,
                        ZeroResultRate: hits == 0 ? 0d : (double)zeros / hits,
                        Ctr: hits == 0 ? 0d : (double)clicks / hits,
                        Locale: bucket.FirstOrDefault()?.Locale ?? string.Empty,
                        ProfileKey: bucket.FirstOrDefault()?.ProfileKey ?? string.Empty);
                });
            return order(grouped);
        }
    }
}
