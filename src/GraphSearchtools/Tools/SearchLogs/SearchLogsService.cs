using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;

/// <summary>
/// Read-side wrapper for the Phase 4 Wave 5 Search Logs UI. Owns three
/// contracts the controller leans on:
/// <list type="number">
///   <item>Default the time window to 24h when the caller omits <c>since</c>.</item>
///   <item>Clamp <c>take</c> to <c>[1, MaxTake]</c> so the UI can't pull more
///   rows than the reader is willing to materialise.</item>
///   <item>Project <see cref="PhraseAggregate"/> / <see cref="RawEvent"/> to
///   camelCase wire shapes so the Razor JS sees a stable surface.</item>
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

    private readonly ITelemetryReader _reader;

    public SearchLogsService(ITelemetryReader reader)
    {
        _reader = reader;
    }

    /// <summary>
    /// Top phrases by hit count in the window. Returns most-frequent first.
    /// When <paramref name="profileKey"/> is supplied, only sessions that ran
    /// against that profile are counted — the Profiles detail page passes the
    /// active profile key here.
    /// </summary>
    public async Task<IReadOnlyList<SearchLogPhraseRow>> TopPhrasesAsync(
        DateTime? since, int? take, string? profileKey = null, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(since, take, profileKey);
        var rows = await _reader.TopPhrasesAsync(query, cancellationToken);
        return rows.Select(ToPhraseRow).ToList();
    }

    /// <summary>
    /// Phrases whose sessions returned zero hits. Most-frequent first;
    /// these are the strongest synonym-mining candidates. Profile-scoped when
    /// <paramref name="profileKey"/> is supplied.
    /// </summary>
    public async Task<IReadOnlyList<SearchLogPhraseRow>> ZeroResultPhrasesAsync(
        DateTime? since, int? take, string? profileKey = null, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(since, take, profileKey);
        var rows = await _reader.ZeroResultPhrasesAsync(query, cancellationToken);
        return rows.Select(ToPhraseRow).ToList();
    }

    /// <summary>
    /// Phrases with the lowest click-through rate in the window. The reader
    /// excludes phrases with too few hits to score honestly.
    /// </summary>
    public async Task<IReadOnlyList<SearchLogPhraseRow>> LowCtrPhrasesAsync(
        DateTime? since, int? take, string? profileKey = null, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(since, take, profileKey);
        var rows = await _reader.LowCtrPhrasesAsync(query, cancellationToken);
        return rows.Select(ToPhraseRow).ToList();
    }

    /// <summary>
    /// Most-recent raw events from the per-instance forensic ring. Backs the
    /// live-tail card; the JS polls every 30s. Note: under the v0.5 aggregate-
    /// first design, this is a reservoir sample — events are persisted with
    /// uniform-random selection over the flush interval, not contiguously.
    /// Cross-node forensics is a non-goal; rows here come from one node's ring.
    /// </summary>
    public async Task<IReadOnlyList<SearchLogRawRow>> RecentEntriesAsync(
        DateTime? since, int? take, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(since, take, profileKey: null);
        var rows = await _reader.RecentRawAsync(query, cancellationToken);
        return rows.Select(ToRawRow).ToList();
    }

    /// <summary>
    /// Window + take normalisation. <paramref name="since"/> defaults to
    /// <see cref="DefaultWindow"/> ago; <paramref name="take"/> defaults to
    /// <see cref="DefaultTake"/> and is clamped to <c>[1, <see cref="MaxTake"/>]</c>.
    /// Future-dated <c>since</c> values are clamped to "now" so a clock-skewed
    /// caller can't accidentally request the empty set.
    /// </summary>
    internal static TelemetryQuery BuildQuery(DateTime? since, int? take, string? profileKey)
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
        var profile = string.IsNullOrWhiteSpace(profileKey) ? null : profileKey;
        return new TelemetryQuery(sinceUtc, now, clamped, profile);
    }

    private static SearchLogPhraseRow ToPhraseRow(PhraseAggregate row) => new()
    {
        Phrase = row.Phrase,
        Hits = row.Hits,
        ZeroResultRate = row.ZeroResultRate,
        Ctr = row.Ctr,
        Locale = row.Locale,
        ProfileKey = row.ProfileKey
    };

    private static SearchLogRawRow ToRawRow(RawEvent e) => new()
    {
        At = e.TimestampUtc,
        Kind = e.Kind,
        Phrase = e.Phrase,
        Locale = e.Locale,
        ProfileKey = e.ProfileKey,
        ResultCount = e.ResultCount,
        ClickRank = e.ClickRank,
        NodeId = e.NodeId
    };
}
