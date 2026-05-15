using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Insights.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.SearchLogs;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Insights;

/// <summary>
/// Aggregation surface for the Insights dashboard. Reuses
/// <see cref="SearchLogsService"/> for the phrase/zero-result lanes,
/// <see cref="SynonymCoverageService"/> for the synonym-coverage rollup, and
/// <see cref="SearchProfileEditService"/> for the activity strip — no new DB
/// schema. The Insights surface is read-only; everything here is a join over
/// data the other tools already maintain.
/// </summary>
public sealed class InsightsService
{
    /// <summary>
    /// Default window for the top-phrases / zero-result panels (7 days). The
    /// JS toggle exposes 30 days too.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    private readonly SearchLogsService _logs;
    private readonly SynonymCoverageService _synonymCoverage;
    private readonly SearchProfileEditService _editLog;

    public InsightsService(
        SearchLogsService logs,
        SynonymCoverageService synonymCoverage,
        SearchProfileEditService editLog)
    {
        _logs = logs;
        _synonymCoverage = synonymCoverage;
        _editLog = editLog;
    }

    /// <summary>
    /// Top phrases in the window, with a derived <c>zeroResults</c> count so
    /// the same row can show "people search for this" and "and this fraction
    /// found nothing" at once. <paramref name="days"/> is clamped to
    /// <c>[1, 90]</c>; the JS picker only sends 7 or 30.
    /// </summary>
    public async Task<IReadOnlyList<InsightsPhraseRow>> TopPhrasesAsync(
        int days,
        int take,
        string? profileKey = null,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(Math.Clamp(days, 1, 90));
        var rows = await _logs.TopPhrasesAsync(since, take, profileKey, locale, cancellationToken);
        return rows.Select(r => new InsightsPhraseRow
        {
            Phrase = r.Phrase,
            Count = r.Hits,
            // ZeroResultRate is a fraction in [0,1]; the wire count is
            // a long so big windows don't overflow Int32.
            ZeroResults = (long)Math.Round(r.Hits * r.ZeroResultRate),
            Locale = r.Locale,
            ProfileKey = r.ProfileKey
        }).ToList();
    }

    /// <summary>
    /// Zero-result phrases in the window — the pin-candidate list.
    /// </summary>
    public async Task<IReadOnlyList<InsightsZeroResultRow>> ZeroResultPhrasesAsync(
        int days,
        int take,
        string? profileKey = null,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow - TimeSpan.FromDays(Math.Clamp(days, 1, 90));
        var rows = await _logs.ZeroResultPhrasesAsync(since, take, profileKey, locale, cancellationToken);
        return rows.Select(r => new InsightsZeroResultRow
        {
            Phrase = r.Phrase,
            Count = r.Hits,
            Locale = r.Locale,
            ProfileKey = r.ProfileKey
        }).ToList();
    }

    /// <summary>
    /// Synonym coverage rollup. Drives the "are your synonyms keeping up?"
    /// card — totals + the two actionable counts (unused, suggested-add).
    /// The 30d window matches <see cref="SynonymCoverageService.DefaultWindow"/>
    /// so a marketer who drills from this panel into the full Synonyms ›
    /// Unused tab sees the same dataset.
    /// </summary>
    public async Task<InsightsSynonymCoverage> SynonymCoverageAsync(CancellationToken cancellationToken = default)
    {
        var result = await _synonymCoverage.AnalyzeAsync(cancellationToken);
        return new InsightsSynonymCoverage
        {
            TotalRules = result.TotalRules,
            UnusedRules = result.UnusedEntries.Count,
            LogsScanned = result.LogsScanned,
            GeneratedAt = result.GeneratedAt,
            WindowStart = result.WindowStart
        };
    }

    /// <summary>
    /// Cross-profile recent activity feed for the strip at the bottom of the
    /// Insights view. Newest-first.
    /// </summary>
    public IReadOnlyList<InsightsActivityRow> RecentActivity(int take = 20)
    {
        return _editLog.ListRecent(take)
            .Select(e => new InsightsActivityRow
            {
                At = e.At,
                ProfileKey = e.ProfileKey,
                Kind = e.Kind,
                Action = e.Action,
                Subject = e.Subject,
                ActorName = e.ActorName,
                Locale = e.Locale
            })
            .ToList();
    }
}
