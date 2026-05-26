using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Sites;

/// <summary>
/// Read-only API surface for picker metadata: the CMS site/language list (used
/// by the Pinned Results and Synonyms tools) and the Graph-side locale list
/// (used by tools that scope queries by locale, e.g. Search Console and
/// Autocomplete). Access piggybacks on Pinned OR Synonyms — a tenant that
/// disables both has implicitly disabled the pickers too.
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
internal class SitesApiController : Controller
{
    private readonly LanguageSiteEnumerator _enumerator;
    private readonly IGraphAdminClient _graphClient;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SitesApiController> _logger;

    public SitesApiController(
        LanguageSiteEnumerator enumerator,
        IGraphAdminClient graphClient,
        FeatureAccessChecker accessChecker,
        ILogger<SitesApiController> logger)
    {
        _enumerator = enumerator;
        _graphClient = graphClient;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<SiteInfo>> List()
    {
        if (!HasAccess()) return Forbid();
        return Ok(_enumerator.Enumerate());
    }

    /// <summary>
    /// Returns the locale codes Optimizely Graph itself can serve, introspected
    /// from the schema's <c>Locales</c> enum. The list reflects what is
    /// queryable in Graph rather than what the host CMS happens to expose, so
    /// pickers stay accurate even when the two have drifted.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Locales(CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            var locales = await _graphClient.GetGraphLocalesAsync(cancellationToken);
            return Ok(locales);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph locale introspection failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Graph locale lookup failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph locale lookup rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Pinned), GraphSearchtoolsPermissions.Pinned)
        || _accessChecker.HasAccess(HttpContext, nameof(FeatureToggles.Synonyms), GraphSearchtoolsPermissions.Synonyms);
}
