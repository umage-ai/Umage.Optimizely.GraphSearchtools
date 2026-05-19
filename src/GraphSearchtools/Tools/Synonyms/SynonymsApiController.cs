using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

/// <summary>
/// REST API for the Synonyms tool. Each language has its own synonym blob
/// addressed by <c>language_routing</c>; the "Global" blob is reached by
/// passing no language. The wire format with Optimizely Graph is plain text —
/// the line-per-rule structure is enforced client-side and joined here.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SynonymsApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Synonyms);

    private readonly SynonymsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly AuditLogService _audit;
    private readonly ILogger<SynonymsApiController> _logger;

    public SynonymsApiController(
        SynonymsService service,
        FeatureAccessChecker accessChecker,
        AuditLogService audit,
        ILogger<SynonymsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _audit = audit;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? languageRouting,
        [FromQuery] string? sourceRouting,
        [FromQuery] string? slot,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var query = new SynonymsQuery
        {
            LanguageRouting = languageRouting,
            SourceRouting = sourceRouting,
            Slot = slot
        };

        try
        {
            var content = await _service.GetAsync(query, cancellationToken);
            return Ok(new SynonymsResponse { Content = content ?? string.Empty });
        }
        catch (GraphSearchApiException ex) when (ex.StatusCode == 404)
        {
            // No synonym set for this scope — return empty.
            return Ok(new SynonymsResponse { Content = string.Empty });
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpPut]
    [RequireAjax]
    public async Task<IActionResult> Update([FromBody] SynonymsRequest request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Synonyms payload is required." });

        // Read the previous blob *before* the PUT so we can diff it against
        // the incoming content. The synonyms PUT replaces a whole blob —
        // tracking adds/removes per-line gives the changelog rule-level
        // granularity without storing every line on every save.
        var query = new SynonymsQuery
        {
            LanguageRouting = request.LanguageRouting,
            SourceRouting = request.SourceRouting,
            Slot = request.Slot
        };
        var previous = await SafeGetAsync(query, cancellationToken);

        try
        {
            await _service.UpdateAsync(request, cancellationToken);
            AppendSynonymDiff(previous, request.Content ?? string.Empty, query);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    [HttpDelete]
    [RequireAjax]
    public async Task<IActionResult> Delete(
        [FromQuery] string? languageRouting,
        [FromQuery] string? sourceRouting,
        [FromQuery] string? slot,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var query = new SynonymsQuery
        {
            LanguageRouting = languageRouting,
            SourceRouting = sourceRouting,
            Slot = slot
        };

        // DELETE wipes the slot — every rule that was in it becomes a
        // "Deleted" row in the audit log.
        var previous = await SafeGetAsync(query, cancellationToken);

        try
        {
            await _service.DeleteAsync(query, cancellationToken);
            AppendSynonymDiff(previous, string.Empty, query);
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    /// <summary>
    /// GET wrapped so the read-before-write swallows the "scope doesn't
    /// exist yet" 404 (common on a brand-new locale/slot) and treats it as
    /// an empty blob. Other failures fall through to an empty string too —
    /// audit is best-effort and must never block the write.
    /// </summary>
    private async Task<string> SafeGetAsync(SynonymsQuery query, CancellationToken cancellationToken)
    {
        try { return await _service.GetAsync(query, cancellationToken) ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Emit one audit row per added / removed rule. The synonyms blob is
    /// line-per-rule; tenants occasionally double-quote the body, so we
    /// strip a wrapping pair of quotes before splitting.
    /// </summary>
    private void AppendSynonymDiff(string previousBlob, string nextBlob, SynonymsQuery query)
    {
        var before = ParseRules(previousBlob);
        var after = ParseRules(nextBlob);
        var added = after.Except(before, StringComparer.Ordinal).ToList();
        var removed = before.Except(after, StringComparer.Ordinal).ToList();

        if (added.Count == 0 && removed.Count == 0) return;

        var locale = string.IsNullOrEmpty(query.LanguageRouting) ? string.Empty : query.LanguageRouting;
        var slot = string.IsNullOrEmpty(query.Slot) ? string.Empty : query.Slot;
        var actor = HttpContext.User.Identity?.Name ?? string.Empty;
        var at = DateTime.UtcNow;

        var entries = new List<AuditLogEntry>(added.Count + removed.Count);
        foreach (var rule in added)
        {
            entries.Add(new AuditLogEntry
            {
                Kind = "Synonym", Action = "Created", Subject = rule,
                Locale = locale, Slot = slot,
                ActorId = actor, ActorName = actor, At = at
            });
        }
        foreach (var rule in removed)
        {
            entries.Add(new AuditLogEntry
            {
                Kind = "Synonym", Action = "Deleted", Subject = rule,
                Locale = locale, Slot = slot,
                ActorId = actor, ActorName = actor, At = at
            });
        }

        try { _audit.AppendMany(entries); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append audit rows for Synonym update.");
        }
    }

    private static IReadOnlyList<string> ParseRules(string? blob)
    {
        if (string.IsNullOrEmpty(blob)) return Array.Empty<string>();
        var raw = blob;
        // Some tenants return the blob JSON-quoted (`"a => b\n…"`); peel
        // the wrapping pair so we don't compare unwrapped-to-wrapped.
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            try { raw = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? raw; }
            catch { /* leave as-is */ }
        }
        return raw.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Synonyms);

    private IActionResult HandleError(Exception exception)
    {
        if (exception is GraphSearchApiException apiException)
        {
            _logger.LogWarning(apiException, "Graph synonyms request failed with status {StatusCode}.", apiException.StatusCode);
            return StatusCode(apiException.StatusCode, new { message = "Graph API request failed." });
        }
        if (exception is InvalidOperationException invalidOp)
        {
            _logger.LogWarning(invalidOp, "Graph synonyms request rejected: {Reason}.", invalidOp.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
        _logger.LogError(exception, "Unhandled error in SynonymsApiController.");
        return Problem(title: "Synonyms API request failed.");
    }
}
