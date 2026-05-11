using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit;

/// <summary>
/// Phase 4 — REST surface for the Content Searchability Audit. The single
/// endpoint runs the local CMS scan inline and returns the result. We use
/// <c>POST</c> to make the on-demand cost explicit (a refresh of the page
/// shouldn't accidentally re-scan a 10k-page site) and gate it with
/// <see cref="RequireAjaxAttribute"/> so it can't be triggered by a stray
/// browser navigation. Issues are capped at
/// <see cref="AuditResult.ItemCap"/> per kind upstream.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public sealed class ContentSearchabilityAuditApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.ContentSearchabilityAudit);

    private readonly ContentSearchabilityAuditService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<ContentSearchabilityAuditApiController> _logger;

    public ContentSearchabilityAuditApiController(
        ContentSearchabilityAuditService service,
        FeatureAccessChecker accessChecker,
        ILogger<ContentSearchabilityAuditApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult Run(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        try
        {
            var result = _service.Run(cancellationToken);
            return Ok(new
            {
                status = "completed",
                scannedAt = result.ScannedAt,
                elapsedMs = result.ElapsedMs,
                itemsScanned = result.ItemsScanned,
                truncated = result.Truncated,
                items = result.Issues
            });
        }
        catch (OperationCanceledException)
        {
            // Client navigated away — no body.
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Content searchability audit failed.");
            return Problem(title: "Content searchability audit failed.");
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.ContentSearchabilityAudit);
}
