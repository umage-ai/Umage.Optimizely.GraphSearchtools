using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.CustomDataSources;

/// <summary>
/// Orchestration around <see cref="IGraphAdminClient"/> for the Custom Data
/// Sources tool. Maps between the wire-shape <see cref="DataSourceResult"/>
/// and the camelCase <see cref="DataSourceSummary"/> the API surfaces to the
/// page JS. v1 is read + trigger-resync only — registration of new sources
/// happens out-of-band in the source's own pipeline.
/// </summary>
public sealed class CustomDataSourcesService
{
    private readonly IGraphAdminClient _client;

    public CustomDataSourcesService(IGraphAdminClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<DataSourceSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var sources = await _client.GetDataSourcesAsync(cancellationToken);
        return sources.Select(ToSummary).ToList();
    }

    public Task TriggerSyncAsync(string sourceName, CancellationToken cancellationToken)
        => _client.TriggerDataSourceSyncAsync(sourceName, cancellationToken);

    private static DataSourceSummary ToSummary(DataSourceResult result) => new()
    {
        Name = result.Name,
        Type = result.Type,
        ItemCount = result.ItemCount,
        LastSyncedAt = result.LastSyncedAt,
        Status = result.Status
    };
}
