using EPiServer.Data.Dynamic;
using EPiServer.PlugIn;
using EPiServer.Scheduler;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Nightly cleanup of aggregate buckets and the per-instance raw ring. Drops
/// rows older than the configured retention window. The default exists so a
/// forgotten dev instance doesn't accrue forever; production deployments with
/// long retention needs override <see cref="LocalTelemetryOptions.BucketRetention"/>.
/// </summary>
// CMS 13 marks ScheduledPlugInAttribute obsolete in favour of
// EPiServer.Scheduler.ScheduledJobAttribute, but the new attribute isn't part
// of CMS 12 — keeping the older one is the only single-attribute path that
// compiles on both targets, so the obsolete warning is suppressed here.
#pragma warning disable CS0618
[ScheduledPlugIn(
    DisplayName = "Graph Search Tools — Telemetry retention",
    Description = "Drops aggregate search-log buckets older than the configured retention window, plus aged-out forensic ring rows.",
    GUID = "8b2e9e4f-5a4b-4f0a-9d3a-7c2b9c1e8d10",
    DefaultEnabled = true,
    IntervalLength = 1,
    IntervalType = ScheduledIntervalType.Days,
    Restartable = true)]
#pragma warning restore CS0618
public sealed class TelemetryRetentionJob : ScheduledJobBase
{
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly ILogger<TelemetryRetentionJob> _logger;
    private bool _stopRequested;

    public TelemetryRetentionJob(IOptions<GraphSearchtoolsOptions> options, ILogger<TelemetryRetentionJob> logger)
    {
        _options = options;
        _logger = logger;
        IsStoppable = true;
    }

    public override string Execute()
    {
        try
        {
            var opts = _options.Value.Telemetry;
            var bucketCutoff = DateTime.UtcNow - opts.BucketRetention;
            var ringCutoff = DateTime.UtcNow - opts.RawRingTtl;

            var bucketsDeleted = TrimBuckets(bucketCutoff);
            if (_stopRequested) return $"Stop requested. Trimmed {bucketsDeleted} aggregate row(s).";
            var ringDeleted = TrimRing(ringCutoff);

            return $"Trimmed {bucketsDeleted} aggregate row(s) older than {bucketCutoff:u} and {ringDeleted} ring row(s) older than {ringCutoff:u}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry retention job failed.");
            return $"Failed: {ex.GetType().Name} — {ex.Message}";
        }
    }

    public override void Stop()
    {
        _stopRequested = true;
        base.Stop();
    }

    private int TrimBuckets(DateTime cutoff)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var stale = store.Items<SearchLogBucket>().Where(b => b.BucketUtc < cutoff).ToList();
        foreach (var row in stale)
        {
            if (_stopRequested) break;
            store.Delete(row.Id);
        }
        return stale.Count;
    }

    private int TrimRing(DateTime cutoff)
    {
        var store = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogRing));
        var stale = store.Items<SearchLogRing>().Where(r => r.TimestampUtc < cutoff).ToList();
        foreach (var row in stale)
        {
            if (_stopRequested) break;
            store.Delete(row.Id);
        }
        return stale.Count;
    }
}
