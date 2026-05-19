using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.AuditLog;

/// <summary>
/// Read-only API for the global changelog. Pinned and Synonyms tools each
/// surface a "Changelog" tab backed by <see cref="Recent"/>; both pass the
/// kinds they care about via the <c>kind</c> CSV query param.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class AuditLogApiController : Controller
{
    private readonly AuditLogService _audit;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<AuditLogApiController> _logger;

    public AuditLogApiController(
        AuditLogService audit,
        FeatureAccessChecker accessChecker,
        ILogger<AuditLogApiController> logger)
    {
        _audit = audit;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    /// <summary>
    /// <c>GET AuditLogApi/Recent?take=200&amp;kind=PinnedItem,Collection</c>.
    /// When <c>kind</c> is omitted, returns rows of every kind. The take
    /// param is clamped to <c>[1, 1000]</c>.
    /// </summary>
    [HttpGet]
    public IActionResult Recent([FromQuery] int take = 200, [FromQuery] string? kind = null)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var kinds = ParseKinds(kind);
            var rows = _audit.ListRecent(take, kinds);
            // Project to a stable shape — DDS-serialized records include
            // internal columns we don't want to bleed through the API.
            return Ok(rows.Select(AuditLogDto.From).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AuditLog API request failed.");
            return Problem(title: "AuditLog request failed.");
        }
    }

    private static IReadOnlyCollection<string>? ParseKinds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    /// <summary>
    /// Audit-log access piggybacks on Pinned OR Synonyms — the two surfaces
    /// that render the changelog. A tenant that disables both has implicitly
    /// disabled the changelog too.
    /// </summary>
    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Pinned), GraphSearchtoolsPermissions.Pinned)
        || _accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Synonyms), GraphSearchtoolsPermissions.Synonyms);
}

/// <summary>JSON-friendly projection of <see cref="AuditLogEntry"/>.</summary>
public sealed record AuditLogDto
{
    public DateTime At { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string ActorId { get; init; } = string.Empty;
    public string ActorName { get; init; } = string.Empty;
    public string ChannelKey { get; init; } = string.Empty;
    public string Site { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public string CollectionKey { get; init; } = string.Empty;

    public static AuditLogDto From(AuditLogEntry e) => new()
    {
        At = e.At,
        Kind = e.Kind,
        Action = e.Action,
        Subject = e.Subject,
        ActorId = e.ActorId,
        ActorName = e.ActorName,
        ChannelKey = e.ChannelKey,
        Site = e.Site,
        Locale = e.Locale,
        Slot = e.Slot,
        CollectionKey = e.CollectionKey
    };
}
