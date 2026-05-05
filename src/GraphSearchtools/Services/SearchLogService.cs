using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// One row of phrase-level aggregation surfaced by <see cref="SearchLogService"/>.
/// Returned by <see cref="SearchLogService.TopPhrases"/>,
/// <see cref="SearchLogService.ZeroResultPhrases"/>, and
/// <see cref="SearchLogService.LowCtrPhrases"/>; the Phase 4 Search Logs UI
/// renders these directly.
/// </summary>
/// <param name="Phrase">The grouped-by phrase (raw, as logged).</param>
/// <param name="Hits">Total session count for the phrase in the window.</param>
/// <param name="ZeroResultRate">Fraction of sessions whose <c>ResultCount</c> was 0; in <c>[0, 1]</c>.</param>
/// <param name="Ctr">
/// Click-through rate — fraction of sessions whose <c>TopResultRank</c> ≤ 3.
/// In <c>[0, 1]</c>. Sessions with no rank at all (Graph-poll rows) count as
/// "no click" in the denominator.
/// </param>
/// <param name="Locale">Most common locale among the sessions, or empty.</param>
/// <param name="ProfileKey">Most common profile key among the sessions, or empty.</param>
public record SearchLogAggregateRow(
    string Phrase,
    int Hits,
    double ZeroResultRate,
    double Ctr,
    string Locale,
    string ProfileKey);

/// <summary>
/// Append-and-read access to the <see cref="SearchLogEntry"/> DDS table.
/// Foundation service — Phase 4 Wave 5 tools (Search Logs UI, Pinned Result
/// Coverage, Synonym Coverage) read from this; the
/// <c>TelemetryApiController</c> writes to it.
/// </summary>
/// <remarks>
/// DDS is process-global (singleton factory) so this service is registered as
/// a singleton too. Methods are tolerant of the store not being available
/// (running outside an Optimizely host) — they degrade to no-op / empty rather
/// than throwing, mirroring <see cref="SearchProfileEditService"/>.
///
/// Aggregations are computed in-memory: pull all rows since
/// <c>sinceUtc</c> from DDS, then group/count in LINQ. DDS doesn't support
/// SQL-style group-by, and the Phase 4 UIs only need ~50-row aggregate cards,
/// not row-level paging.
/// </remarks>
public class SearchLogService
{
    /// <summary>
    /// CTR rank cutoff — a session counts as a "click-through" if its
    /// <c>TopResultRank</c> falls in <c>[1, 3]</c>. Defined here as a constant
    /// so tests can refer to it without re-encoding the policy.
    /// </summary>
    public const int CtrRankCutoff = 3;

    /// <summary>Append a single entry. No-op when DDS is unavailable.</summary>
    public virtual void Append(SearchLogEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (entry.At == default) entry.At = DateTime.UtcNow;

        var store = TryGetStore();
        if (store == null) return;
        store.Save(entry);
    }

    /// <summary>Append a batch of entries. No-op when DDS is unavailable.</summary>
    public virtual void AppendBatch(IEnumerable<SearchLogEntry> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        var store = TryGetStore();
        if (store == null) return;

        foreach (var entry in entries)
        {
            if (entry == null) continue;
            if (entry.At == default) entry.At = DateTime.UtcNow;
            store.Save(entry);
        }
    }

    /// <summary>
    /// Newest-first list of recent entries. <paramref name="take"/> is clamped
    /// to <c>[1, 5000]</c>; default 200.
    /// </summary>
    public virtual IEnumerable<SearchLogEntry> ListRecent(int take = 200)
    {
        var store = TryGetStore();
        if (store == null) return Array.Empty<SearchLogEntry>();

        var clamped = Math.Clamp(take, 1, 5000);
        return store.Items<SearchLogEntry>()
            .OrderByDescending(e => e.At)
            .Take(clamped)
            .ToList();
    }

    /// <summary>
    /// Newest-first list of entries with <c>At &gt;= sinceUtc</c>.
    /// <paramref name="take"/> is clamped to <c>[1, 50000]</c>; default 1000.
    /// </summary>
    public virtual IEnumerable<SearchLogEntry> ListSince(DateTime sinceUtc, int take = 1000)
    {
        var store = TryGetStore();
        if (store == null) return Array.Empty<SearchLogEntry>();

        var clamped = Math.Clamp(take, 1, 50000);
        return store.Items<SearchLogEntry>()
            .Where(e => e.At >= sinceUtc)
            .OrderByDescending(e => e.At)
            .Take(clamped)
            .ToList();
    }

