using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.IndexInspector;

/// <summary>
/// Orchestration around <see cref="IGraphAdminClient.InspectIndexAsync"/> for
/// the Index Inspector tool. Pulls the host's
/// <c>SearchableContentTypes</c> allow-list from
/// <see cref="GraphSearchtoolsOptions"/> so the per-type fallback in the admin
/// client knows which types to probe when the schema doesn't expose
/// <c>types { name count }</c> directly. Maps the Graph DTO to a camelCase
/// snapshot that the page JS can render without further translation.
/// </summary>
public sealed class IndexInspectorService
{
    private readonly IGraphAdminClient _client;
    private readonly IOptions<GraphSearchtoolsOptions> _options;

    public IndexInspectorService(IGraphAdminClient client, IOptions<GraphSearchtoolsOptions> options)
    {
        _client = client;
        _options = options;
    }

    public async Task<IndexInspectorSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        var searchable = _options.Value.SearchableContentTypes ?? Array.Empty<string>();
        var result = await _client.InspectIndexAsync(searchable, cancellationToken);
        return ToSnapshot(result);
    }

    private static IndexInspectorSnapshot ToSnapshot(IndexInspectionResult result) => new()
    {
        TotalItems = result.TotalItems,
        PerContentType = result.PerContentType.Select(ToRow).ToList(),
        MissingNameCount = result.MissingNameCount,
        MissingTitleCount = result.MissingTitleCount,
        CapturedAt = result.CapturedAt
    };

    private static IndexInspectorRow ToRow(ContentTypeIndexRow row) => new()
    {
        Name = row.Name,
        Count = row.Count,
        MissingNameCount = row.MissingNameCount,
        MissingTeaserCount = row.MissingTeaserCount,
        MissingMainBodyCount = row.MissingMainBodyCount
    };
}
