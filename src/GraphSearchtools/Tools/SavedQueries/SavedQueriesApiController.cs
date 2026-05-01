using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

[Authorize(Policy = "codeart:graphsearchtools")]
public class SavedQueriesApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.SavedQueries);

    private readonly SavedQueriesService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<SavedQueriesApiController> _logger;

    public SavedQueriesApiController(
        SavedQueriesService service,
        FeatureAccessChecker accessChecker,
        ILogger<SavedQueriesApiController> logger)
    {
        _service = service;
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
    public IActionResult Delete(string id)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return _service.Delete(id) ? NoContent() : NotFound();
        }
        catch (Exception ex) { return Handle(ex); }
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.SavedQueries);

    private IActionResult Handle(Exception ex)
    {
        _logger.LogError(ex, "SavedQueries API error.");
        return Problem(title: "Saved queries request failed.");
    }
}