    /// <summary>
    /// Newest-first list of entries for a specific profile key.
    /// <paramref name="take"/> is clamped to <c>[1, 5000]</c>; default 200.
    /// </summary>
    public virtual IEnumerable<SearchLogEntry> ListForProfile(string profileKey, int take = 200)
    {
        if (string.IsNullOrEmpty(profileKey)) return Array.Empty<SearchLogEntry>();

        var store = TryGetStore();
        if (store == null) return Array.Empty<SearchLogEntry>();

        var clamped = Math.Clamp(take, 1, 5000);
        return store.Items<SearchLogEntry>()
            .Where(e => e.ProfileKey == profileKey)
            .OrderByDescending(e => e.At)
            .Take(clamped)
            .ToList();
    }

    /// <summary>
    /// Top phrases by hit count in the window. Most-frequent first.
    /// <paramref name="take"/> is clamped to <c>[1, 500]</c>; default 50.
    /// </summary>
    public virtual IEnumerable<SearchLogAggregateRow> TopPhrases(DateTime sinceUtc, int take = 50)
    {
        var rows = LoadWindow(sinceUtc);
        return Aggregate(rows)
            .OrderByDescending(r => r.Hits)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();
    }

    /// <summary>
    /// Phrases whose sessions all had <c>ResultCount == 0</c>. Most-frequent
    /// first. Phase 4 Synonym Coverage uses this list to suggest missing
    /// synonyms.
    /// </summary>
    public virtual IEnumerable<SearchLogAggregateRow> ZeroResultPhrases(DateTime sinceUtc, int take = 50)
    {
        var rows = LoadWindow(sinceUtc).Where(e => e.ResultCount == 0);
        return Aggregate(rows)
            .OrderByDescending(r => r.Hits)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();
    }

    /// <summary>
    /// Phrases ranked by lowest CTR (sessions with <c>TopResultRank ≤ 3</c>
    /// over total sessions). Phrases with fewer than 5 sessions are excluded —
    /// their CTR is too noisy to act on. Phase 4 Pinned Result Coverage uses
    /// this list to flag pins that aren't earning clicks.
    /// </summary>
    public virtual IEnumerable<SearchLogAggregateRow> LowCtrPhrases(DateTime sinceUtc, int take = 50)
    {
        const int minSessions = 5;
        var rows = LoadWindow(sinceUtc);
        return Aggregate(rows)
            .Where(r => r.Hits >= minSessions)
            .OrderBy(r => r.Ctr)
            .ThenByDescending(r => r.Hits)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();
    }

    /// <summary>Load all rows in the window — single DDS query for all aggregations.</summary>
    private List<SearchLogEntry> LoadWindow(DateTime sinceUtc)
    {
        var store = TryGetStore();
        if (store == null) return new List<SearchLogEntry>();

        return store.Items<SearchLogEntry>()
            .Where(e => e.At >= sinceUtc)
            .ToList();
    }

    /// <summary>
    /// Group by phrase (case-insensitive, trimmed) and produce
    /// <see cref="SearchLogAggregateRow"/>. The grouping key is normalised so
    /// "Warranty" / " warranty" / "warranty" collapse into one row, but the
    /// row's <c>Phrase</c> is the most-common original-cased form.
    /// </summary>
    private static IEnumerable<SearchLogAggregateRow> Aggregate(IEnumerable<SearchLogEntry> rows)
    {
        return rows
            .Where(e => !string.IsNullOrWhiteSpace(e.Phrase))
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

                var locale = bucket
                    .Where(e => !string.IsNullOrEmpty(e.Locale))
                    .GroupBy(e => e.Locale)
                    .OrderByDescending(lg => lg.Count())
                    .Select(lg => lg.Key)
                    .FirstOrDefault() ?? string.Empty;

                var profileKey = bucket
                    .Where(e => !string.IsNullOrEmpty(e.ProfileKey))
                    .GroupBy(e => e.ProfileKey)
                    .OrderByDescending(pg => pg.Count())
                    .Select(pg => pg.Key)
                    .FirstOrDefault() ?? string.Empty;

                return new SearchLogAggregateRow(
                    Phrase: displayPhrase,
                    Hits: hits,
                    ZeroResultRate: hits == 0 ? 0d : (double)zeros / hits,
                    Ctr: hits == 0 ? 0d : (double)clicks / hits,
                    Locale: locale,
                    ProfileKey: profileKey);
            });
    }

    private static DynamicDataStore? TryGetStore()
    {
        try
        {
            return DynamicDataStoreFactory.Instance?.GetStore(typeof(SearchLogEntry))
                ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(SearchLogEntry));
        }
        catch
        {
            return null;
        }
    }
}
