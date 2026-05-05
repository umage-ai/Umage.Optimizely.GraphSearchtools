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
/// <remarks>
/// Phase 2.5 §4.2 added an optional <c>profileKey</c> query parameter. When
/// supplied the controller resolves the synonym slot from the registered
/// <see cref="SearchProfile.SynonymSlot"/> and ignores any client-supplied
/// <c>slot</c>. <c>profileKey</c> wins over <c>language</c>; profiles without
/// a synonym slot return 400.
///
/// On successful Create / Update / Delete the controller appends a
/// <see cref="SearchProfileEdit"/> row with <c>Kind = "Synonym"</c>. Global
/// (non-profile) edits use <c>ProfileKey = "global"</c> so the audit table
/// preserves a single, queryable namespace — the alternative of "" would mix
/// with profile-keyed rows when the row is read back outside its filter.
/// </remarks>
[Authorize(Policy = "codeart:graphsearchtools")]
public class SynonymsApiController : Controller
{
    /// <summary>Sentinel ProfileKey used in the audit log for global-scope edits.</summary>
    public const string GlobalAuditProfileKey = "global";

    private const string FeatureName = nameof(FeatureToggles.Synonyms);

    private readonly SynonymsService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ISearchProfileRegistry _profiles;
    private readonly SearchProfileEditService _edits;
    private readonly ILogger<SynonymsApiController> _logger;

    public SynonymsApiController(
        SynonymsService service,
        FeatureAccessChecker accessChecker,
        ISearchProfileRegistry profiles,
        SearchProfileEditService edits,
        ILogger<SynonymsApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _profiles = profiles;
        _edits = edits;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? languageRouting,
        [FromQuery] string? sourceRouting,
        [FromQuery] string? slot,
        [FromQuery] string? profileKey,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var resolved = ResolveScope(profileKey, languageRouting, slot, out var error);
        if (error != null) return error;

        var query = new SynonymsQuery
        {
            LanguageRouting = resolved.LanguageRouting,
            SourceRouting = sourceRouting,
            Slot = resolved.Slot
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
    public async Task<IActionResult> Update(
        [FromBody] SynonymsRequest request,
        [FromQuery] string? profileKey,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Synonyms payload is required." });

        var resolved = ResolveScope(profileKey, request.LanguageRouting, request.Slot, out var error);
        if (error != null) return error;

        // Re-shape the request so the service sees the resolved scope.
        var scopedRequest = request with
        {
            LanguageRouting = resolved.LanguageRouting,
            Slot = resolved.Slot
        };

        try
        {
            var hadContent = !string.IsNullOrWhiteSpace(request.Content);
            await _service.UpdateAsync(scopedRequest, cancellationToken);

            // Heuristic: a synonym slot is one blob per (slot, language). Treat
            // any non-empty PUT as "Updated" (we don't currently know whether
            // the slot was previously empty without a GET round-trip).
            AppendAudit(resolved, hadContent ? "Updated" : "Cleared", PreviewSubject(request.Content));
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
        [FromQuery] string? profileKey,
        CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();

        var resolved = ResolveScope(profileKey, languageRouting, slot, out var error);
        if (error != null) return error;

        var query = new SynonymsQuery
        {
            LanguageRouting = resolved.LanguageRouting,
            SourceRouting = sourceRouting,
            Slot = resolved.Slot
        };

        try
        {
            await _service.DeleteAsync(query, cancellationToken);
            AppendAudit(resolved, "Deleted", subject: resolved.Slot ?? "");
            return NoContent();
        }
        catch (Exception ex)
        {
            return HandleError(ex);
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Synonyms);

    /// <summary>
    /// Resolves the effective synonym scope from the optional <paramref name="profileKey"/>
    /// (preferred when set) and the legacy <paramref name="language"/> / <paramref name="slot"/>
    /// query params. Returns a <see cref="BadRequestObjectResult"/> via <paramref name="error"/>
    /// when <paramref name="profileKey"/> identifies a profile without a synonym slot.
    /// </summary>
    private ResolvedScope ResolveScope(
        string? profileKey,
        string? language,
        string? slot,
        out IActionResult? error)
    {
        error = null;

        if (!string.IsNullOrWhiteSpace(profileKey))
        {
            var profile = _profiles.Get(profileKey);
            if (profile == null)
            {
                error = NotFound(new { message = "Profile not found." });
                return default;
            }
            if (string.IsNullOrWhiteSpace(profile.SynonymSlot))
            {
                error = BadRequest(new { message = "Profile has no synonym slot — there is nothing to tune." });
                return default;
            }
            return new ResolvedScope(
                ProfileKey: profile.Key,
                LanguageRouting: NormalizeLanguage(language),
                Slot: profile.SynonymSlot);
        }

        // Legacy / global-scope path.
        return new ResolvedScope(
            ProfileKey: GlobalAuditProfileKey,
            LanguageRouting: NormalizeLanguage(language),
            Slot: string.IsNullOrWhiteSpace(slot) ? null : slot);
    }

    private static string? NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language) ? null : language.Trim();

    private void AppendAudit(ResolvedScope scope, string action, string subject)
    {
        try
        {
            var actor = HttpContext.User?.Identity?.Name ?? string.Empty;
            _edits.Append(new SearchProfileEdit
            {
                ProfileKey = scope.ProfileKey,
                Site = string.Empty,
                Locale = scope.LanguageRouting ?? string.Empty,
                Kind = "Synonym",
                Action = action,
                Subject = subject ?? string.Empty,
                ActorId = actor,
                ActorName = actor,
                At = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Audit failures must not break the user-visible write.
            _logger.LogWarning(ex, "Failed to append synonym audit entry for profile {ProfileKey}.", scope.ProfileKey);
        }
    }

    /// <summary>Trim a long synonyms blob to a stable, log-friendly preview.</summary>
    private static string PreviewSubject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var firstLine = content.Split('\n', 2)[0].Trim();
        if (firstLine.Length <= 80) return firstLine;
        return firstLine.Substring(0, 77) + "...";
    }

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

    private readonly record struct ResolvedScope(string ProfileKey, string? LanguageRouting, string? Slot);
}
