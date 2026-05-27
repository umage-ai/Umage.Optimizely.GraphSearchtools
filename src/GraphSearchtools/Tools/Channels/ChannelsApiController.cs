using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.Channels.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Channels;

/// <summary>
/// JSON API for the Channels surface. Read-only in v1: writes for pinned /
/// synonym / saved-query data go through their existing controllers — see
/// docs/search-channels-design.md §4.1–§4.3.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
[Route("EPiServer/cms/graphsearchtools/api/channels")]
internal class ChannelsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Channels);

    private readonly ChannelsService _service;
    private readonly ISearchChannelRegistry _registry;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly PinnedService _pinnedService;
    private readonly ILogger<ChannelsApiController> _logger;

    public ChannelsApiController(
        ChannelsService service,
        ISearchChannelRegistry registry,
        FeatureAccessChecker accessChecker,
        PinnedService pinnedService,
        ILogger<ChannelsApiController> logger)
    {
        _service = service;
        _registry = registry;
        _accessChecker = accessChecker;
        _pinnedService = pinnedService;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult List()
    {
        if (!HasAccess()) return Forbid();
        try { return Ok(_service.ListSummaries()); }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpGet("{key}")]
    public IActionResult Get(string key)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Channel key is required." });
        try
        {
            var detail = _service.BuildDetail(key);
            return detail == null ? NotFound() : Ok(detail);
        }
        catch (Exception ex) { return Handle(ex); }
    }

    /// <summary>
    /// Pinned items for a channel + (site, locale) combination. The collection
    /// key is resolved via <see cref="SearchChannel.PinnedKeyForLocale"/>;
    /// channels without a formula return 400 since they cannot scope writes.
    /// </summary>
    [HttpGet("{key}/pinned")]
    public async Task<IActionResult> Pinned(string key, [FromQuery] string? site, [FromQuery] string? locale, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Channel key is required." });

        var channel = _registry.Get(key);
        if (channel == null) return NotFound();
        if (channel.PinnedKeyForLocale == null)
        {
            return BadRequest(new { message = $"Channel '{channel.Key}' has no PinnedKey formula." });
        }

        try
        {
            var collections = await _pinnedService.GetCollectionsAsync(cancellationToken);
            var resolvedKey = channel.PinnedKeyForLocale(locale ?? string.Empty);
            var matching = collections.FirstOrDefault(c =>
                string.Equals(c.Key, resolvedKey, StringComparison.OrdinalIgnoreCase));

            if (matching == null)
            {
                return Ok(new ChannelPinnedResponse
                {
                    ChannelKey = channel.Key,
                    Site = site,
                    Locale = locale,
                    PinnedKey = resolvedKey,
                    CollectionId = null,
                    Rows = Array.Empty<ChannelPinnedRow>()
                });
            }

            var rows = (await _pinnedService.GetItemsAsync(matching.Id, cancellationToken))
                .Select(it => ChannelPinnedRow.From(matching, it))
                .ToList();

            return Ok(new ChannelPinnedResponse
            {
                ChannelKey = channel.Key,
                Site = site,
                Locale = locale,
                PinnedKey = resolvedKey,
                CollectionId = matching.Id,
                Rows = rows
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Handle(ex); }
    }

    /// <summary>
    /// Pinned-tab Try-it preview. Runs the channel's registered GraphQL
    /// document (with <c>$phrase</c> / <c>$pinnedCollectionId</c> substitutions)
    /// against Graph and returns the hits. Independent of the
    /// <c>SavedQueries.DefaultQuery</c> runner so a tenant-specific config
    /// can't break previews on other channels.
    /// </summary>
    [HttpGet("{key}/preview")]
    public async Task<IActionResult> Preview(string key, [FromQuery] string? phrase, [FromQuery] string? locale, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Channel key is required." });
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Trim().Length < 2)
        {
            // Surface a uniform empty payload for short-or-blank phrases so
            // the client-side debouncer doesn't have to special-case 400.
            return Ok(new { hits = Array.Empty<object>(), totalCount = 0, durationMs = 0L });
        }
        try
        {
            var result = await _service.RunPreviewAsync(key, phrase!.Trim(), locale, cancellationToken);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Handle(ex); }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Channels);

    private IActionResult Handle(Exception ex)
    {
        _logger.LogError(ex, "Channels API error.");
        return Problem(title: "Channels request failed.");
    }
}
