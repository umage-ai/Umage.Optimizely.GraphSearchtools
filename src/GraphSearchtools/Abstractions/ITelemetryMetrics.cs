namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// Runtime counters from a local telemetry sink. Surfaced through the Health
/// dashboard so editors see queue backpressure before users do (per design
/// §3.2). External readers register no implementation; the Health surface
/// then omits the telemetry probe.
/// </summary>
public interface ITelemetryMetrics
{
    /// <summary>Approximate channel depth at the moment of the call.</summary>
    int ApproximateQueueDepth { get; }

    /// <summary>Approximate count of dropped events since process start (DropOldest semantics — exact tracking isn't possible).</summary>
    long ApproximateDroppedTotal { get; }

    /// <summary>Configured channel capacity, for percent-full computation in the UI.</summary>
    int QueueCapacity { get; }
}
