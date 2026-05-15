namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// The single dependency the analytics UIs take. Implementations may read from
/// the local DDS bucket store (<c>LocalTelemetryReader</c>), a 3rd-party
/// telemetry backend (App Insights / Mixpanel / Matomo customer adapters), or
/// nothing at all (<c>NullTelemetryReader</c>). The addon never knows the
/// difference.
/// </summary>
public interface ITelemetryReader
{
    /// <summary>
    /// Top phrases by hit count over the window. Order: descending by hits,
    /// ties broken by phrase ascending.
    /// </summary>
    Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phrases with the highest count of zero-result hits over the window.
    /// Local readers MUST keep this dimension uncapped (per design §6.2) so
    /// rare-but-broken phrases are visible — rampant zero-result volume is a
    /// signal we want to surface, not suppress.
    /// </summary>
    Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Phrases whose click-through rate is meaningfully below the window
    /// average. Implementations decide the threshold; the contract is "phrases
    /// that look like missing pinned results / synonyms".
    /// </summary>
    Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Most recent raw events from the per-instance forensic ring. Cross-node
    /// forensics is a non-goal in v1 — readers may return only the local
    /// node's ring contents (per design §3.3).
    /// </summary>
    Task<IReadOnlyList<RawEvent>> RecentRawAsync(TelemetryQuery query, CancellationToken cancellationToken = default);
}
