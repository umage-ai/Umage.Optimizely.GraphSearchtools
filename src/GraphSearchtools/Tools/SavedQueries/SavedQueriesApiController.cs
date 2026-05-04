using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

[Authorize(Policy = "codeart:graphsearchtools")]
public class SavedQueriesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SavedQueries);

    private readonly SavedQueriesService _service;
    private readonly QueryRunnerService _runner;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SavedQueriesApiController> _logger;

    public SavedQueriesApiController(
        SavedQueriesService service,
        QueryRunnerService runner,
        FeatureAccessChecker accessChecker,
        ILogger<SavedQueriesApiController> logger)
    {
        _service = service;
        _runner = runner;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!HasAccess()) return Forbid();
        try { return Ok(_service.List()); }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpGet]
    public IActionResult Get(string id)
    {
        if (!HasAccess()) return Forbid();
        var dto = _service.Get(id);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult Create([FromBody] SavedQueryPayload payload)
    {
        if (!HasAccess()) return Forbid();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return BadRequest(new { message = "Name is required." });
        }
        try { return Ok(_service.Create(payload)); }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpPut]
    [RequireAjax]
    public IActionResult Update(string id, [FromBody] SavedQueryPayload payload)
    {
        if (!HasAccess()) return Forbid();
        if (payload == null) return BadRequest(new { message = "Payload is required." });
        try
        {
            var dto = _service.Update(id, payload);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (Exception ex) { return Handle(ex); }
    }

    [HttpDelete]
    [RequireAjax]
    public IActionResult Delete([FromQuery] string id)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("SavedQueries Delete called without an id.");
                return NotFound(new { message = "Missing id parameter." });
            }
            var ok = _service.Delete(id);
            if (!ok)
            {
                _logger.LogWarning("SavedQueries Delete: no record matched id '{Id}'.", id);
                return NotFound(new { message = "No saved query found for the supplied id." });
            }
            return NoContent();
        }
        catch (Exception ex) { return Handle(ex); }
    }

    /// <summary>
    /// Executes a Graph query (the "Run" button on the page) and returns the
    /// hits plus the literal GraphQL document we sent — same shape the old
    /// Search Console returned, now living in this controller because the
    /// runner UI was folded into Saved Queries.
    /// </summary>
    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Run([FromBody] RunnerRequest request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null) return BadRequest(new { message = "Run request is required." });

        try
        {
            var result = await _runner.RunAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Saved Queries run failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Graph query failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Saved Queries run rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.SavedQueries);

    private IActionResult Handle(Exception ex)
    {
        _logger.LogError(ex, "SavedQueries API error.");
        return Problem(title: "Saved queries request failed.");
    }
}
