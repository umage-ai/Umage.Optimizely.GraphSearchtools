using System.Threading.Channels;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Hot-path implementation of <see cref="ITelemetrySink"/>. Records always
/// return immediately — under overload the channel evicts the oldest event so
/// the present minute's data stays visible (per design §3.1).
/// </summary>
/// <remarks>
/// Singleton. Multi-writer (any HTTP handler), single-reader
/// (<see cref="BucketFlusher"/>).
/// </remarks>
internal sealed class LocalTelemetrySink : ITelemetrySink, ITelemetryMetrics
{
    private readonly Channel<TelemetryQueueItem> _channel;
    private readonly LocalTelemetryOptions _options;

    private long _writesAttempted;
    private long _itemsRead;

    public LocalTelemetrySink(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options.Value.Telemetry;

        // DropOldest: a stalled flusher should not silently swallow the
        // present minute's data — it should drop the backlog.
        _channel = Channel.CreateBounded<TelemetryQueueItem>(
            new BoundedChannelOptions(_options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Record(in SearchEvent searchEvent)
    {
        var phrase = TruncatePhrase(searchEvent.Phrase);
        var item = TelemetryQueueItem.ForSearch(
            phrase,
            searchEvent.ChannelKey ?? string.Empty,
            searchEvent.Locale ?? string.Empty,
            searchEvent.ResultCount,
            searchEvent.TimestampUtc);
        Interlocked.Increment(ref _writesAttempted);
        _channel.Writer.TryWrite(item);
    }

    public void Record(in ClickEvent clickEvent)
    {
        var phrase = TruncatePhrase(clickEvent.Phrase);
        var item = TelemetryQueueItem.ForClick(
            phrase,
            clickEvent.ChannelKey ?? string.Empty,
            clickEvent.Locale ?? string.Empty,
            clickEvent.Rank,
            clickEvent.TimestampUtc,
            clickEvent.OriginalBucketUtc);
        Interlocked.Increment(ref _writesAttempted);
        _channel.Writer.TryWrite(item);
    }

    /// <summary>
    /// Approximate queue depth — exact when the channel exposes a count, the
    /// configured capacity otherwise. Reported by the Health snapshot so
    /// editors see backpressure before users do.
    /// </summary>
    public int ApproximateQueueDepth
        => _channel.Reader.CanCount ? _channel.Reader.Count : _options.QueueCapacity;

    public int QueueCapacity => _options.QueueCapacity;

    /// <summary>
    /// Approximate count of dropped events since process start. Computed as
    /// <c>writes − reads − currentDepth</c>; under <c>DropOldest</c> the
    /// channel doesn't notify on eviction, so this reconciliation is the
    /// closest signal we have. Negative values clamped to 0 to handle
    /// in-flight reads racing the snapshot.
    /// </summary>
    public long ApproximateDroppedTotal
    {
        get
        {
            var depth = ApproximateQueueDepth;
            var dropped = Interlocked.Read(ref _writesAttempted)
                          - Interlocked.Read(ref _itemsRead)
                          - depth;
            return dropped < 0 ? 0 : dropped;
        }
    }

    internal ChannelReader<TelemetryQueueItem> Reader => _channel.Reader;

    /// <summary>
    /// Called by <see cref="BucketFlusher"/> after each successful read so
    /// drop reconciliation stays accurate.
    /// </summary>
    internal void NotifyItemRead() => Interlocked.Increment(ref _itemsRead);

    private string TruncatePhrase(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return string.Empty;
        return phrase.Length <= _options.MaxPhraseLength
            ? phrase
            : phrase.Substring(0, _options.MaxPhraseLength);
    }
}
