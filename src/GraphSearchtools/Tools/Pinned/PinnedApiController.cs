using EPiServer.Framework.Localization;
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
/// <remarks>
/// Phase 2.5 §4.1: pinned data is now scoped to <see cref="SearchProfile"/>s.
/// When at least one profile is registered (i.e. <c>registry.All.Count &gt; 1</c>
/// — Generic is always synthesised), every write requires a <c>profileKey</c>
/// query parameter. The controller resolves the Graph collection key from the
/// profile + locale via <see cref="SearchProfile.PinnedKeyForLocale"/>; the
/// marketer never types the key. Generic-mode (only the synthesised profile
/// exists) keeps the legacy free-form <c>collectionName</c>/<c>collectionId</c>
/// shape for back-compat with installs that haven't adopted profiles yet.
/// </remarks>
[Authorize(Policy = "codeart:graphsearchtools")]
public class PinnedApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Pinned);

    private readonly PinnedService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ISearchProfileRegistry _registry;
    private readonly SearchProfileEditService _editLog;
    private readonly LocalizationService _localization;
    private readonly ILogger<PinnedApiController> _logger;

    public PinnedApiController(
        PinnedService service,
        FeatureAccessChecker accessChecker,
        ISearchProfileRegistry registry,
        SearchProfileEditService editLog,
        LocalizationService localization,
        ILogger<PinnedApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _registry = registry;
        _editLog = editLog;
        _localization = localization;
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
    public async Task<IActionResult> Items(
        string collectionId,
        [FromQuery] int offset,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        if (offset < 0) return BadRequest(new { message = "offset must be >= 0." });
        try
        {
            return Ok(await _service.GetItemsAsync(collectionId, cancellationToken, offset));
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// Walks ContentGraph's 20-item pages and returns the full pin list with
    /// a real total. The Aurora grid calls this on tab open so client-side
    /// search/filter/paging has the whole dataset to work with.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AllItems(string collectionId, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        try
        {
            var items = await _service.LoadAllItemsAsync(collectionId, cancellationToken);
            return Ok(new { items, total = items.Count });
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> CreateItem(
        string collectionId,
        [FromBody] PinnedItemPayload payload,
        [FromQuery] string? profileKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        if (payload == null) return BadRequest(new { message = "Item payload is required." });

        var scope = ResolveScope(profileKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            var result = await _service.CreateItemAsync(collectionId, payload, cancellationToken);
            AppendAudit(scope, action: "Created", subject: payload.Phrases);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPut]
    [RequireAjax]
    public async Task<IActionResult> UpdateItem(
        // [FromQuery] is explicit because the convention route is
        // `{controller}/{action}/{id?}` — without it, the model binder reads
        // `id` from the empty route token and never falls through to the
        // query string, producing a 400 "id required" even when ?id=… is set.
        [FromQuery] string collectionId,
        [FromQuery] string id,
        [FromBody] PinnedItemPayload payload,
        [FromQuery] string? profileKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }
        if (payload == null) return BadRequest(new { message = "Item payload is required." });

        var scope = ResolveScope(profileKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            var result = await _service.UpdateItemAsync(collectionId, id, payload, cancellationToken);
            AppendAudit(scope, action: "Updated", subject: payload.Phrases);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> DeleteItem(
        // See UpdateItem: [FromQuery] needed to bypass the route's `{id?}` token.
        [FromQuery] string collectionId,
        [FromQuery] string id,
        [FromQuery] string? profileKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        [FromQuery] string? phrases,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }

        var scope = ResolveScope(profileKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            await _service.DeleteItemAsync(collectionId, id, cancellationToken);
            AppendAudit(scope, action: "Deleted", subject: phrases ?? id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Pinned);

    /// <summary>
    /// Resolves the (profile, site, locale) tuple from the query string against
    /// the registry. When at least one real profile is registered (i.e. the
    /// registry exposes more than just the synthesised Generic), <c>profileKey</c>
    /// is required — writes without it are 400'd. Generic-only mode keeps the
    /// legacy free-form behaviour: <c>profile</c> is null and the caller's
    /// <c>collectionId</c>/<c>collectionName</c> drives the Graph call directly.
    /// </summary>
    private ScopeResolution ResolveScope(string? profileKey, string? site, string? locale)
    {
        var hasRegisteredProfiles = _registry.All.Count > 1;

        if (string.IsNullOrWhiteSpace(profileKey))
        {
            if (hasRegisteredProfiles)
            {
                var msg = _localization.GetString("/graphsearchtools/profiles/api/profileKeyRequired");
                return ScopeResolution.Error(BadRequest(new { message = msg }));
            }

            // Generic-only mode: legacy behaviour, no profile context.
            return ScopeResolution.Generic(site, locale);
        }

        var profile = _registry.Get(profileKey!);
        if (profile == null)
        {
            return ScopeResolution.Error(NotFound(new { message = "Profile not found." }));
        }

        return ScopeResolution.For(profile, site, locale);
    }

    private void AppendAudit(ScopeResolution scope, string action, string subject)
    {
        try
        {
            var entry = new SearchProfileEdit
            {
                ProfileKey = scope.Profile?.Key ?? "generic",
                Site = scope.Site ?? string.Empty,
                Locale = scope.Locale ?? string.Empty,
                Kind = "Pinned",
                Action = action,
                Subject = subject ?? string.Empty,
                ActorId = HttpContext.User.Identity?.Name ?? string.Empty,
                ActorName = HttpContext.User.Identity?.Name ?? string.Empty,
                At = DateTime.UtcNow
            };
            _editLog.Append(entry);
        }
        catch (Exception ex)
        {
            // Audit failure must not break the user's edit. Log and move on.
            _logger.LogWarning(ex, "Failed to append SearchProfileEdit row for Pinned action {Action}.", action);
        }
    }

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body — log it and return a generic error.
            _logger.LogWarning(apiException, "Graph API request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is BulkLoadCapExceededException cap)
        {
            _logger.LogWarning(cap, "Bulk load cap exceeded for collection {CollectionId}.", cap.CollectionId);
            return StatusCode(503, new { message = "Collection too large to load in one request.", cap = cap.Cap });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph admin request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }

        _logger.LogError(exception, "Unhandled error in PinnedApiController.");
        return Problem(title: "Pinned API request failed.");
    }

    /// <summary>
    /// Internal carrier for the resolved scope. Holds either the (profile, site,
    /// locale) tuple — used to write the audit-log entry — or an <see cref="IActionResult"/>
    /// the caller should return immediately (400 / 404).
    /// </summary>
    private sealed class ScopeResolution
    {
        public SearchProfile? Profile { get; private init; }
        public string? Site { get; private init; }
        public string? Locale { get; private init; }
        public IActionResult? ErrorResult { get; private init; }
        public bool IsError => ErrorResult != null;

        public static ScopeResolution For(SearchProfile profile, string? site, string? locale)
            => new() { Profile = profile, Site = site, Locale = locale };

        public static ScopeResolution Generic(string? site, string? locale)
            => new() { Profile = null, Site = site, Locale = locale };

        public static ScopeResolution Error(IActionResult error)
            => new() { ErrorResult = error };
    }
}
