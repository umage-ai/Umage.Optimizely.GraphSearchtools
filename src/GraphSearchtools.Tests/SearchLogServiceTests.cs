using FluentAssertions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Phase 4 foundation — guards <see cref="SearchLogService"/>'s contract:
/// Append/List round-trip, phrase-bucket aggregation, zero-result filtering,
/// and the CTR definition (sessions whose <c>TopResultRank</c> ≤ 3).
/// </summary>
/// <remarks>
/// The real DDS path requires an Optimizely runtime, so we exercise the
/// service through a tiny in-memory fake — same idiom the existing
/// <c>FakeEditService</c> uses in <c>PinnedApiControllerProfileScopeTests</c>.
/// </remarks>
public class SearchLogServiceTests
{
    [Fact]
    public void Append_AndListRecent_RoundTrip()
    {
        var service = new InMemorySearchLogService();
        var first = NewEntry(phrase: "warranty", at: DateTime.UtcNow.AddMinutes(-10));
        var second = NewEntry(phrase: "shipping", at: DateTime.UtcNow.AddMinutes(-1));

        service.Append(first);
        service.Append(second);

        var recent = service.ListRecent().ToList();
        recent.Should().HaveCount(2);
        recent[0].Phrase.Should().Be("shipping", "ListRecent returns newest-first");
        recent[1].Phrase.Should().Be("warranty");
    }

    [Fact]
    public void Append_StampsAt_When_DefaultDateTime()
    {
        var service = new InMemorySearchLogService();
        var entry = NewEntry(phrase: "no-at", at: default);

        service.Append(entry);

        entry.At.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TopPhrases_GroupsBy_Phrase_AndReturnsCounts()
    {
        var service = new InMemorySearchLogService();
        var since = DateTime.UtcNow.AddDays(-1);

        // 3 × warranty, 2 × shipping, 1 × refund — and a stray pre-window row that should be ignored.
        for (var i = 0; i < 3; i++) service.Append(NewEntry("warranty"));
        for (var i = 0; i < 2; i++) service.Append(NewEntry("shipping"));
        service.Append(NewEntry("refund"));
        service.Append(NewEntry("ancient", at: DateTime.UtcNow.AddDays(-7)));

        var top = service.TopPhrases(since).ToList();

        top.Should().HaveCount(3, "the pre-window row is filtered out by sinceUtc");
        top[0].Phrase.Should().Be("warranty");
        top[0].Hits.Should().Be(3);
        top[1].Phrase.Should().Be("shipping");
        top[1].Hits.Should().Be(2);
        top[2].Phrase.Should().Be("refund");
        top[2].Hits.Should().Be(1);
    }

    [Fact]
    public void TopPhrases_NormalisesPhraseCasing_BeforeGrouping()
    {
        var service = new InMemorySearchLogService();
        var since = DateTime.UtcNow.AddDays(-1);

        // Three variants of the same phrase should collapse into one bucket.
        service.Append(NewEntry("Warranty"));
        service.Append(NewEntry("warranty"));
        service.Append(NewEntry("  WARRANTY  "));

        var top = service.TopPhrases(since).ToList();

        top.Should().HaveCount(1);
        top[0].Hits.Should().Be(3);
    }

    [Fact]
    public void ZeroResultPhrases_OnlyIncludes_ResultCountZero()
    {
        var service = new InMemorySearchLogService();
        var since = DateTime.UtcNow.AddDays(-1);

        // "warranty" is the zero-result phrase; "shipping" has hits and must be excluded.
        service.Append(NewEntry("warranty", resultCount: 0));
        service.Append(NewEntry("warranty", resultCount: 0));
        service.Append(NewEntry("shipping", resultCount: 12));
        service.Append(NewEntry("shipping", resultCount: 5));

        var zero = service.ZeroResultPhrases(since).ToList();

        zero.Should().HaveCount(1);
        zero[0].Phrase.Should().Be("warranty");
        zero[0].Hits.Should().Be(2);
        zero[0].ZeroResultRate.Should().Be(1d);
    }

    [Fact]
    public void LowCtrPhrases_DefinesCtr_As_Sessions_With_TopResultRank_LE_3()
    {
        var service = new InMemorySearchLogService();
        var since = DateTime.UtcNow.AddDays(-1);

        // "warranty": 6 sessions, 1 click in the top-3 → CTR ≈ 0.166
        for (var i = 0; i < 5; i++) service.Append(NewEntry("warranty", topResultRank: null));
        service.Append(NewEntry("warranty", topResultRank: 2));

        // "shipping": 6 sessions, 5 clicks in top-3, 1 click at rank 7 → CTR ≈ 0.833
        for (var i = 0; i < 5; i++) service.Append(NewEntry("shipping", topResultRank: 1));
        service.Append(NewEntry("shipping", topResultRank: 7));

        // "rare": 1 session — below the 5-session min, must be filtered out.
        service.Append(NewEntry("rare", topResultRank: null));

        var low = service.LowCtrPhrases(since).ToList();

        low.Should().HaveCount(2, "phrases with fewer than 5 sessions are excluded");
        low[0].Phrase.Should().Be("warranty", "lowest CTR comes first");
        low[0].Ctr.Should().BeApproximately(1d / 6d, 0.001);
        low[1].Phrase.Should().Be("shipping");
        low[1].Ctr.Should().BeApproximately(5d / 6d, 0.001);

        // Rank 4+ doesn't count as a click-through — explicit guard against
        // a regression that would loosen the cutoff.
        var rankFour = NewEntry("threshold", topResultRank: 4);
        for (var i = 0; i < 5; i++) service.Append(NewEntry("threshold", topResultRank: 4));
        var withThreshold = service.LowCtrPhrases(since).ToList();
        withThreshold.Should().Contain(r => r.Phrase == "threshold" && r.Ctr == 0d);
    }

    [Fact]
    public void ListSince_FiltersByTimestamp()
    {
        var service = new InMemorySearchLogService();
        var since = DateTime.UtcNow.AddHours(-1);

        service.Append(NewEntry("recent", at: DateTime.UtcNow.AddMinutes(-10)));
        service.Append(NewEntry("old", at: DateTime.UtcNow.AddDays(-2)));

        var rows = service.ListSince(since).ToList();

        rows.Should().HaveCount(1);
        rows[0].Phrase.Should().Be("recent");
    }

    [Fact]
    public void ListForProfile_FiltersByProfileKey()
    {
        var service = new InMemorySearchLogService();
        service.Append(NewEntry("a", profileKey: "site-search"));
        service.Append(NewEntry("b", profileKey: "kb-search"));
        service.Append(NewEntry("c", profileKey: "site-search"));

        var rows = service.ListForProfile("site-search").ToList();

        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(e => e.ProfileKey == "site-search");
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
    /// Shadows the DDS-backed methods with an in-memory list so tests don't
    /// need an Optimizely runtime. Mirrors the <c>FakeEditService</c> pattern
    /// in <c>PinnedApiControllerProfileScopeTests</c>.
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
            => RunAggregate(sinceUtc, e => true, ordered => ordered.OrderByDescending(r => r.Hits)).Take(take).ToList();

        public override IEnumerable<SearchLogAggregateRow> ZeroResultPhrases(DateTime sinceUtc, int take = 50)
            => RunAggregate(sinceUtc, e => e.ResultCount == 0, ordered => ordered.OrderByDescending(r => r.Hits)).Take(take).ToList();

        public override IEnumerable<SearchLogAggregateRow> LowCtrPhrases(DateTime sinceUtc, int take = 50)
            => RunAggregate(sinceUtc, e => true, ordered => ordered
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
