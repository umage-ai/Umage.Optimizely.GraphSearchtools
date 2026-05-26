using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UmageAI.Optimizely.GraphSearchTools.Components;

/// <summary>
/// API endpoints for reusable UI components (content picker, content type picker).
/// </summary>
[Authorize(Policy = "umageai:graphsearchtools")]
internal class ComponentsApiController : Controller
{
    private readonly IContentLoader _contentLoader;
    private readonly IContentRepository _contentRepository;
    private readonly IContentTypeRepository _contentTypeRepository;

    public ComponentsApiController(
        IContentLoader contentLoader,
        IContentRepository contentRepository,
        IContentTypeRepository contentTypeRepository)
    {
        _contentLoader = contentLoader;
        _contentRepository = contentRepository;
        _contentTypeRepository = contentTypeRepository;
    }

    [HttpGet]
    public IActionResult GetContent(int id)
    {
        var contentRef = id == 0 ? ContentReference.RootPage : new ContentReference(id);
        if (!_contentLoader.TryGet<IContent>(contentRef, out var content))
            return NotFound();

        return Ok(MapContent(content));
    }

    [HttpGet]
    public IActionResult GetChildren(int id)
    {
        var contentRef = id == 0 ? ContentReference.RootPage : new ContentReference(id);
        var children = _contentLoader.GetChildren<IContent>(
                contentRef,
                new LoaderOptions { LanguageLoaderOption.FallbackWithMaster() })
            .Select(MapContent)
            .ToList();

        return Ok(children);
    }

    [HttpGet]
    public IActionResult SearchContent([FromQuery] string q, [FromQuery] int rootId = 0)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(Array.Empty<object>());

        var root = rootId == 0 ? ContentReference.RootPage : new ContentReference(rootId);
        var results = new List<object>();

        try
        {
            var descendants = _contentRepository.GetDescendents(root);
            foreach (var descRef in descendants.Take(2000))
            {
                if (results.Count >= 50) break;

                if (_contentLoader.TryGet<IContent>(descRef, out var content) &&
                    content.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(MapContent(content));
                }
            }
        }
        catch
        {
            // Fallback: return empty on error
        }

        return Ok(results);
    }

    [HttpGet]
    public IActionResult GetContentTypes([FromQuery] string? q)
    {
        var types = _contentTypeRepository.List()
            .Where(t => t.ModelType != null)
            .Select(ct => new
            {
                ct.ID,
                ct.Name,
                DisplayName = ct.DisplayName ?? ct.Name,
                ct.Description,
                ct.GroupName,
                Base = ct.Base.ToString(),
                IsSystemType = ct.ModelType?.Namespace?.StartsWith("EPiServer", StringComparison.OrdinalIgnoreCase) == true
            });

        if (!string.IsNullOrWhiteSpace(q))
        {
            types = types.Where(t =>
                t.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(types.OrderBy(t => t.DisplayName).ToList());
    }

    private object MapContent(IContent content)
    {
        bool hasChildren;
        try
        {
            hasChildren = _contentLoader.GetChildren<IContent>(
                content.ContentLink,
                new LoaderOptions { LanguageLoaderOption.FallbackWithMaster() })
                .Any();
        }
        catch
        {
            hasChildren = false;
        }

        var contentType = _contentTypeRepository.Load(content.ContentTypeID);

        return new
        {
            Id = content.ContentLink.ID,
            // Pinned items in Optimizely Graph key off ContentGuid, not the
            // numeric ContentLink.ID — so the picker must surface it for the
            // Pinned flyout's "Add target" path. Lower-cased to match how the
            // pinned-aurora.js target-name resolver keys its name lookup.
            ContentGuid = content.ContentGuid.ToString(),
            content.Name,
            TypeName = contentType?.DisplayName ?? contentType?.Name ?? "Unknown",
            HasChildren = hasChildren
        };
    }
}
