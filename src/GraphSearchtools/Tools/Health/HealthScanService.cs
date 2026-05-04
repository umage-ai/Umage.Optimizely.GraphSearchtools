using System.Diagnostics;
using EPiServer;
using EPiServer.Core;
using EPiServer.Data.Dynamic;
using EPiServer.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;

#pragma warning disable CS0618 // ISiteDefinitionRepository (CMS 12 name) — see LanguageSiteEnumerator.

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

/// <summary>
/// Drives the background health scan: runs the four probes, then walks a batch
/// of published pages and verifies each is queryable via Graph by GUID. Results
/// land in DDS (settings, scans, issues) so the dashboard can render history
/// even when no editor has the page open.
///
/// Rotation: one batch of <see cref="BatchSize"/> per run, picking
/// <c>IContent</c> with <c>ContentLink.ID &gt; cursor</c>; the cursor wraps to
/// 0 when no items remain so the catalog cycles continuously. Issues clear
/// when the content is found on a later scan or has been removed from the
/// CMS repo entirely.
/// </summary>
public sealed class HealthScanService
{
    private const int BatchSize = 25;
    private const int ScanRetention = 288;     // 24h at 5-min cadence
    private const int ResolvedRetentionDays = 14;

    private readonly HealthService _probes;
    private readonly IGraphAdminClient _graphClient;
    private readonly IContentLoader _contentLoader;
    private readonly ISiteDefinitionRepository _siteDefinitions;
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly ILogger<HealthScanService> _logger;

    public HealthScanService(
        HealthService probes,
        IGraphAdminClient graphClient,
        IContentLoader contentLoader,
        ISiteDefinitionRepository siteDefinitions,
        IOptions<GraphSearchtoolsOptions> options,
        ILogger<HealthScanService> logger)
    {
        _probes = probes;
        _graphClient = graphClient;
        _contentLoader = contentLoader;
        _siteDefinitions = siteDefinitions;
        _options = options;
        _logger = logger;
    }

    public HealthSettingsRecord LoadSettings()
    {
        var store = SettingsStore();
        var existing = store.Items<HealthSettingsRecord>().FirstOrDefault();
        if (existing != null) return existing;
        var fresh = new HealthSettingsRecord { ScanEnabled = false, RotationCursor = 0 };
        store.Save(fresh);
        return fresh;
    }

    public HealthSettingsRecord SetEnabled(bool enabled)
    {
        var store = SettingsStore();
        var settings = LoadSettings();
        settings.ScanEnabled = enabled;
        store.Save(settings);
        return settings;
    }

    /// <summary>
    /// Records a probe-only scan (no content rotation, no issue diff) so the
    /// dashboard timeline reflects manual Run-check button presses too.
    /// Bars are green when all probes pass, red otherwise — manual runs can
    /// never produce an amber bar because amber requires the content cycle
    /// only the scheduled job runs.
    /// </summary>
    public void RecordManualScan(HealthResult probes)
    {
        var probesPassed = probes.Probes.Count(p => p.Status == HealthStatus.Green);
        var probesTotal = probes.Probes.Count;
        var status = probesPassed == probesTotal ? "green" : "red";
        var now = DateTime.UtcNow;
        var record = new HealthScanRecord
        {
            RanAt = now,
            ElapsedMs = probes.ElapsedMs,
            ProbesPassed = probesPassed,
            ProbesTotal = probesTotal,
            ContentChecked = 0,
            ContentMissing = 0,
            OverallStatus = status,
            Note = "Manual probe check (content cycle skipped)."
        };
        SaveScan(record);

        // Bump LastScanAt so the dashboard's auto-status reflects the most
        // recent activity from either source — otherwise "Never scanned"
        // sticks until the 5-minute scheduled job has actually fired.
        var settings = LoadSettings();
        settings.LastScanAt = now;
        SettingsStore().Save(settings);

        TrimHistory();
    }

