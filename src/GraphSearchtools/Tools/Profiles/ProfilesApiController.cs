using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Pinned;
using UmageAI.Optimizely.GraphSearchTools.Tools.Profiles.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Profiles;

/// <summary>
/// JSON API for the Profiles surface. Read-only in v1: writes for pinned /
/// synonym / saved-query data go through their existing controllers — see
/// docs/search-profiles-design.md §4.1–§4.3.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
[Route("EPiServer/cms/graphsearchtools/api/profiles")]
public class ProfilesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Profiles);

    private readonly ProfilesService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ISearchProfileRegistry _registry;
    private readonly PinnedService _pinnedService;
    private readonly ILogger<ProfilesApiController> _logger;

    public ProfilesApiController(
        ProfilesService service,
        FeatureAccessChecker accessChecker,
        ISearchProfileRegistry registry,
        PinnedService pinnedService,
        ILogger<ProfilesApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _registry = registry;
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
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Profile key is required." });
        try
        {
            var detail = _service.BuildDetail(key);
            return detail == null ? NotFound() : Ok(detail);
        }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpGet("{key}/audit")]
    public IActionResult Audit(string key, [FromQuery] int take = 100)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Profile key is required." });
        var clamped = Math.Clamp(take, 1, 500);
        try { return Ok(_service.ListAudit(key, clamped)); }
        catch (Exception ex) { return Handle(ex); }
    }

    /// <summary>
    /// Pinned items for a profile + (site, locale) combination. The collection
    /// key is resolved via <see cref="SearchProfile.PinnedKeyForLocale"/>; for
    /// the synthesised Generic profile (which has no formula) we return all
    /// items across every collection so the legacy free-form view keeps
    /// working — see design doc §5.
    /// </summary>
    [HttpGet("{key}/pinned")]
    public async Task<IActionResult> Pinned(string key, [FromQuery] string? site, [FromQuery] string? locale, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Profile key is required." });

        var profile = _registry.Get(key);
        if (profile == null) return NotFound();

        try
        {
            var collections = await _pinnedService.GetCollectionsAsync(cancellationToken);

            // Generic / no formula → return everything for the free-form editor.
            if (profile.PinnedKeyForLocale == null)
            {
                var all = new List<ProfilePinnedRow>();
                foreach (var col in collections)
                {
                    var items = await _pinnedService.GetItemsAsync(col.Id, cancellationToken);
                    foreach (var item in items)
                    {
                        all.Add(ProfilePinnedRow.From(col, item));
                    }
                }
                return Ok(new ProfilePinnedResponse
                {
                    ProfileKey = profile.Key,
                    Site = site,
                    Locale = locale,
                    PinnedKey = null,
                    CollectionId = null,
                    IsGeneric = true,
                    Rows = all
                });
            }

            var resolvedKey = profile.PinnedKeyForLocale(locale ?? string.Empty);
            var matching = collections.FirstOrDefault(c =>
                string.Equals(c.Key, resolvedKey, StringComparison.OrdinalIgnoreCase));

            if (matching == null)
            {
                // Collection doesn't exist yet — return empty so the UI can render the editor.
                return Ok(new ProfilePinnedResponse
                {
                    ProfileKey = profile.Key,
                    Site = site,
                    Locale = locale,
                    PinnedKey = resolvedKey,
                    CollectionId = null,
                    IsGeneric = false,
                    Rows = Array.Empty<ProfilePinnedRow>()
                });
            }

            var rows = (await _pinnedService.GetItemsAsync(matching.Id, cancellationToken))
                .Select(it => ProfilePinnedRow.From(matching, it))
                .ToList();

            return Ok(new ProfilePinnedResponse
            {
                ProfileKey = profile.Key,
                Site = site,
                Locale = locale,
                PinnedKey = resolvedKey,
                CollectionId = matching.Id,
                IsGeneric = false,
                Rows = rows
            });
        }
        catch (Exception ex) { return Handle(ex); }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Profiles);

    private IActionResult Handle(Exception ex)
    {
        _logger.LogError(ex, "Profiles API error.");
        return Problem(title: "Profiles request failed.");
    }
}
