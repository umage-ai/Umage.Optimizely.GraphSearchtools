using EPiServer.PlugIn;
using EPiServer.Scheduler;
using Microsoft.Extensions.Logging;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

/// <summary>
/// Background job that runs the Health scan every 5 minutes. Auto-init creates
/// a default schedule on first start; scanning is gated by the <c>ScanEnabled</c>
/// flag toggled from the dashboard's Auto switch — when off, the job no-ops so
/// editors don't have to dig into the CMS scheduled-jobs admin to pause it.
/// </summary>
// CMS 13 marks ScheduledPlugInAttribute obsolete in favour of
// EPiServer.Scheduler.ScheduledJobAttribute, but the new attribute isn't
// part of CMS 12 — keeping the older one is the only single-attribute path
// that compiles on both targets, so the obsolete warning is suppressed here.
#pragma warning disable CS0618
[ScheduledPlugIn(
    DisplayName = "Graph Search Tools — Health scan",
    Description = "Runs gateway/credential probes and verifies a rotating batch of published pages against Optimizely Graph. Toggled from the Health dashboard.",
    GUID = "f6a4d5c1-3ea8-4c7c-a0a8-7ff6f0a4b3d1",
    DefaultEnabled = true,
    IntervalLength = 5,
    IntervalType = ScheduledIntervalType.Minutes,
    Restartable = true)]
#pragma warning restore CS0618
public sealed class HealthScanJob : ScheduledJobBase
{
    private readonly HealthScanService _scanner;
    private readonly ILogger<HealthScanJob> _logger;
    private bool _stopRequested;

    public HealthScanJob(HealthScanService scanner, ILogger<HealthScanJob> logger)
    {
        _scanner = scanner;
        _logger = logger;
        IsStoppable = true;
    }

    public override string Execute()
    {
        try
        {
            using var cts = new CancellationTokenSource();
            // ScheduledJobBase doesn't expose a CancellationToken; we honour
            // OnStopRequested by checking `_stopRequested` before persisting.
            var task = _scanner.RunAsync(cts.Token);
            task.GetAwaiter().GetResult();
            if (_stopRequested) return "Stop requested.";
            // Re-read history to surface a useful summary in the admin UI.
            var settings = _scanner.LoadSettings();
            return settings.ScanEnabled
                ? $"OK. Last run {settings.LastScanAt:u}."
                : "Scanning disabled — toggle Auto on the Health dashboard to enable.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health scan job failed.");
            return $"Failed: {ex.GetType().Name} — {ex.Message}";
        }
    }

    public override void Stop()
    {
        _stopRequested = true;
        base.Stop();
    }
}
