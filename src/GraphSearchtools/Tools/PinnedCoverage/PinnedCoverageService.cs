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
/// <see cref="ITelemetryReader.TopPhrasesAsync"/> read sized to the collection
/// count. Tenants with hundreds of collections will see a multi-second
/// latency; the UI hides it behind a "Run audit" button.
/// </remarks>
public sealed class PinnedCoverageService
{
    /// <summary>
    /// Window the CTR / no-activity heuristics span. Matches the Search Logs
    /// UI's default — keeping them aligned means a phrase flagged here as
    /// "no activity" is also absent from the top-phrases view.
    /// </summary>
    public static readonly TimeSpan ActivityWindow = TimeSpan.FromDays(7);

    /// <summary>
    /// Hits cutoff for low-CTR detection. Fewer than 5 hits is too noisy to
    /// act on; the audit suppresses the issue rather than guess.
    /// </summary>
    public const int MinSessionsForCtr = 5;

    /// <summary>CTR threshold under which a pinned phrase is flagged "LowCtr". Encoded as a constant so the audit policy is one place to tweak.</summary>
    public const double LowCtrThreshold = 0.05;

    /// <summary>
    /// Cap on the phrase aggregate pull. Big enough to cover every pinned
    /// phrase a typical site declares; the head-only nature of TopPhrasesAsync
    /// is fine here because we only join against pin phrases anyway.
    /// </summary>
    private const int PhraseAggregatePullSize = 5000;

    private readonly IGraphAdminClient _graphClient;
    private readonly IContentLoader _contentLoader;
    private readonly ISearchChannelRegistry _registry;
    private readonly ITelemetryReader _reader;
    private readonly ILogger<PinnedCoverageService> _logger;

    public PinnedCoverageService(
        IGraphAdminClient graphClient,
        IContentLoader contentLoader,
        ISearchChannelRegistry registry,
        ITelemetryReader reader,
        ILogger<PinnedCoverageService> logger)
    {
        _graphClient = graphClient;
        _contentLoader = contentLoader;
        _registry = registry;
        _reader = reader;
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

        var channelLookup = BuildChannelLookup();

        // Per-phrase aggregates over the activity window. The aggregate-first
        // ingest doesn't carry the click target id, so we lose the legacy
        // "did this *pin* earn the click?" attribution. Instead the audit
        // checks whether the pinned phrase earns clicks at all — if a query
        // for "warranty" gets zero engagement no matter what's pinned, the
        // pin's effort is wasted.
        var phraseAggregates = await _reader.TopPhrasesAsync(
            new TelemetryQuery(now - ActivityWindow, now, PhraseAggregatePullSize),
            cancellationToken);
        var phraseIndex = phraseAggregates
            .GroupBy(p => NormalizePhrase(p.Phrase), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new PhraseStats(g.Sum(p => p.Hits), WeightedCtr(g)),
                StringComparer.Ordinal);
        var totalLoggedHits = phraseAggregates.Sum(p => p.Hits);

        var issues = new List<PinnedIssue>();
        foreach (var (col, item) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channelKey = ResolveChannelKeyForCollection(col, channelLookup);

            // Issue: target unpublished or deleted.
            var targetState = ResolveTargetState(item.TargetKey);
            if (targetState.Kind == "Deleted")
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "Deleted",
                    CollectionKey = col.Key,
                    ChannelKey = channelKey,
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
                    ChannelKey = channelKey,
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
                    ChannelKey = channelKey,
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
            var stats = phraseIndex.TryGetValue(NormalizePhrase(item.Phrases), out var s) ? s : null;
            var phraseHits = stats?.Hits ?? 0;
            if (totalLoggedHits > 0 && phraseHits == 0)
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "NoActivity",
                    CollectionKey = col.Key,
                    ChannelKey = channelKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = targetState.Name,
                    Detail = $"No search sessions for this phrase in the last {ActivityWindow.TotalDays:N0} days."
                });
                continue;
            }

            // Issue: phrase has hits but doesn't earn clicks. The aggregate-
            // first ingest doesn't carry per-target click attribution, so this
            // is now a phrase-level CTR check rather than a per-pin one. The
            // semantic shift: before, "LowCtr" meant the *pin* didn't earn
            // the click; now it means users aren't engaging with this phrase
            // *at all* — pinning effort here is wasted regardless of target.
            if (stats != null && phraseHits >= MinSessionsForCtr && stats.Ctr < LowCtrThreshold)
            {
                issues.Add(new PinnedIssue
                {
                    Kind = "LowCtr",
                    CollectionKey = col.Key,
                    ChannelKey = channelKey,
                    Phrase = item.Phrases,
                    TargetId = item.TargetKey ?? string.Empty,
                    TargetName = targetState.Name,
                    Detail = $"Phrase has {phraseHits} hits but only {stats.Ctr:P1} CTR — users aren't engaging."
                });
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
    /// Map collection.Key → channel-key by invoking each registered channel's
    /// <see cref="SearchChannel.PinnedKeyForLocale"/> against every locale the
    /// channel declares. The channel-keyed lookup is channel-keys-only (no
    /// generic) — Generic-bound collections are deliberately surfaced as
    /// <c>null</c> ChannelKey so the UI links to the Generic channel detail
    /// rather than no-op.
    /// </summary>
    private Dictionary<string, string> BuildChannelLookup()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in _registry.All)
        {
            if (channel.PinnedKeyForLocale == null) continue;
            // Probe the channel's declared locales — and an "en" fallback when
            // the channel has none — to cover the common cases. Most production
            // formulas are deterministic functions of locale, so this catches
            // every collection a channel owns.
            var locales = channel.Locales != null && channel.Locales.Count > 0
                ? channel.Locales
                : new[] { "en" };
            foreach (var locale in locales)
            {
                string? key;
                try { key = channel.PinnedKeyForLocale(locale); }
                catch { key = null; }
                if (string.IsNullOrEmpty(key)) continue;
                // First-write-wins: if two channels share a key, the earlier
                // registration takes precedence. The UI exposes the conflict
                // through the overlap table anyway.
                map.TryAdd(key, channel.Key);
            }
        }
        return map;
    }

    private static string? ResolveChannelKeyForCollection(PinnedCollectionResult col, Dictionary<string, string> lookup)
    {
        if (string.IsNullOrEmpty(col.Key)) return null;
        return lookup.TryGetValue(col.Key, out var channelKey) ? channelKey : null;
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
    /// Hits-weighted CTR across the per-(channel, locale) aggregates a single
    /// normalized phrase produces. Reader returns one row per (phrase, channel,
    /// locale) tuple, each with its own per-row CTR; collapsing them naively
    /// (mean of CTRs) over-weights low-traffic rows. Weight by hits instead so
    /// the audit reflects the true engagement rate the phrase earns.
    /// </summary>
    private static double WeightedCtr(IEnumerable<PhraseAggregate> rowsForPhrase)
    {
        var clicks = 0d;
        var hits = 0;
        foreach (var r in rowsForPhrase)
        {
            clicks += r.Hits * r.Ctr;
            hits += r.Hits;
        }
        return hits == 0 ? 0d : clicks / hits;
    }

    private sealed record PhraseStats(int Hits, double Ctr);

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
