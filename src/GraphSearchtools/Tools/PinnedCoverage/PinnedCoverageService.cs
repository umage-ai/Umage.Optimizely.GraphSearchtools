using EPiServer;
using EPiServer.Core;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage;

/// <summary>
/// Phase 4 §6 audit composer. Pulls every pinned collection + item from Graph,
/// joins each item against the CMS via <see cref="IContentLoader"/> to detect
/// unpublished/deleted targets, joins against the 7-day search-log window for
/// CTR, and groups phrases across collections to surface overlap conflicts.
/// </summary>
/// <remarks>
/// The audit is read-only and runs entirely on demand — there is no DDS table
/// behind <see cref="PinnedCoverageResult"/>. Cost is one
/// <see cref="IGraphAdminClient.GetCollectionsAsync"/> call plus one
/// <see cref="IGraphAdminClient.GetItemsAsync"/> call per collection, plus one
/// <see cref="SearchLogService.ListSince"/> read. Tenants with hundreds of
/// collections will see a multi-second latency; the UI hides it behind a
/// "Run audit" button.
/// </remarks>
public sealed class PinnedCoverageService
{
    /// <summary>
    /// Window the CTR / no-activity heuristics span. Matches
    /// <c>SearchLogService</c>'s phase-4 window default — keeping these
    /// aligned means a phrase that's "no activity" here is also missing from
    /// the Search Logs UI's "top phrases" view.
    /// </summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Sessions cutoff for low-CTR detection. Mirrors <c>SearchLogService</c>'s
    /// <c>LowCtrPhrases</c> floor — fewer than 5 hits is too noisy to act on.
    /// </summary>
    public const int MinSessionsForCtr = 5;

    /// <summary>CTR threshold under which a pin is flagged "LowCtr". Encoded as a constant so the audit policy is one place to tweak.</summary>
    public const double LowCtrThreshold = 0.05;

    private readonly IGraphAdminClient _graphClient;
    private readonly IContentLoader _contentLoader;
    private readonly ISearchProfileRegistry _registry;
    private readonly SearchLogService _logs;
    private readonly ILogger<PinnedCoverageService> _logger;

    public PinnedCoverageService(
        IGraphAdminClient graphClient,
        IContentLoader contentLoader,
        ISearchProfileRegistry registry,
        SearchLogService logs,
        ILogger<PinnedCoverageService> logger)
    {
        _graphClient = graphClient;
        _contentLoader = contentLoader;
        _registry = registry;
        _logs = logs;
        _logger = logger;
    }

    /// <summary>Compose a fresh audit result. Throws on Graph failure — the controller maps it to 503.</summary>
    public async Task<PinnedCoverageResult> RunAuditAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var collections = await _graphClient.GetCollectionsAsync(cancellationToken);

