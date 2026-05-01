using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Autocomplete;

/// <summary>
/// Thin pass-through to <see cref="IGraphAdminClient.AutocompleteAsync"/>.
/// The view drives the input; the service is intentionally stateless so it can
/// be exercised one keystroke at a time without persisting anything.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class AutocompleteApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.Autocomplete);

    private readonly IGraphAdminClient _client;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<AutocompleteApiController> _logger;

    public AutocompleteApiController(
        IGraphAdminClient client,
        FeatureAccessChecker accessChecker,
        ILogger<AutocompleteApiController> logger)
    {
        _client = client;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Suggest(
        [FromQuery] string value,
        [FromQuery] string? locale,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (!_accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Autocomplete))
        {
            return Forbid();
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            return Ok(Array.Empty<string>());
        }

        try
        {
            var results = await _client.AutocompleteAsync(value, locale, limit, cancellationToken);
            return Ok(results);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Autocomplete request failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }
}
