using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// Default <see cref="IGraphCredentialsResolver"/>: prefers values explicitly
/// set on <c>UmageAI:GraphSearchTools:Graph</c> and falls back per-field to the
/// host's <c>Optimizely:ContentGraph</c> section.
/// </summary>
internal sealed class GraphCredentialsResolver : IGraphCredentialsResolver
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<GraphSearchtoolsOptions> _options;

    public GraphCredentialsResolver(IConfiguration configuration, IOptions<GraphSearchtoolsOptions> options)
    {
        _configuration = configuration;
        _options = options;
    }

    public GraphCredentials Resolve()
    {
        var hostSection = _configuration.GetSection("Optimizely:ContentGraph");
        var hostGateway = hostSection.GetValue<string>("GatewayAddress") ?? string.Empty;
        var hostAppKey = hostSection.GetValue<string>("AppKey") ?? string.Empty;
        var hostSecret = hostSection.GetValue<string>("Secret") ?? string.Empty;
        var hostSingleKey = hostSection.GetValue<string>("SingleKey") ?? string.Empty;

        var overrideOptions = _options.Value.Graph;

        return new GraphCredentials(
            FirstNonEmpty(overrideOptions?.GatewayAddress, hostGateway),
            FirstNonEmpty(overrideOptions?.AppKey, hostAppKey),
            FirstNonEmpty(overrideOptions?.Secret, hostSecret),
            FirstNonEmpty(overrideOptions?.SingleKey, hostSingleKey));
    }

    private static string FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary)) return primary!;
        return fallback ?? string.Empty;
    }
}
