using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Read-side API for the admin analytics surfaces (Search Logs, Pinned
/// Coverage, Synonym Coverage). All three queries hang off a single window;
/// the UI picks the call based on which card it's rendering.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
internal sealed class TelemetryAdminApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Telemetry);
    private const int DefaultTake = 50;
    private const int MaxTake = 500;

    private readonly ITelemetryReader _reader;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<TelemetryAdminApiController> _logger;

    public TelemetryAdminApiController(
        ITelemetryReader reader,
        FeatureAccessChecker accessChecker,
        ILogger<TelemetryAdminApiController> logger)
    {
        _reader = reader;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public Task<IActionResult> Top([FromQuery] DateTime? since, [FromQuery] DateTime? until,
        [FromQuery] int? take, [FromQuery] string? channelKey, [FromQuery] string? locale,
        CancellationToken cancellationToken)
        => RunAsync(q => _reader.TopPhrasesAsync(q, cancellationToken), since, until, take, channelKey, locale, "Top");

    [HttpGet]
    public Task<IActionResult> ZeroResult([FromQuery] DateTime? since, [FromQuery] DateTime? until,
        [FromQuery] int? take, [FromQuery] string? channelKey, [FromQuery] string? locale,
        CancellationToken cancellationToken)
        => RunAsync(q => _reader.ZeroResultPhrasesAsync(q, cancellationToken), since, until, take, channelKey, locale, "ZeroResult");

    [HttpGet]
    public Task<IActionResult> LowCtr([FromQuery] DateTime? since, [FromQuery] DateTime? until,
        [FromQuery] int? take, [FromQuery] string? channelKey, [FromQuery] string? locale,
        CancellationToken cancellationToken)
        => RunAsync(q => _reader.LowCtrPhrasesAsync(q, cancellationToken), since, until, take, channelKey, locale, "LowCtr");

    [HttpGet]
    public async Task<IActionResult> Recent([FromQuery] DateTime? since, [FromQuery] DateTime? until,
        [FromQuery] int? take, [FromQuery] string? channelKey, [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var query = BuildQuery(since, until, take, channelKey, locale);
            var rows = await _reader.RecentRawAsync(query, cancellationToken);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry RecentRawAsync failed.");
            return Problem(title: "Telemetry read failed.");
        }
    }

    private async Task<IActionResult> RunAsync(
        Func<TelemetryQuery, Task<IReadOnlyList<PhraseAggregate>>> reader,
        DateTime? since, DateTime? until, int? take, string? channelKey, string? locale,
        string action)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var query = BuildQuery(since, until, take, channelKey, locale);
            var rows = await reader(query);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry {Action} failed.", action);
            return Problem(title: "Telemetry read failed.");
        }
    }

    private static TelemetryQuery BuildQuery(DateTime? since, DateTime? until, int? take, string? channelKey, string? locale)
    {
        var untilUtc = (until ?? DateTime.UtcNow).ToUniversalTime();
        var sinceUtc = (since ?? untilUtc.AddHours(-24)).ToUniversalTime();
        var clampedTake = Math.Clamp(take ?? DefaultTake, 1, MaxTake);
        return new TelemetryQuery(
            sinceUtc,
            untilUtc,
            clampedTake,
            string.IsNullOrWhiteSpace(channelKey) ? null : channelKey,
            string.IsNullOrWhiteSpace(locale) ? null : locale);
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Insights);
}
