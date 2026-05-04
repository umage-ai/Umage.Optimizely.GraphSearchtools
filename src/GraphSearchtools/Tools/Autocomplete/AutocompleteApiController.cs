using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Autocomplete;

/// <summary>
/// Endpoints for the Autocomplete Tester. <c>Schema</c> populates the
/// type/field pickers; <c>Suggest</c> runs autocomplete against the chosen
/// type+field on each keystroke.
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
    public async Task<IActionResult> Schema(CancellationToken cancellationToken)
    {
        if (!_accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.Autocomplete))
        {
            return Forbid();
        }
        try
        {
            var schema = await _client.GetAutocompleteSchemaAsync(cancellationToken);
            return Ok(schema);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete schema failed with status {StatusCode}: {Body}", ex.StatusCode, ex.ResponseContent);
            return StatusCode(ex.StatusCode, new { message = "Autocomplete schema request failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete schema rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Suggest(
        [FromQuery] string type,
        [FromQuery] string field,
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
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(field))
        {
            return BadRequest(new { message = "type and field are required." });
        }

        try
        {
            var results = await _client.AutocompleteAsync(type, field, value, locale, limit, cancellationToken);
            return Ok(results);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete rejected request: {Reason}.", ex.Message);
            return BadRequest(new { message = "Invalid type or field." });
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete failed with status {StatusCode}: {Body}", ex.StatusCode, ex.ResponseContent);
            return StatusCode(ex.StatusCode, new { message = "Autocomplete request failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph autocomplete rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }
}
