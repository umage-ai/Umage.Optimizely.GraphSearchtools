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
/// Phase 2.5 §4.1: pinned data is scoped to <see cref="SearchChannel"/>s.
/// Every write requires a <c>channelKey</c> query parameter; the controller
/// resolves the Graph collection key from the channel + locale via
/// <see cref="SearchChannel.PinnedKeyForLocale"/> so the marketer never types
/// the key.
/// </remarks>
[Authorize(Policy = "umageai:graphsearchtools")]
public class PinnedApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Pinned);

    private readonly PinnedService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ISearchChannelRegistry _registry;
    private readonly AuditLogService _audit;
    private readonly LocalizationService _localization;
    private readonly CmsLocaleResolver _localeResolver;
    private readonly ILogger<PinnedApiController> _logger;

    public PinnedApiController(
        PinnedService service,
        FeatureAccessChecker accessChecker,
        ISearchChannelRegistry registry,
        AuditLogService audit,
        LocalizationService localization,
        CmsLocaleResolver localeResolver,
        ILogger<PinnedApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _registry = registry;
        _audit = audit;
        _localization = localization;
        _localeResolver = localeResolver;
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

    /// <summary>
    /// Returns the <c>{ collectionKey: channelKey }</c> lookup the top-level
    /// Pinned grid uses for row deep-links. First-write-wins on overlap; for
    /// the richer "all matching channels + their locales" payload that the
    /// Collections tab uses, see <see cref="CollectionChannels"/>.
    /// </summary>
    [HttpGet]
    public IActionResult ChannelMap()
    {
        if (!HasAccess()) return Forbid();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in EnumerateChannelMatches())
        {
            map.TryAdd(match.CollectionKey, match.ChannelKey);
        }
        return Ok(map);
    }

    /// <summary>
    /// Returns the inverse of <see cref="ChannelMap"/>: every registered
    /// channel × declared locale tuple grouped by the collection key its
    /// <see cref="SearchChannel.PinnedKeyForLocale"/> resolves to. Used by the
    /// Collections-tab flyout to show "which channels point at this
    /// collection and through which locales."
    /// </summary>
    [HttpGet]
    public IActionResult CollectionChannels()
    {
        if (!HasAccess()) return Forbid();

        var map = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in EnumerateChannelMatches())
        {
            if (!map.TryGetValue(match.CollectionKey, out var list))
            {
                list = new List<object>();
                map[match.CollectionKey] = list;
            }
            list.Add(new { channelKey = match.ChannelKey, locale = match.Locale });
        }
        return Ok(map);
    }

    /// <summary>
    /// Resolves the (channel, locale) tuple to its Graph collection, creating
    /// the collection if it doesn't exist yet. Returns the resolved key and
    /// the collection id (existing or newly created). Used by the Channel
    /// detail Pinned tab so editors can add a pin under a declared locale
    /// without manually pre-creating the backing collection.
    /// </summary>
    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> EnsureCollection(
        [FromQuery] string channelKey,
        [FromQuery] string locale,
        CancellationToken cancellationToken)
    {
        if (!HasCollectionEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(channelKey)) return BadRequest(new { message = "channelKey is required." });
        if (string.IsNullOrWhiteSpace(locale)) return BadRequest(new { message = "locale is required." });

        var channel = _registry.Get(channelKey);
        if (channel == null) return NotFound(new { message = "Channel not found." });
        if (channel.PinnedKeyForLocale == null)
        {
            return BadRequest(new { message = $"Channel '{channel.Key}' has no PinnedKey formula." });
        }

        string? resolvedKey;
        try { resolvedKey = channel.PinnedKeyForLocale(locale); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PinnedKeyForLocale threw for channel {Channel} locale {Locale}.", channel.Key, locale);
            return BadRequest(new { message = "Channel's PinnedKey formula failed for this locale." });
        }
        if (string.IsNullOrEmpty(resolvedKey))
        {
            return BadRequest(new { message = "Channel's PinnedKey formula returned no key for this locale." });
        }

        try
        {
            var collections = await _service.GetCollectionsAsync(cancellationToken);
            var existing = collections.FirstOrDefault(c =>
                string.Equals(c.Key, resolvedKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return Ok(new { collectionId = existing.Id, key = existing.Key, created = false });
            }

            var created = await _service.CreateCollectionAsync(
                new PinnedCollectionPayload { Key = resolvedKey!, IsActive = true },
                cancellationToken);
            return Ok(new { collectionId = created.Id, key = created.Key, created = true });
        }
        catch (Exception ex) { return HandleError(ex); }
    }

    private IEnumerable<(string CollectionKey, string ChannelKey, string Locale)> EnumerateChannelMatches()
    {
        foreach (var channel in _registry.All)
        {
            if (channel.PinnedKeyForLocale == null) continue;
            // Use the resolver instead of channel.Locales directly so channels
            // that opt into LocalesFromCmsLanguages still get a populated list.
            var resolved = _localeResolver.Resolve(channel);
            var locales = resolved.Count > 0
                ? (IReadOnlyList<string>)resolved
                : new[] { "en" };
            foreach (var locale in locales)
            {
                string? key;
                try { key = channel.PinnedKeyForLocale(locale); }
                catch { key = null; }
                if (string.IsNullOrEmpty(key)) continue;
                yield return (key!, channel.Key, locale);
            }
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> CreateCollection([FromBody] PinnedCollectionPayload payload, CancellationToken cancellationToken)
    {
        if (!HasCollectionEditAccess()) return Forbid();
        if (payload == null) return BadRequest(new { message = "Collection payload is required." });
        try
        {
            var result = await _service.CreateCollectionAsync(payload, cancellationToken);
            AppendCollectionAudit(action: "Created", collectionKey: result?.Key ?? payload.Key);
            return Ok(result);
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
        if (!HasCollectionEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Collection id is required." });
        if (payload == null) return BadRequest(new { message = "Collection payload is required." });
        try
        {
            var result = await _service.UpdateCollectionAsync(id, payload, cancellationToken);
            AppendCollectionAudit(action: "Updated", collectionKey: result?.Key ?? payload.Key ?? id);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> DeleteCollection(
        // [FromQuery] is explicit because the convention route is
        // `{controller}/{action}/{id?}` — without it, the binder reads
        // the empty route token and 400s before the query string is
        // considered. Same wart as UpdateItem above.
        [FromQuery] string id,
        CancellationToken cancellationToken)
    {
        if (!HasCollectionEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Collection id is required." });
        try
        {
            // Resolve the human-readable key *before* the delete so the
            // audit row records something useful — after the DELETE the
            // collection is gone and the lookup would return empty.
            var collectionKey = await ResolveCollectionKeyAsync(id, cancellationToken);
            await _service.DeleteCollectionAsync(id, cancellationToken);
            AppendCollectionAudit(action: "Deleted", collectionKey: string.IsNullOrEmpty(collectionKey) ? id : collectionKey);
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
        [FromQuery] string? channelKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (!HasItemEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId)) return BadRequest(new { message = "collectionId is required." });
        if (payload == null) return BadRequest(new { message = "Item payload is required." });

        var scope = ResolveScope(channelKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            var result = await _service.CreateItemAsync(collectionId, payload, cancellationToken);
            var collectionKey = await ResolveCollectionKeyAsync(collectionId, cancellationToken);
            AppendAudit(scope, action: "Created", subject: payload.Phrases, collectionKey: collectionKey);
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
        [FromQuery] string? channelKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (!HasItemEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }
        if (payload == null) return BadRequest(new { message = "Item payload is required." });

        var scope = ResolveScope(channelKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            var result = await _service.UpdateItemAsync(collectionId, id, payload, cancellationToken);
            var collectionKey = await ResolveCollectionKeyAsync(collectionId, cancellationToken);
            AppendAudit(scope, action: "Updated", subject: payload.Phrases, collectionKey: collectionKey);
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
        [FromQuery] string? channelKey,
        [FromQuery] string? site,
        [FromQuery] string? locale,
        [FromQuery] string? phrases,
        CancellationToken cancellationToken)
    {
        if (!HasItemEditAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(collectionId) || string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "collectionId and id are required." });
        }

        var scope = ResolveScope(channelKey, site, locale);
        if (scope.IsError) return scope.ErrorResult!;

        try
        {
            await _service.DeleteItemAsync(collectionId, id, cancellationToken);
            var collectionKey = await ResolveCollectionKeyAsync(collectionId, cancellationToken);
            AppendAudit(scope, action: "Deleted", subject: phrases ?? id, collectionKey: collectionKey);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Pinned);

    private bool HasItemEditAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.PinnedEdit);

    private bool HasCollectionEditAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Collections);

    /// <summary>
    /// Resolves the (channel, site, locale) tuple from the query string against
    /// the registry. <c>channelKey</c> is optional — the top-level Pinned
    /// page's flyout writes directly against a collection and may have no
    /// owning channel to attribute the audit row to, in which case
    /// <see cref="AppendAudit"/> is skipped.
    /// </summary>
    private ScopeResolution ResolveScope(string? channelKey, string? site, string? locale)
    {
        if (string.IsNullOrWhiteSpace(channelKey))
        {
            return ScopeResolution.For(channel: null, site, locale);
        }

        var channel = _registry.Get(channelKey!);
        if (channel == null)
        {
            return ScopeResolution.Error(NotFound(new { message = "Channel not found." }));
        }

        return ScopeResolution.For(channel, site, locale);
    }

    private void AppendCollectionAudit(string action, string collectionKey)
    {
        // Collection events are not scoped to a channel — collections can
        // serve any number of channels via PinnedKeyForLocale. ChannelKey is
        // left empty so the global changelog shows these as standalone.
        try
        {
            var entry = new AuditLogEntry
            {
                Kind = "Collection",
                Action = action,
                Subject = collectionKey ?? string.Empty,
                CollectionKey = collectionKey ?? string.Empty,
                ActorId = HttpContext.User.Identity?.Name ?? string.Empty,
                ActorName = HttpContext.User.Identity?.Name ?? string.Empty,
                At = DateTime.UtcNow
            };
            _audit.Append(entry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append audit row for Collection action {Action}.", action);
        }
    }

    /// <summary>
    /// Best-effort lookup of a collection's human-readable key for the audit
    /// row. The pinned item write path only carries the collection id; the
    /// changelog UI wants the key. Returns an empty string on lookup failure
    /// — never blocks the user's edit.
    /// </summary>
    private async Task<string> ResolveCollectionKeyAsync(string collectionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(collectionId)) return string.Empty;
        try
        {
            var collections = await _service.GetCollectionsAsync(cancellationToken);
            return collections.FirstOrDefault(c => string.Equals(c.Id, collectionId, StringComparison.OrdinalIgnoreCase))?.Key
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void AppendAudit(ScopeResolution scope, string action, string subject, string? collectionKey = null)
    {
        // Pin events are always recorded — even direct-collection writes
        // (no channelKey) need to show up in the global changelog. When the
        // edit comes through a channel-scoped flyout we record the channel
        // key too so the Channels index's "last edited" hint still works.
        try
        {
            var entry = new AuditLogEntry
            {
                Kind = "PinnedItem",
                Action = action,
                Subject = subject ?? string.Empty,
                ChannelKey = scope.Channel?.Key ?? string.Empty,
                Site = scope.Site ?? string.Empty,
                Locale = scope.Locale ?? string.Empty,
                CollectionKey = collectionKey ?? string.Empty,
                ActorId = HttpContext.User.Identity?.Name ?? string.Empty,
                ActorName = HttpContext.User.Identity?.Name ?? string.Empty,
                At = DateTime.UtcNow
            };
            _audit.Append(entry);
        }
        catch (Exception ex)
        {
            // Audit failure must not break the user's edit. Log and move on.
            _logger.LogWarning(ex, "Failed to append audit row for Pinned action {Action}.", action);
        }
    }

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            // Don't leak the upstream response body to the HTTP response —
            // but DO log it server-side so the developer can diagnose the
            // upstream rejection without having to wireshark Graph traffic.
            _logger.LogWarning(apiException,
                "Graph API request failed with status {StatusCode}. Body: {Body}",
                apiException.StatusCode, apiException.ResponseContent);
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
    /// Internal carrier for the resolved scope. Holds either the (channel, site,
    /// locale) tuple — used to write the audit-log entry — or an <see cref="IActionResult"/>
    /// the caller should return immediately (400 / 404).
    /// </summary>
    private sealed class ScopeResolution
    {
        public SearchChannel? Channel { get; private init; }
        public string? Site { get; private init; }
        public string? Locale { get; private init; }
        public IActionResult? ErrorResult { get; private init; }
        public bool IsError => ErrorResult != null;

        public static ScopeResolution For(SearchChannel? channel, string? site, string? locale)
            => new() { Channel = channel, Site = site, Locale = locale };

        public static ScopeResolution Error(IActionResult error)
            => new() { ErrorResult = error };
    }
}
