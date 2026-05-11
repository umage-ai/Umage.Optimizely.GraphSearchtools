using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Permissions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;

/// <summary>
/// REST API for the Relevancy Lab. Mounted under the conventional
/// <c>{basePath}/RelevancyLabApi/{action}</c> route — the implementation-plan
/// shorthand <c>/api/relevancylab/…</c> is honoured logically (one action per
/// REST verb). Individual surface map:
/// <list type="bullet">
///   <item><c>GET ListSets</c> — list golden sets.</item>
///   <item><c>GET GetSet?id=…</c> — full set detail.</item>
///   <item><c>POST UpsertSet</c> — upsert (empty Id ⇒ create).</item>
///   <item><c>POST DeleteSet?id=…</c> — delete a set.</item>
///   <item><c>POST Run</c> — execute a run; returns the <see cref="Run"/>.</item>
///   <item><c>GET ListRuns?setId=…</c> — recent runs (optionally per-set).</item>
///   <item><c>GET Compare?a=…&amp;b=…</c> — per-phrase deltas.</item>
///   <item><c>GET ExportCsv?id=…</c> — CSV export.</item>
/// </list>
/// All actions are policy-protected. Write actions carry <see cref="RequireAjaxAttribute"/>.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class RelevancyLabApiController : Controller
{
    private const string FeatureName = nameof(FeatureToggles.RelevancyLab);

    private readonly RelevancyLabService _service;
    private readonly FeatureAccessChecker _accessChecker;
    private readonly ILogger<RelevancyLabApiController> _logger;

    public RelevancyLabApiController(
        RelevancyLabService service,
        FeatureAccessChecker accessChecker,
        ILogger<RelevancyLabApiController> logger)
    {
        _service = service;
        _accessChecker = accessChecker;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult ListSets()
    {
        if (!HasAccess()) return Forbid();
        try { return Ok(_service.ListSets()); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab list sets failed.");
            return Problem(title: "Could not list golden sets.");
        }
    }

    [HttpGet]
    public IActionResult GetSet(Guid id)
    {
        if (!HasAccess()) return Forbid();
        var set = _service.GetSet(id);
        return set == null ? NotFound() : Ok(set);
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult UpsertSet([FromBody] GoldenSet? set)
    {
        if (!HasAccess()) return Forbid();
        if (set == null) return BadRequest(new { message = "Golden set body is required." });

        var error = RelevancyLabService.ValidateSet(set);
        if (error != null) return BadRequest(new { message = error });

        try
        {
            var actor = HttpContext.User?.Identity?.Name;
            var saved = _service.UpsertSet(set, actor);
            return Ok(saved);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Relevancy Lab set upsert rejected: {Reason}.", ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab set upsert failed.");
            return Problem(title: "Could not save golden set.");
        }
    }

    [HttpPost]
    [RequireAjax]
    public IActionResult DeleteSet(Guid id)
    {
        if (!HasAccess()) return Forbid();
        try
        {
            return _service.DeleteSet(id) ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab set delete failed.");
            return Problem(title: "Could not delete golden set.");
        }
    }

    [HttpPost]
    [RequireAjax]
    public async Task<IActionResult> Run([FromBody] RunRequest? request, CancellationToken cancellationToken)
    {
        if (!HasAccess()) return Forbid();
        if (request == null || request.GoldenSetId == Guid.Empty)
            return BadRequest(new { message = "Run request requires a goldenSetId." });

        var set = _service.GetSet(request.GoldenSetId);
        if (set == null) return NotFound(new { message = "Golden set not found." });

        try
        {
            var run = await _service.RunAsync(set, request.Config ?? new RankingConfig(), cancellationToken);
            return Ok(run);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Relevancy Lab run rejected upstream with {Status}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Graph query failed during run." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Relevancy Lab run rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab run failed.");
            return Problem(title: "Run failed.");
        }
    }

    [HttpGet]
    public IActionResult ListRuns([FromQuery] Guid? setId, [FromQuery] int take = 25)
    {
        if (!HasAccess()) return Forbid();
        try { return Ok(_service.ListRecentRuns(setId, take)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab list runs failed.");
            return Problem(title: "Could not list runs.");
        }
    }

    [HttpGet]
    public IActionResult Compare([FromQuery] Guid a, [FromQuery] Guid b)
    {
        if (!HasAccess()) return Forbid();
        if (a == Guid.Empty || b == Guid.Empty)
            return BadRequest(new { message = "Both 'a' and 'b' run IDs are required." });
        try { return Ok(_service.CompareRuns(a, b)); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Relevancy Lab compare failed.");
            return Problem(title: "Could not compare runs.");
        }
    }

    [HttpGet]
    public IActionResult ExportCsv(Guid id)
    {
        if (!HasAccess()) return Forbid();
        var run = _service.GetRun(id);
        if (run == null) return NotFound();
        var bytes = Encoding.UTF8.GetBytes(RelevancyLabService.ToCsv(run));
        var filename = $"relevancy-lab-run-{id}.csv";
        return File(bytes, "text/csv", filename);
    }

    private bool HasAccess()
        => _accessChecker.HasAccess(HttpContext, FeatureName, GraphSearchtoolsPermissions.RelevancyLab);
}
