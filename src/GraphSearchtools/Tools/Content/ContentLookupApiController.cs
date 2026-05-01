using EPiServer;
using EPiServer.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Content;

/// <summary>
/// Free-text content search and GUID resolution used by the Pinned tool's
/// inline content picker. Backed by the Optimizely Graph GraphQL endpoint.
/// </summary>
[Authorize(Policy = "codeart:graphsearchtools")]
public class ContentLookupApiController : Controller
{
    private readonly IContentLoader _contentLoader;
    private readonly IGraphAdminClient _graphClient;
    private readonly IOptions<GraphSearchtoolsOptions> _options;
    private readonly ILogger<ContentLookupApiController> _logger;

    public ContentLookupApiController(
        IContentLoader contentLoader,
        IGraphAdminClient graphClient,
        IOptions<GraphSearchtoolsOptions> options,
        ILogger<ContentLookupApiController> logger)
    {
        _contentLoader = contentLoader;
        _graphClient = graphClient;
        _options = options;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentSearchHit>>> Search(
        [FromQuery] string q,
        [FromQuery] string? locale,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<ContentSearchHit>());
        }

        try
        {
            var hits = await _graphClient.SearchContentAsync(q, locale, _options.Value.SearchableContentTypes, cancellationToken);
            return Ok(hits);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph content search failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Content search failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph content search rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    [HttpPost]
    public async Task<ActionResult<IReadOnlyList<ContentSearchHit>>> Resolve(
        [FromBody] string[] guids,
        CancellationToken cancellationToken)
    {
        if (guids == null || guids.Length == 0)
        {
            return Ok(Array.Empty<ContentSearchHit>());
        }

        try
        {
            var hits = await _graphClient.ResolveByGuidsAsync(guids, _options.Value.SearchableContentTypes, cancellationToken);
            return Ok(hits);
        }
        catch (GraphSearchApiException ex)
        {
            _logger.LogWarning(ex, "Graph GUID resolution failed with status {StatusCode}.", ex.StatusCode);
            return StatusCode(ex.StatusCode, new { message = "Content resolve failed." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Graph GUID resolution rejected: {Reason}.", ex.Message);
            return StatusCode(503, new { message = "Optimizely Graph is not configured." });
        }
    }

    [HttpGet]
    public IActionResult GetTarget(int contentId)
    {
        if (contentId <= 0)
        {
            return BadRequest(new { message = "Content id must be greater than zero." });
        }

        var contentReference = new ContentReference(contentId);
        if (!_contentLoader.TryGet<IContent>(contentReference, out var content))
        {
            return NotFound();
        }

        return Ok(new
        {
            contentId,
            contentGuid = content.ContentGuid.ToString(),
            name = content.Name ?? string.Empty
        });
    }
}
