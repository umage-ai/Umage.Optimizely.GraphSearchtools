using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Empty-results reader. Wired by default when neither
/// <c>AddLocalTelemetrySink</c> nor a custom <c>AddTelemetryReader&lt;T&gt;</c>
/// is registered, so the analytics UIs render their empty state with a
/// "configure telemetry" link instead of crashing.
/// </summary>
internal sealed class NullTelemetryReader : ITelemetryReader
{
    public Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());

    public Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());

    public Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());

    public Task<IReadOnlyList<RawEvent>> RecentRawAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RawEvent>>(Array.Empty<RawEvent>());

    public Task<IReadOnlyList<DailyAggregate>> DailyTotalsAsync(TelemetryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DailyAggregate>>(Array.Empty<DailyAggregate>());
}
