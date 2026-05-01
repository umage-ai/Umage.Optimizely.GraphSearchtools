using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

/// <summary>
/// Thin orchestration around <see cref="IGraphAdminClient"/> for the synonyms tool.
/// </summary>
public sealed class SynonymsService
{
    private readonly IGraphAdminClient _client;

    public SynonymsService(IGraphAdminClient client)
    {
        _client = client;
    }

    public Task<string> GetAsync(SynonymsQuery query, CancellationToken ct)
        => _client.GetSynonymsAsync(query, ct);

    public Task UpdateAsync(SynonymsRequest request, CancellationToken ct)
        => _client.UpdateSynonymsAsync(request, ct);

    public Task DeleteAsync(SynonymsQuery query, CancellationToken ct)
        => _client.DeleteSynonymsAsync(query, ct);
}