        // Collect every (collection, item) pair upfront so overlap detection
        // and per-item issue detection can run over the same flat list. Each
        // collection is listed once via a separate call; the API has no batch
        // endpoint so we fan out sequentially to avoid hammering the gateway.
        var pairs = new List<(PinnedCollectionResult Col, PinnedItemResult Item)>();
        foreach (var col in collections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var items = await _graphClient.GetItemsAsync(col.Id, cancellationToken);
                foreach (var item in items)
                {
                    pairs.Add((col, item));
                }
            }
            catch (Exception ex)
            {
                // One bad collection shouldn't break the whole audit; log and
                // skip. The audit's value is the surfaced issue list, not
                // bullet-proof aggregation.
                _logger.LogWarning(ex, "Pinned coverage: skipping collection {CollectionId} because items could not be loaded.", col.Id);
            }
        }

        var profileLookup = BuildProfileLookup();
        var logWindow = _logs.ListSince(now - ActivityWindow, take: 50000).ToList();

        // Pre-aggregate the log window by phrase + target so the per-item
        // CTR loop runs in O(items) rather than O(items * logs).
        var phraseTargetIndex = IndexLogsByPhraseAndTarget(logWindow);
        var phraseIndex = IndexLogsByPhrase(logWindow);

        var issues = new List<PinnedIssue>();
        foreach (var (col, item) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profileKey = ResolveProfileKeyForCollection(col, profileLookup);

            // Issue: target unpublished or deleted.
            var targetState = ResolveTargetState(item.TargetKey);
            if (targetState.Kind == "Deleted")
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "Deleted",
                    CollectionKey = col.Key,
                    ProfileKey = profileKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = null,
                    Detail = "Target content not found in CMS."
                });
                continue;
            }
            if (targetState.Kind == "Unpublished")
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "Unpublished",
                    CollectionKey = col.Key,
                    ProfileKey = profileKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = targetState.Name,
                    Detail = "Target content exists but is not in Published state."
                });
                continue;
            }

            // Issue: pin past its EffectiveTo.
            if (item.EffectiveTo.HasValue && item.EffectiveTo.Value < now)
            {
                var days = Math.Max(1, (int)(now - item.EffectiveTo.Value).TotalDays);
                issues.Add(new PinnedIssue
                {
                    Kind = "Expired",
                    CollectionKey = col.Key,
                    ProfileKey = profileKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = targetState.Name,
                    Detail = $"Expired {days} day(s) ago ({item.EffectiveTo.Value:yyyy-MM-dd})."
                });
                continue;
            }

            // Issue: pin earned no traffic this window. We only surface this
            // when at least one log row exists overall — on a tenant with no
            // ingestion path wired up, every pin would otherwise be flagged
            // and the audit would be useless.
            var phraseHits = phraseIndex.TryGetValue(NormalizePhrase(item.Phrases), out var pHits) ? pHits : 0;
            if (logWindow.Count > 0 && phraseHits == 0)
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "NoActivity",
                    CollectionKey = col.Key,
                    ProfileKey = profileKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = targetState.Name,
                    Detail = $"No search sessions for this phrase in the last {ActivityWindow.TotalDays:N0} days."
                });
                continue;
            }

            // Issue: pin shown but not earning clicks. CTR is computed against
            // the SearchLogEntry rows whose phrase matches AND whose
            // TopResultId equals the pin's TargetKey — i.e. the visitor
            // clicked the pinned result, not just any result.
            var key = (NormalizePhrase(item.Phrases), item.TargetKey ?? string.Empty);
            if (phraseHits >= MinSessionsForCtr
                && phraseTargetIndex.TryGetValue(key, out var tally))
            {
                var ctr = phraseHits == 0 ? 0d : (double)tally.Clicks / phraseHits;
                if (ctr < LowCtrThreshold)
                {
                    issues.Add(new PinnedIssue
                    {
                        Kind = "LowCtr",
                        CollectionKey = col.Key,
                        ProfileKey = profileKey,
                        Phrase = item.Phrases,
                        TargetId = item.TargetKey ?? string.Empty,
                        TargetName = targetState.Name,
                        Detail = $"{tally.Clicks} / {phraseHits} sessions clicked through (CTR {ctr:P1})."
                    });
                }
            }
        }

        var overlaps = DetectOverlaps(pairs);

        return new PinnedCoverageResult
        {
            GeneratedAt = now,
            Issues = issues
                .OrderBy(i => KindSortOrder(i.Kind))
                .ThenBy(i => i.CollectionKey, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.Phrase, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Overlaps = overlaps
        };
    }

    /// <summary>
    /// Map collection.Key → profile-key by invoking each registered profile's
    /// <see cref="SearchProfile.PinnedKeyForLocale"/> against every locale the
    /// profile declares. The profile-keyed lookup is profile-keys-only (no
    /// generic) — Generic-bound collections are deliberately surfaced as
    /// <c>null</c> ProfileKey so the UI links to the Generic profile detail
    /// rather than no-op.
    /// </summary>
    private Dictionary<string, string> BuildProfileLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in _registry.All)
        {
            if (profile.PinnedKeyForLocale == null) continue;
            // Probe the profile's declared locales — and an "en" fallback when
            // the profile has none — to cover the common cases. Most production
            // formulas are deterministic functions of locale, so this catches
            // every collection a profile owns.
            var locales = profile.Locales != null && profile.Locales.Count > 0
                ? profile.Locales
                : new[] { "en" };
            foreach (var locale in locales)
            {
                string? key;
                try { key = profile.PinnedKeyForLocale(locale); }
                catch { key = null; }
                if (string.IsNullOrEmpty(key)) continue;
                // First-write-wins: if two profiles share a key, the earlier
                // registration takes precedence. The UI exposes the conflict
                // through the overlap table anyway.
                map.TryAdd(key, profile.Key);
            }
        }
        return map;
    }

    private static string? ResolveProfileKeyForCollection(PinnedCollectionResult col, Dictionary<string, string> lookup)
    {
        if (string.IsNullOrEmpty(col.Key)) return null;
        return lookup.TryGetValue(col.Key, out var profileKey) ? profileKey : null;
    }

    private (string Kind, string? Name) ResolveTargetState(string targetKey)
    {
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            // No target at all — treat as Deleted; the pin can't ever satisfy.
            return ("Deleted", null);
        }
        if (!Guid.TryParse(targetKey, out var guid))
        {
            // External / non-CMS targets can't be checked locally. Treat as
            // "Published" to skip the issue — the audit only surfaces pins it
            // can authoritatively flag.
            return ("Published", null);
        }

        IContent? content = null;
        try
        {
            if (!_contentLoader.TryGet<IContent>(guid, out var fetched) || fetched == null)
            {
                return ("Deleted", null);
            }
            content = fetched;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pinned coverage: content lookup for {Guid} threw.", guid);
            return ("Deleted", null);
        }

        if (content is IVersionable versionable && versionable.Status != VersionStatus.Published)
        {
            return ("Unpublished", content.Name);
        }

        return ("Published", content.Name);
    }

    /// <summary>
    /// Build a phrase + target → (clicks) lookup. "Click" matches
    /// <see cref="SearchLogService.CtrRankCutoff"/> — a session whose
    /// <c>TopResultRank</c> is in <c>[1, cutoff]</c>. Restricting by
    /// <c>TopResultId == TargetKey</c> ensures we attribute the click to the
    /// pin, not to any organic result.
    /// </summary>
    private static Dictionary<(string Phrase, string TargetId), (int Clicks, int Hits)> IndexLogsByPhraseAndTarget(IEnumerable<SearchLogEntry> rows)
    {
        var map = new Dictionary<(string, string), (int Clicks, int Hits)>();
        foreach (var entry in rows)
        {
            if (string.IsNullOrWhiteSpace(entry.Phrase)) continue;
            if (string.IsNullOrEmpty(entry.TopResultId)) continue;
            var key = (NormalizePhrase(entry.Phrase), entry.TopResultId);
            map.TryGetValue(key, out var current);
            var click = entry.TopResultRank.HasValue
                && entry.TopResultRank.Value >= 1
                && entry.TopResultRank.Value <= SearchLogService.CtrRankCutoff
                ? 1 : 0;
            map[key] = (current.Clicks + click, current.Hits + 1);
        }
        return map;
    }

    private static Dictionary<string, int> IndexLogsByPhrase(IEnumerable<SearchLogEntry> rows)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            if (string.IsNullOrWhiteSpace(entry.Phrase)) continue;
            var key = NormalizePhrase(entry.Phrase);
            map.TryGetValue(key, out var hits);
            map[key] = hits + 1;
        }
        return map;
    }

    private static string NormalizePhrase(string phrase)
        => (phrase ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Detect phrases pinned across two-or-more collections. The grouping key
    /// is normalised so casing/whitespace differences collapse. Each phrase
    /// in a collection is split on commas because Graph stores comma-joined
    /// phrase aliases on a single row.
    /// </summary>
    private static List<PinnedOverlap> DetectOverlaps(IEnumerable<(PinnedCollectionResult Col, PinnedItemResult Item)> pairs)
    {
        var phraseToCols = new Dictionary<string, (string DisplayPhrase, HashSet<string> Cols)>(StringComparer.Ordinal);

        foreach (var (col, item) in pairs)
        {
            foreach (var phrase in SplitPhrases(item.Phrases))
            {
                var key = NormalizePhrase(phrase);
                if (string.IsNullOrEmpty(key)) continue;
                if (!phraseToCols.TryGetValue(key, out var bucket))
                {
                    bucket = (phrase.Trim(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    phraseToCols[key] = bucket;
                }
                if (!string.IsNullOrEmpty(col.Key))
                {
                    bucket.Cols.Add(col.Key);
                }
            }
        }

        return phraseToCols
            .Where(kv => kv.Value.Cols.Count >= 2)
            .Select(kv => new PinnedOverlap
            {
                Phrase = kv.Value.DisplayPhrase,
                Collections = kv.Value.Cols
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderByDescending(o => o.Collections.Count)
            .ThenBy(o => o.Phrase, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitPhrases(string phrases)
    {
        if (string.IsNullOrWhiteSpace(phrases)) yield break;
        foreach (var part in phrases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    /// <summary>
    /// Render order for the issues table. Severe / actionable kinds first so
    /// editors see what's broken before what's just stale.
    /// </summary>
    private static int KindSortOrder(string kind) => kind switch
    {
        "Deleted" => 0,
        "Unpublished" => 1,
        "Expired" => 2,
        "LowCtr" => 3,
        "NoActivity" => 4,
        _ => 5
    };
}