    /// <summary>Reads settings + most recent scan history for the dashboard.</summary>
    public HealthHistoryDto GetHistory()
    {
        var settings = LoadSettings();
        var scans = ScanStore().Items<HealthScanRecord>()
            .OrderByDescending(s => s.RanAt)
            .Take(ScanRetention)
            .ToList()
            .Select(MapScan)
            .ToList();

        var issues = IssueStore().Items<HealthIssueRecord>().ToList();
        var open = issues.Where(i => i.ResolvedAt == null)
            .OrderByDescending(i => i.LastSeenAt)
            .Select(MapIssue)
            .ToList();
        var recent = issues.Where(i => i.ResolvedAt != null && i.ResolvedAt > DateTime.UtcNow.AddDays(-1))
            .OrderByDescending(i => i.ResolvedAt)
            .Take(20)
            .Select(MapIssue)
            .ToList();

        return new HealthHistoryDto(
            new HealthSettingsDto(settings.ScanEnabled, settings.LastScanAt),
            scans,
            open,
            recent,
            CountPublishedContent());
    }

    /// <summary>
    /// Cheap-ish enumeration of every published content item under every site's
    /// start page — same source the scan rotation cycles through. The count
    /// drives the "% verified" KPI on the dashboard. Caching is unnecessary
    /// for typical CMS sizes; we can introduce one later if it shows up in
    /// profiling on a large catalog.
    /// </summary>
    private int CountPublishedContent()
    {
        try
        {
            return EnumeratePublishedContent().Count();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Counting published content failed; reporting 0.");
            return 0;
        }
    }

    /// <summary>
    /// Runs a full scan synchronously (called from the scheduled job and tests).
    /// No-ops if scanning is disabled. Returns a one-line status the CMS scheduled
    /// jobs UI can render.
    /// </summary>
    public async Task<string> RunAsync(CancellationToken cancellationToken)
    {
        var settings = LoadSettings();
        if (!settings.ScanEnabled)
        {
            return "Scanning disabled — toggle Auto on the Health dashboard to enable.";
        }

        var sw = Stopwatch.StartNew();
        // 1. Probes — gateway / admin / single key / index population.
        var probeResult = await _probes.CheckAsync(cancellationToken);
        var probesPassed = probeResult.Probes.Count(p => p.Status == HealthStatus.Green);
        var probesTotal = probeResult.Probes.Count;
        var probesAllGreen = probesPassed == probesTotal;

        // 2. Content cycle — pick the next batch of published descendants.
        var batch = NextBatch(settings.RotationCursor, BatchSize, out var newCursor);
        var checkedCount = batch.Count;

        // 3. Verify each item is in Graph (only when probes passed, otherwise
        //    we'd flag every item because the gateway is down).
        var missing = new List<IContent>();
        if (probesAllGreen && checkedCount > 0)
        {
            try
            {
                var guids = batch.Select(c => c.ContentGuid.ToString()).ToList();
                var hits = await _graphClient.ResolveByGuidsAsync(
                    guids,
                    Array.Empty<string>(), // any content type
                    cancellationToken);
                var found = new HashSet<string>(hits.Select(h => h.ContentGuid), StringComparer.OrdinalIgnoreCase);
                missing = batch.Where(c => !found.Contains(c.ContentGuid.ToString())).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Graph GUID lookup failed during health scan; skipping content check this round.");
                checkedCount = 0; // pretend we didn't check (keeps the chart honest)
            }
        }

        // 4. Issue diff — open new ones, resolve fixed ones.
        var openedCount = 0;
        var resolvedCount = 0;
        if (probesAllGreen && checkedCount > 0)
        {
            var batchGuids = new HashSet<string>(batch.Select(c => c.ContentGuid.ToString()), StringComparer.OrdinalIgnoreCase);
            (openedCount, resolvedCount) = ApplyIssueDiff(batch, missing, batchGuids);
        }

        // 5. Persist scan summary, advance cursor + LastScanAt, trim history.
        sw.Stop();
        var status = !probesAllGreen ? "red" : missing.Count > 0 ? "amber" : "green";
        var note = !probesAllGreen
            ? "Probes failed — content cycle skipped."
            : checkedCount == 0
                ? "No content checked this round."
                : $"{checkedCount} checked, {missing.Count} missing.";

        SaveScan(new HealthScanRecord
        {
            RanAt = DateTime.UtcNow,
            ElapsedMs = sw.ElapsedMilliseconds,
            ProbesPassed = probesPassed,
            ProbesTotal = probesTotal,
            ContentChecked = checkedCount,
            ContentMissing = missing.Count,
            OverallStatus = status,
            Note = note
        });

        settings.RotationCursor = newCursor;
        settings.LastScanAt = DateTime.UtcNow;
        SettingsStore().Save(settings);

        TrimHistory();

        return $"Probes {probesPassed}/{probesTotal}; checked {checkedCount} pages, {missing.Count} missing. Open issues: {openedCount} new, {resolvedCount} resolved.";
    }

