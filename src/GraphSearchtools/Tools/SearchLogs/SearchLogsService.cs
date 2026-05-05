using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

/// <summary>
/// Thin wrapper around the foundation <see cref="SearchLogService"/> for the
/// Phase 4 Wave 5 Search Logs UI. Owns three contracts the controller leans on:
/// <list type="number">
///   <item>Default the time window to 24h when the caller omits <c>since</c>.</item>
///   <item>Clamp <c>take</c> to <c>[1, MaxTake]</c> so the UI can't pull more
///   rows than DDS will happily aggregate in-process.</item>
///   <item>Project the DDS-side <see cref="SearchLogAggregateRow"/> /
///   <see cref="SearchLogEntry"/> to camelCase wire shapes so the Razor JS sees
///   a stable surface.</item>
/// </list>
/// All four list operations (top / zero-result / low-CTR / raw) flow through
/// this single service to keep the windowing and clamping policy in one place.
/// </summary>
public sealed class SearchLogsService
{
    /// <summary>Default page size when the caller omits <c>take</c>.</summary>
    public const int DefaultTake = 50;

    /// <summary>Inclusive upper bound for <c>take</c> on every endpoint.</summary>
    public const int MaxTake = 1000;

    /// <summary>Default <c>since</c> window when the caller omits the parameter.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    private readonly SearchLogService _logs;

    public SearchLogsService(SearchLogService logs)
    {
        _logs = logs;
    }

    /// <summary>
    /// Top phrases by hit count in the window. Returns most-frequent first.
    /// </summary>
    public IReadOnlyList<SearchLogPhraseRow> TopPhrases(DateTime? since, int? take)
    {
        var (sinceUtc, clampedTake) = Normalise(since, take);
        return _logs.TopPhrases(sinceUtc, clampedTake)
            .Select(ToPhraseRow)
            .ToList();
    }

    /// <summary>
    /// Phrases whose sessions all returned zero hits. Most-frequent first;
    /// these are the strongest synonym-mining candidates.
    /// </summary>
    public IReadOnlyList<SearchLogPhraseRow> ZeroResultPhrases(DateTime? since, int? take)
    {
        var (sinceUtc, clampedTake) = Normalise(since, take);
        return _logs.ZeroResultPhrases(sinceUtc, clampedTake)
            .Select(ToPhraseRow)
            .ToList();
    }

    /// <summary>
    /// Phrases with the lowest click-through rate in the window. The underlying
    /// service excludes phrases with fewer than 5 sessions to keep the list
    /// actionable.
    /// </summary>
    public IReadOnlyList<SearchLogPhraseRow> LowCtrPhrases(DateTime? since, int? take)
    {
        var (sinceUtc, clampedTake) = Normalise(since, take);
        return _logs.LowCtrPhrases(sinceUtc, clampedTake)
            .Select(ToPhraseRow)
            .ToList();
    }

    /// <summary>
    /// Most-recent raw entries in the window. Backs the live-tail card; the JS
    /// polls this every 30s.
    /// </summary>
    public IReadOnlyList<SearchLogRawRow> RecentEntries(DateTime? since, int? take)
    {
        var (sinceUtc, clampedTake) = Normalise(since, take);
        return _logs.ListSince(sinceUtc, clampedTake)
            .Select(ToRawRow)
            .ToList();
    }

    /// <summary>
    /// Window + take normalisation. <paramref name="since"/> defaults to
    /// <see cref="DefaultWindow"/> ago; <paramref name="take"/> defaults to
    /// <see cref="DefaultTake"/> and is clamped to <c>[1, <see cref="MaxTake"/>]</c>.
    /// Future-dated <c>since</c> values are clamped to "now" so a clock-skewed
    /// caller can't accidentally request the empty set.
    /// </summary>
    internal static (DateTime SinceUtc, int Take) Normalise(DateTime? since, int? take)
    {
        var now = DateTime.UtcNow;
        DateTime sinceUtc;
        if (since.HasValue)
        {
            var raw = since.Value.Kind == DateTimeKind.Utc
                ? since.Value
                : since.Value.ToUniversalTime();
            sinceUtc = raw > now ? now : raw;
        }
        else
        {
            sinceUtc = now - DefaultWindow;
        }

        var clamped = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        return (sinceUtc, clamped);
    }

    private static SearchLogPhraseRow ToPhraseRow(SearchLogAggregateRow row) => new()
    {
        Phrase = row.Phrase,
        Hits = row.Hits,
        ZeroResultRate = row.ZeroResultRate,
        Ctr = row.Ctr,
        Locale = row.Locale,
        ProfileKey = row.ProfileKey
    };

    private static SearchLogRawRow ToRawRow(SearchLogEntry e) => new()
    {
        At = e.At,
        Phrase = e.Phrase,
        Locale = e.Locale,
        Site = e.Site,
        ProfileKey = e.ProfileKey,
        ResultCount = e.ResultCount,
        TopResultRank = e.TopResultRank,
        TopResultId = e.TopResultId,
        DurationMs = e.DurationMs,
        Ranking = e.Ranking,
        Source = e.Source
    };
}
