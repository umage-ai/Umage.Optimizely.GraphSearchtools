using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Webhooks;

/// <summary>
/// Orchestration around <see cref="IGraphAdminClient"/> for the Webhooks tool.
/// Maps between the wire-shape DTOs (<see cref="WebhookResult"/>,
/// <see cref="WebhookPayload"/>) and the camelCase <see cref="WebhookSummary"/>
/// the API surfaces to the page JS. Edits aren't supported by the upstream API,
/// so this service deliberately omits an Update method — the UI guides the
/// editor through delete + recreate instead.
/// </summary>
public sealed class WebhooksService
{
    private readonly IGraphAdminClient _client;

    public WebhooksService(IGraphAdminClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<WebhookSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var hooks = await _client.GetWebhooksAsync(cancellationToken);
        return hooks.Select(ToSummary).ToList();
    }

    public async Task<WebhookSummary> CreateAsync(WebhookCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = new WebhookPayload
        {
            Request = new WebhookRequestShape
            {
                Url = request.Url.Trim(),
                Method = string.IsNullOrWhiteSpace(request.Method) ? "POST" : request.Method.Trim().ToUpperInvariant(),
                Headers = request.Headers != null && request.Headers.Count > 0
                    ? new Dictionary<string, string>(request.Headers)
                    : null
            },
            Filters = request.Filters
        };

        var result = await _client.CreateWebhookAsync(payload, cancellationToken);
        return ToSummary(result);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
        => _client.DeleteWebhookAsync(id, cancellationToken);

    private static WebhookSummary ToSummary(WebhookResult result) => new()
    {
        Id = result.Id,
        Url = result.Request?.Url ?? string.Empty,
        Method = string.IsNullOrWhiteSpace(result.Request?.Method) ? "POST" : result.Request!.Method!,
        Headers = result.Request?.Headers,
        Filters = result.Filters,
        Disabled = result.Disabled ?? false,
        CreatedAt = result.CreatedAt
    };
}
