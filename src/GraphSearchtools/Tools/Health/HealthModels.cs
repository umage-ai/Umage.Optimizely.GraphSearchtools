using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthStatus
{
    Unknown,
    Red,
    Amber,
    Green
}

/// <summary>
/// One probe outcome. <see cref="Target"/> is the URL or operation the probe
/// hit (rendered in the UI under the probe name); <see cref="ElapsedMs"/> is
/// the wall-clock time we waited for it.
/// </summary>
public sealed record HealthProbeResult(
    string Name,
    HealthStatus Status,
    string Message,
    string Target,
    long ElapsedMs);

public sealed record HealthResult(
    string GatewayAddress,
    bool AdminConfigured,
    bool QueryConfigured,
    IReadOnlyList<HealthProbeResult> Probes,
    long ElapsedMs,
    DateTime CheckedAt);
