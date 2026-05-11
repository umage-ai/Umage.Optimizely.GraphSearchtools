using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks;

/// <summary>
/// REST API for the Webhooks tool. Mounted under
/// <c>{basePath}/WebhooksApi/{action}</c> by the convention route. List/create/
/// delete only — Graph's webhook admin API doesn't expose update, so the UI
/// surfaces "edit = delete + recreate".
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class WebhooksApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Webhooks);

    private readonly WebhooksService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<WebhooksApiController> _logger;

    public WebhooksApiController(
        WebhooksService service,
        FeatureAccessChecker accessChecker,
        ILogger<WebhooksApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.ListAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Create([FromBody] WebhookCreateRequest? request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        if (request == null || string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new { message = "URL is required." });
        }

        if (!IsValidWebhookUrl(request.Url))
        {
            return BadRequest(new { message = "URL must be an absolute https:// (or http://) address." });
        }

        if (!IsAllowedMethod(request.Method))
        {
            return BadRequest(new { message = "Method must be POST or PUT." });
        }

        try
        {
            return Ok(await _service.CreateAsync(request, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> Delete([FromQuery] string id, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Webhook id is required." });
        }

        try
        {
            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Webhooks);

    /// <summary>
    /// Webhooks fire from Graph to a public host, so we require an absolute
    /// URL with an http(s) scheme. We accept http for back-compat with on-prem
    /// staging hosts but the page UI should warn loudly.
    /// </summary>
    private static bool IsValidWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
    }

    private static bool IsAllowedMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method)) return true; // service defaults to POST
        var normalized = method.Trim().ToUpperInvariant();
        return normalized == "POST" || normalized == "PUT";
    }

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Graph webhooks request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph webhooks request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
        if (exception is ArgumentException argEx)
        {
            _logger.LogWarning(argEx, "Webhook request rejected: {Reason}.", argEx.Message);
            return BadRequest(new { message = "Invalid webhook request." });
        }

        _logger.LogError(exception, "Unhandled error in WebhooksApiController.");
        return Problem(title: "Webhooks API request failed.");
    }
}
