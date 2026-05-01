using EPiServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;

/// <summary>
/// REST API for the Pinned Results tool. Mounted under
/// <c>{basePath}/PinnedApi/{action}</c> by the convention route. Writes require
/// the X-Requested-With header (CSRF mitigation) and per-action access checks
/// honour the optional per-feature permission gate.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class PinnedApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Pinned);

    private readonly PinnedService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<PinnedApiController> _logger;

    public PinnedApiController(
        PinnedService service,
        FeatureAccessChecker accessChecker,
        ILogger<PinnedApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Collections(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return Ok(await _service.GetCollectionsAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> CreateCollection([FromBody] PinnedCollectionPayload payload, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (payload == null) return BadRequest(new { message = "Collection payload is required." });
        try
        {
            return Ok(await _service.CreateCollectionAsync(payload, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPut]
    [RequireAjax]
    public async Task<IActionResult> UpdateCollection(string id, [FromBody] PinnedCollectionUpdatePayload payload, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Collection id is required." });
        if (payload == null) return BadRequest(new { message = "Collection payload is required." });
        try
        {
            return Ok(await _service.UpdateCollectionAsync(id, payload, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> DeleteCollection(string id, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Collection id is required." });
        try
        {
            await _service.DeleteCollectionAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Items(string collectionId, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        try
        {
            return Ok(await _service.GetItemsAsync(collectionId, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> CreateItem(string collectionId, [FromBody] PinnedItemPayload payload, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        if (payload == null) return BadRequest(new { message = "Item payload is required." });
        try
        {
            return Ok(await _service.CreateItemAsync(collectionId, payload, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPut]
    [RequireAjax]
    public async Task<IActionResult> UpdateItem(string collectionId, string id, [FromBody] PinnedItemPayload payload, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }
        if (payload == null) return BadRequest(new { message = "Item payload is required." });
        try
        {
            return Ok(await _service.UpdateItemAsync(collectionId, id, payload, cancellationToken));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> DeleteItem(string collectionId, string id, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }
        try
        {
            await _service.DeleteItemAsync(collectionId, id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Pinned);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Graph API request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph admin request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in PinnedApiController.");
        return Problem(title: "Pinned API request failed.");
    }
}
