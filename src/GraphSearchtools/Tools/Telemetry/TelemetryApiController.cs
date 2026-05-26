using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Public ingest endpoint for the host page's search/click beacons. Open by
/// design — public visitors aren't authenticated — but defended in depth: a
/// per-IP and global rate limiter, a body-bytes cap, and the channel's
/// DropOldest semantics together bound the cost a malicious caller can impose.
/// </summary>
[ApiController]
[Route("api/telemetry")]
internal sealed class TelemetryApiController : ControllerBase
{
    private const int MaxAllowedBodyBytes = 64 * 1024; // upper cap on the option

    private readonly IServiceProvider _services;
    private readonly TelemetryAbuseGuard _guard;
    private readonly LocalTelemetryOptions _options;
    private readonly bool _featureEnabled;
    private readonly ILogger<TelemetryApiController> _logger;

    public TelemetryApiController(
        IServiceProvider services,
        TelemetryAbuseGuard guard,
        IOptions<GraphSearchtoolsOptions> options,
        ILogger<TelemetryApiController> logger)
    {
        _services = services;
        _guard = guard;
        _options = options.Value.Telemetry;
        _featureEnabled = options.Value.Features.Telemetry;
        _logger = logger;
    }

    /// <summary>
    /// Single endpoint for both event kinds. Keeps the host-SDK call site
    /// uniform and the response codes honest: 204 on accept, 410 when no
    /// local sink is wired (so the SDK disables itself), 429 on rate-limit
    /// rejection, 413 on oversize body.
    /// </summary>
    [HttpPost("searchlog")]
    public async Task<IActionResult> SearchLog(CancellationToken cancellationToken)
    {
        if (!_featureEnabled) return NotFound();

        var sink = _services.GetService<ITelemetrySink>();
        if (sink == null)
        {
            // Host has wired a 3rd-party reader; no local pipeline exists.
            // The SDK detects 410 once and stops calling.
            return StatusCode(StatusCodes.Status410Gone, new { message = "Telemetry sink not configured." });
        }

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (!_guard.TryAdmit(clientIp))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var maxBytes = Math.Min(_options.MaxBodyBytes, MaxAllowedBodyBytes);
        if (Request.ContentLength is long length && length > maxBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        SearchLogPayload? payload;
        try
        {
            payload = await ReadPayloadAsync(maxBytes, cancellationToken);
        }
        catch (PayloadTooLargeException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception)
        {
            // Bad JSON / wrong shape — don't echo details; the host SDK
            // shouldn't depend on error bodies.
            return BadRequest();
        }

        if (payload == null) return BadRequest();

        var ts = payload.TimestampUtc ?? DateTime.UtcNow;

        switch ((payload.Kind ?? "").ToLowerInvariant())
        {
            case "search":
                sink.Record(new SearchEvent(
                    payload.Phrase ?? string.Empty,
                    payload.ChannelKey ?? string.Empty,
                    payload.Locale ?? string.Empty,
                    payload.ResultCount ?? 0,
                    ts));
                break;
            case "click":
                sink.Record(new ClickEvent(
                    payload.Phrase ?? string.Empty,
                    payload.ChannelKey ?? string.Empty,
                    payload.Locale ?? string.Empty,
                    payload.Rank ?? 0,
                    ts,
                    payload.OriginalBucketUtc));
                break;
            default:
                return BadRequest();
        }

        return NoContent();
    }

    private async Task<SearchLogPayload?> ReadPayloadAsync(int maxBytes, CancellationToken ct)
    {
        // Read with a hard byte cap so a chunked POST can't sneak past the
        // ContentLength gate. EnableBuffering isn't needed — we read the body
        // once.
        using var ms = new MemoryStream();
        var buffer = new byte[8 * 1024];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > maxBytes) throw new PayloadTooLargeException();
            ms.Write(buffer, 0, read);
        }
        if (ms.Length == 0) return null;
        ms.Position = 0;
        return await System.Text.Json.JsonSerializer.DeserializeAsync<SearchLogPayload>(ms,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase },
            ct);
    }

    private sealed class PayloadTooLargeException : Exception { }

    /// <summary>
    /// Wire format. <c>kind</c> selects the union variant: "search" reads
    /// <c>resultCount</c>; "click" reads <c>rank</c> and the optional
    /// <c>originalBucketUtc</c> attribution timestamp.
    /// </summary>
    internal sealed class SearchLogPayload
    {
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("phrase")] public string? Phrase { get; set; }
        [JsonPropertyName("channelKey")] public string? ChannelKey { get; set; }
        [JsonPropertyName("locale")] public string? Locale { get; set; }
        [JsonPropertyName("resultCount")] public int? ResultCount { get; set; }
        [JsonPropertyName("rank")] public int? Rank { get; set; }
        [JsonPropertyName("ts")] public DateTime? TimestampUtc { get; set; }
        [JsonPropertyName("originalBucketUtc")] public DateTime? OriginalBucketUtc { get; set; }
    }
}
