namespace UmageAI.Optimizely.GraphSearchTools.Tools.Connectivity;

public enum ConnectivityStatus
{
    Unknown,
    Red,
    Amber,
    Green
}

public sealed record ConnectivityProbeResult(string Name, ConnectivityStatus Status, string Message);

public sealed record ConnectivityResult(
    string GatewayAddress,
    bool AdminConfigured,
    bool QueryConfigured,
    IReadOnlyList<ConnectivityProbeResult> Probes,
    DateTime CheckedAt);