    // ── Content enumeration ────────────────────────────────────────

    /// <summary>
    /// Returns up to <paramref name="batchSize"/> published <c>IContent</c>
    /// items with <c>ContentLink.ID</c> strictly greater than
    /// <paramref name="afterId"/>. When fewer than the batch size are found,
    /// wraps around to the start so the rotation never stalls. The new cursor
    /// is the highest ID returned (or 0 when wrapping with no results).
    /// </summary>
    private List<IContent> NextBatch(int afterId, int batchSize, out int newCursor)
    {
        var all = EnumeratePublishedContent().ToList();
        if (all.Count == 0)
        {
            newCursor = 0;
            return new List<IContent>();
        }
        var batch = all.Where(c => c.ContentLink.ID > afterId).Take(batchSize).ToList();
        if (batch.Count < batchSize && afterId > 0)
        {
            // Wrap — fill the rest from the front of the list, dedup'd by id.
            var seen = new HashSet<int>(batch.Select(c => c.ContentLink.ID));
            foreach (var c in all)
            {
                if (batch.Count >= batchSize) break;
                if (seen.Add(c.ContentLink.ID)) batch.Add(c);
            }
        }
        newCursor = batch.Count > 0 ? batch.Max(c => c.ContentLink.ID) : 0;
        // If we just hit the very last item, wrap back to 0 so the next scan
        // restarts the cycle.
        if (batch.Count > 0 && batch[^1].ContentLink.ID == all[^1].ContentLink.ID)
        {
            newCursor = 0;
        }
        return batch;
    }

    /// <summary>
    /// Enumerates published descendants of every site's start page, deduped
    /// and sorted by ContentLink.ID. v1 only walks the page tree — blocks /
    /// media / Commerce nodes are out of scope until we add explicit picker
    /// support.
    /// </summary>
    private IEnumerable<IContent> EnumeratePublishedContent()
    {
        var seen = new HashSet<int>();
        foreach (var site in _siteDefinitions.List())
        {
            if (ContentReference.IsNullOrEmpty(site.StartPage)) continue;
            foreach (var link in _contentLoader.GetDescendents(site.StartPage))
            {
                if (!seen.Add(link.ID)) continue;
                if (!_contentLoader.TryGet<IContent>(link, out var content)) continue;
                if (!IsPublished(content)) continue;
                yield return content;
            }
        }
    }

    private static bool IsPublished(IContent content)
    {
        if (content is IVersionable v) return v.Status == VersionStatus.Published;
        return true; // un-versioned content (uncommon for pages) — assume yes
    }

    // ── Issue diff ─────────────────────────────────────────────────

