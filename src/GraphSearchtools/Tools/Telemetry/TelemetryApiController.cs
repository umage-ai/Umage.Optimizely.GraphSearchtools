using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// JSON request shape for a single search-log entry. Mirrors
/// <see cref="SearchLogEntry"/> but with camelCase JSON binding and no
/// server-controlled fields (<c>Id</c>, <c>Source</c>) — those are filled in
/// by the controller. <c>At</c> is a hint; the server clamps stale or
/// future-dated values.
/// </summary>
public class SearchLogEntryRequest
{
    public DateTime? At { get; set; }
    public string? Phrase { get; set; }
    public string? Locale { get; set; }
    public string? Site { get; set; }
    public string? ProfileKey { get; set; }
    public int ResultCount { get; set; } = -1;
    public int? TopResultRank { get; set; }
    public string? TopResultId { get; set; }
    public int DurationMs { get; set; }
    public string? Ranking { get; set; }
    public string? ClientId { get; set; }
    public string? UserAgentClass { get; set; }
}

/// <summary>
/// Phase 4 foundation — telemetry ingest endpoint for the host search SDK.
/// Mounted under <c>{basePath}/TelemetryApi/{action}</c> by the convention
/// route (the same shape as other admin tool controllers).
/// </summary>
/// <remarks>
/// <para>
/// The endpoint is auth-gated (<c>codeart:graphsearchtools</c> + the optional
/// per-feature <see cref="GraphSearchtoolsPermissions.Telemetry"/> permission).
/// Anonymous push is intentionally not allowed — host search surfaces are
/// expected to call server-side with the same auth shape an editor uses.
/// </para>
/// <para>
/// Stale-<c>At</c> policy: if the host supplies an <c>at</c> more than 24h in
/// the past or more than 5 minutes in the future, the entry is dropped
/// quietly with HTTP 202 Accepted. Telemetry must never fail loudly — a clock-
/// skew or buffered backfill from a misbehaving host shouldn't break the
/// search experience. Single-entry endpoint reports the drop in the response
/// body for diagnostics; batch endpoint returns the per-row accept/drop
/// counts.
/// </para>
/// </remarks>
[Authorize(Policy = "codeart:graphsearchtools")]
public class TelemetryApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Telemetry);

    /// <summary>Maximum entries accepted per batch call. Larger payloads → 400.</summary>
    public const int MaxBatchSize = 200;

    /// <summary>Reject host-supplied <c>At</c> older than this. Quietly dropped.</summary>
    public static readonly TimeSpan StaleWindow = TimeSpan.FromHours(24);

    /// <summary>Reject host-supplied <c>At</c> further in the future than this. Quietly dropped.</summary>
    public static readonly TimeSpan FutureSkewWindow = TimeSpan.FromMinutes(5);

    private readonly SearchLogService _searchLog;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<TelemetryApiController> _logger;

    public TelemetryApiController(
        SearchLogService searchLog,
        FeatureAccessChecker accessChecker,
        ILogger<TelemetryApiController> logger)
    {
        _searchLog = searchLog;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    /// <summary>
    /// Append one search-log entry. Path:
    /// <c>POST {basePath}/TelemetryApi/SearchLog</c>.
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> when stored; <c>202 Accepted</c> with
    /// <c>{ stored = false, reason = "stale-at" | "future-at" }</c> when the
    /// entry was quietly dropped; <c>400 Bad Request</c> on missing body.
    /// </returns>
    [HttpPost]
    [RequireAjax]
    public IActionResult SearchLog([FromBody] SearchLogEntryRequest? request)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Telemetry payload is required." });

        try
        {
            var (entry, dropReason) = NormaliseAndValidate(request);
            if (dropReason != null)
            {
                // Quiet drop — telemetry must never fail loudly. 202 lets the
                // host know we received but did not store.
                return StatusCode(StatusCodes.Status202Accepted, new { stored = false, reason = dropReason });
            }

            _searchLog.Append(entry);
            return Ok(new { stored = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in TelemetryApiController.SearchLog.");
            return Problem(title: "Telemetry ingest failed.");
        }
    }

    /// <summary>
    /// Append a batch of search-log entries. Path:
    /// <c>POST {basePath}/TelemetryApi/SearchLogBatch</c>.
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with <c>{ accepted, dropped }</c> counts when accepted
    /// (drops still count as success — telemetry never fails loudly);
    /// <c>400 Bad Request</c> when the array is missing or has more than
    /// <see cref="MaxBatchSize"/> entries.
    /// </returns>
    [HttpPost]
    [RequireAjax]
    public IActionResult SearchLogBatch([FromBody] SearchLogEntryRequest[]? requests)
    {
        if (!HasAccess()) return Forbid();
        if (requests == null) return BadRequest(new { message = "Telemetry batch is required." });
        if (requests.Length > MaxBatchSize)
        {
            return BadRequest(new { message = $"Batch size exceeds the {MaxBatchSize}-entry limit." });
        }

        try
        {
            var accepted = new List<SearchLogEntry>(requests.Length);
            var dropped = 0;
            foreach (var req in requests)
            {
                if (req == null) { dropped++; continue; }
                var (entry, dropReason) = NormaliseAndValidate(req);
                if (dropReason != null) { dropped++; continue; }
                accepted.Add(entry);
            }

            if (accepted.Count > 0) _searchLog.AppendBatch(accepted);
            return Ok(new { accepted = accepted.Count, dropped });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in TelemetryApiController.SearchLogBatch.");
            return Problem(title: "Telemetry ingest failed.");
        }
    }

    /// <summary>
    /// Build a <see cref="SearchLogEntry"/> from the host request, server-
    /// stamping <c>Source = "host-sdk"</c> and clamping <c>At</c> to the
    /// allowed window. Returns a non-null <c>dropReason</c> when the entry
    /// should be quietly dropped.
    /// </summary>
    internal static (SearchLogEntry Entry, string? DropReason) NormaliseAndValidate(SearchLogEntryRequest request)
    {
        var now = DateTime.UtcNow;
        var hostAt = request.At;
        DateTime at;
        string? dropReason = null;

        if (hostAt.HasValue)
        {
            var hostUtc = hostAt.Value.Kind == DateTimeKind.Utc
                ? hostAt.Value
                : hostAt.Value.ToUniversalTime();

            if (hostUtc < now - StaleWindow)
            {
                dropReason = "stale-at";
                at = now; // never used because we drop, but keep the entry well-formed.
            }
            else if (hostUtc > now + FutureSkewWindow)
            {
                dropReason = "future-at";
                at = now;
            }
            else
            {
                at = hostUtc;
            }
        }
        else
        {
            // Host didn't supply a timestamp — server-stamp now.
            at = now;
        }

        var entry = new SearchLogEntry
        {
            At = at,
            Phrase = request.Phrase ?? string.Empty,
            Locale = (request.Locale ?? string.Empty).ToLowerInvariant(),
            Site = request.Site ?? string.Empty,
            ProfileKey = request.ProfileKey ?? string.Empty,
            ResultCount = request.ResultCount,
            TopResultRank = request.TopResultRank,
            TopResultId = request.TopResultId ?? string.Empty,
            DurationMs = request.DurationMs,
            Ranking = request.Ranking ?? string.Empty,
            ClientId = request.ClientId ?? string.Empty,
            UserAgentClass = request.UserAgentClass ?? string.Empty,
            Source = "host-sdk"
        };

        return (entry, dropReason);
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Telemetry);
}
