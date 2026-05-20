namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// Resolves Optimizely Graph credentials from configuration. The default
/// implementation prefers <c>UmageAI:GraphSearchTools:Graph</c> when populated
/// and falls back to the host's <c>Optimizely:ContentGraph</c> section, so a
/// site already wired up for Graph does not have to double-configure.
/// </summary>
public interface IGraphCredentialsResolver
{
    GraphCredentials Resolve();
}

public sealed record GraphCredentials(
    string GatewayAddress,
    string AppKey,
    string Secret,
    string SingleKey)
{
    public bool IsAdminConfigured =>
        !string.IsNullOrWhiteSpace(GatewayAddress)
        && !string.IsNullOrWhiteSpace(AppKey)
        && !string.IsNullOrWhiteSpace(Secret);

    public bool IsQueryConfigured =>
        !string.IsNullOrWhiteSpace(GatewayAddress)
        && !string.IsNullOrWhiteSpace(SingleKey);

    public static GraphCredentials Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}