    private (int opened, int resolved) ApplyIssueDiff(
        List<IContent> batch,
        List<IContent> missing,
        HashSet<string> batchGuids)
    {
        var store = IssueStore();
        var existingByGuid = store.Items<HealthIssueRecord>()
            .Where(i => i.ResolvedAt == null)
            .ToList()
            .ToDictionary(i => i.ContentGuid, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var opened = 0;

        // Open or refresh issues for items that are missing from Graph.
        foreach (var item in missing)
        {
            var guid = item.ContentGuid.ToString();
            if (existingByGuid.TryGetValue(guid, out var existing))
            {
                existing.LastSeenAt = now;
                existing.ContentName = item.Name ?? existing.ContentName;
                store.Save(existing);
                existingByGuid.Remove(guid); // mark as still open
            }
            else
            {
                var record = new HealthIssueRecord
                {
                    ContentGuid = guid,
                    ContentId = item.ContentLink.ID,
                    ContentName = item.Name ?? string.Empty,
                    ContentTypeName = ResolveContentTypeName(item),
                    Locale = (item as ILocalizable)?.Language?.Name ?? string.Empty,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    Reason = "missing-from-graph"
                };
                store.Save(record);
                opened++;
            }
        }

        // Items in this batch that ARE in Graph (i.e. found) → resolve any
        // existing open issue for them.
        var resolved = 0;
        foreach (var found in batch)
        {
            var guid = found.ContentGuid.ToString();
            if (existingByGuid.TryGetValue(guid, out var stale) && !missing.Contains(found))
            {
                stale.ResolvedAt = now;
                store.Save(stale);
                existingByGuid.Remove(guid);
                resolved++;
            }
        }

        // Sweep open issues whose CMS content has been removed entirely
        // (cheap pass — only checks a small number per scan in practice).
        foreach (var stale in existingByGuid.Values.ToList())
        {
            if (!CmsHasContent(stale.ContentGuid))
            {
                stale.Reason = "removed-from-cms";
                stale.ResolvedAt = now;
                store.Save(stale);
                resolved++;
            }
        }

        return (opened, resolved);
    }

    private static string ResolveContentTypeName(IContent content)
    {
        var t = content.GetOriginalType();
        return t.Name ?? string.Empty;
    }

    private bool CmsHasContent(string guidString)
    {
        if (!Guid.TryParse(guidString, out var guid)) return false;
        try
        {
            return _contentLoader.TryGet<IContent>(guid, out _);
        }
        catch
        {
            return false;
        }
    }

    // ── Persistence helpers ────────────────────────────────────────

    private void SaveScan(HealthScanRecord record) => ScanStore().Save(record);

    private void TrimHistory()
    {
        var scans = ScanStore();
        var stale = scans.Items<HealthScanRecord>()
            .OrderByDescending(s => s.RanAt)
            .Skip(ScanRetention)
            .ToList();
        foreach (var s in stale) scans.Delete(s.Id);

        var issues = IssueStore();
        var oldResolved = issues.Items<HealthIssueRecord>()
            .Where(i => i.ResolvedAt != null && i.ResolvedAt < DateTime.UtcNow.AddDays(-ResolvedRetentionDays))
            .ToList();
        foreach (var i in oldResolved) issues.Delete(i.Id);
    }

    private static HealthScanDto MapScan(HealthScanRecord r) =>
        new(r.Id.ToString(), r.RanAt, r.ElapsedMs, r.ProbesPassed, r.ProbesTotal,
            r.ContentChecked, r.ContentMissing, r.OverallStatus, r.Note);

    private static HealthIssueDto MapIssue(HealthIssueRecord r) =>
        new(r.Id.ToString(), r.ContentGuid, r.ContentId, r.ContentName, r.ContentTypeName,
            r.Locale, r.FirstSeenAt, r.LastSeenAt, r.ResolvedAt, r.Reason);

    private static DynamicDataStore SettingsStore()
        => DynamicDataStoreFactory.Instance.CreateStore(typeof(HealthSettingsRecord));
    private static DynamicDataStore ScanStore()
        => DynamicDataStoreFactory.Instance.CreateStore(typeof(HealthScanRecord));
    private static DynamicDataStore IssueStore()
        => DynamicDataStoreFactory.Instance.CreateStore(typeof(HealthIssueRecord));
}
