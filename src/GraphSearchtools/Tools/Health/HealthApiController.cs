using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

/// <summary>
/// Drives the Health dashboard. Three concerns surfaced here:
///   • <c>Check</c> — runs the four probes on demand (the Run button).
///   • <c>History</c> — returns the persisted scan timeline + open issues +
///     recently-resolved issues, used to draw the chart and issues panel.
///   • <c>Settings</c> — read/write toggle for the background scan.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class HealthApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Health);

    private readonly HealthService _service;
    private readonly HealthScanService _scanner;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<HealthApiController> _logger;

    public HealthApiController(
        HealthService service,
        HealthScanService scanner,
        FeatureAccessChecker accessChecker,
        ILogger<HealthApiController> logger)
    {
        _service = service;
        _scanner = scanner;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Check(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var result = await _service.CheckAsync(cancellationToken);
            // Record on the timeline so editors get visible feedback from
            // manual checks. Persistence failure is non-fatal — the probe
            // result has already been computed and the user is waiting on it.
            try { _scanner.RecordManualScan(result); }
            catch (Exception ex) { _logger.LogWarning(ex, "Recording manual probe scan failed."); }
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health probes failed unexpectedly.");
            return Problem(title: "Health check failed.");
        }
    }

    [HttpGet]
    public IActionResult History()
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(_scanner.GetHistory());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health history lookup failed.");
            return Problem(title: "Health history failed.");
        }
    }

    [HttpGet]
    public IActionResult Settings()
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var s = _scanner.LoadSettings();
            return Ok(new HealthSettingsDto(s.ScanEnabled, s.LastScanAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health settings lookup failed.");
            return Problem(title: "Health settings failed.");
        }
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult Settings([FromBody] HealthSettingsUpdate update)
    {
        if (!HasAccess()) return Forbid();
        if (update == null) return BadRequest(new { message = "Settings payload is required." });
        try
        {
            var s = _scanner.SetEnabled(update.ScanEnabled);
            return Ok(new HealthSettingsDto(s.ScanEnabled, s.LastScanAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health settings update failed.");
            return Problem(title: "Health settings update failed.");
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Health);
}

public sealed record HealthSettingsUpdate(bool ScanEnabled);
