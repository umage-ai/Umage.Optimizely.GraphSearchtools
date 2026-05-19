using EPiServer.Data.Dynamic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Drains the sink's channel into per-(minute, phrase, channel, locale)
/// buckets, then upserts closed buckets to DDS on
/// <see cref="LocalTelemetryOptions.FlushInterval"/>. Read and flush run on
/// separate tasks; a single lock guards the in-memory state because the only
/// contention is once per flush interval.
/// </summary>
/// <remarks>
/// Two open dictionaries:
///  • <c>_open</c> — capped at <see cref="LocalTelemetryOptions.MaxOpenBuckets"/>.
///    When full, new keys are dropped (the rate limiter is the real defense
///    against cardinality attacks; this just bounds memory).
///  • <c>_zeroOnly</c> — uncapped. Catches phrases that
///    <c>_open</c> turned away whose <c>ResultCount == 0</c>, so the
///    Synonym-Coverage signal survives even under cardinality pressure
///    (per design §6.2).
///
/// Click attribution: a click whose originating bucket is still open folds
/// in directly. A click for an already-flushed bucket lands in <c>_delayed</c>
/// and is reconciled against DDS on the next flush.
/// </remarks>
internal sealed class BucketFlusher : BackgroundService
{
    private readonly LocalTelemetrySink _sink;
    private readonly LocalTelemetryOptions _options;
    private readonly ILogger<BucketFlusher> _logger;

    private readonly object _lock = new();
    private readonly Dictionary<BucketKey, BucketCounters> _open = new();
    private readonly Dictionary<BucketKey, ZeroOnlyCounters> _zeroOnly = new();
    private readonly Dictionary<BucketKey, ClickCounters> _delayed = new();

    /// <summary>Reservoir for the per-instance forensic ring. Persisted on flush.</summary>
    private readonly List<TelemetryQueueItem> _ringSample = new();
    private long _ringSeen;

    public BucketFlusher(
        LocalTelemetrySink sink,
        IOptions<GraphSearchtoolsOptions> options,
        ILogger<BucketFlusher> logger)
    {
        _sink = sink;
        _options = options.Value.Telemetry;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var read = ReadLoopAsync(stoppingToken);
        var flush = FlushLoopAsync(stoppingToken);
        return Task.WhenAll(read, flush);
    }

    // ── Read loop ────────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var reader = _sink.Reader;
        try
        {
            await foreach (var item in reader.ReadAllAsync(ct))
            {
                _sink.NotifyItemRead();
                Apply(item);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry read loop crashed.");
        }
    }

    internal void Apply(in TelemetryQueueItem item)
    {
        var bucketUtc = TruncateToMinute(item.TimestampUtc);
        var key = new BucketKey(bucketUtc, NormalizePhrase(item.Phrase), item.ChannelKey, item.Locale);

        lock (_lock)
        {
            switch (item.Kind)
            {
                case TelemetryQueueItemKind.Search:
                    ApplySearch(key, item);
                    break;
                case TelemetryQueueItemKind.Click:
                    ApplyClick(key, item);
                    break;
            }

            ReservoirSample(item);
        }
    }

    private void ApplySearch(BucketKey key, in TelemetryQueueItem item)
    {
        if (_open.TryGetValue(key, out var counters))
        {
            counters.Hits++;
            if (item.ResultCount == 0) counters.Zeroes++;
            return;
        }

        if (_open.Count < _options.MaxOpenBuckets)
        {
            _open[key] = new BucketCounters
            {
                DisplayPhrase = item.Phrase,
                Hits = 1,
                Zeroes = item.ResultCount == 0 ? 1 : 0,
            };
            return;
        }

        // Main dict at cap. Zero-result events still get tracked in the
        // uncapped sub-dictionary so broken phrases stay visible — the
        // Synonym-Coverage and zero-result UIs depend on this.
        if (item.ResultCount == 0)
        {
            if (_zeroOnly.TryGetValue(key, out var z))
            {
                z.Zeroes++;
            }
            else
            {
                _zeroOnly[key] = new ZeroOnlyCounters
                {
                    DisplayPhrase = item.Phrase,
                    Zeroes = 1,
                };
            }
        }
    }

    private void ApplyClick(BucketKey key, in TelemetryQueueItem item)
    {
        // The click should land in the bucket the originating search created.
        // OriginalBucketUtc takes precedence; absent, fall back to the click's
        // own minute as a best-effort attribution.
        var attributedBucket = item.OriginalBucketUtc.HasValue
            ? TruncateToMinute(item.OriginalBucketUtc.Value)
            : key.BucketUtc;
        var attributedKey = key with { BucketUtc = attributedBucket };

        if (_open.TryGetValue(attributedKey, out var counters))
        {
            IncrementClick(counters, item.ClickRank);
            return;
        }

        // Bucket already flushed (or never opened — the click arrived before
        // the search, or its search was filtered). Stage it for next flush
        // where we'll reconcile against DDS.
        if (_delayed.Count >= _options.MaxOpenBuckets) return;
        if (_delayed.TryGetValue(attributedKey, out var staged))
        {
            IncrementClick(staged, item.ClickRank);
        }
        else
        {
            var fresh = new ClickCounters();
            IncrementClick(fresh, item.ClickRank);
            _delayed[attributedKey] = fresh;
        }
    }

    private static void IncrementClick(BucketCounters c, int rank)
    {
        switch (rank)
        {
            case 1: c.Clicks1++; break;
            case 2: c.Clicks2++; break;
            case 3: c.Clicks3++; break;
            // Higher ranks intentionally not tracked — the read side cares
            // about whether the top hits were useful (per design §4 schema).
        }
    }

    private static void IncrementClick(ClickCounters c, int rank)
    {
        switch (rank)
        {
            case 1: c.Clicks1++; break;
            case 2: c.Clicks2++; break;
            case 3: c.Clicks3++; break;
        }
    }

    private void ReservoirSample(in TelemetryQueueItem item)
    {
        // Sample to a soft per-flush cap so the ring's DDS write rate is
        // bounded regardless of ingest. Cap = ringCapacity * (flush / ttl).
        var sampleCap = ComputeRingSampleCap();
        _ringSeen++;
        if (_ringSample.Count < sampleCap)
        {
            _ringSample.Add(item);
            return;
        }
        // Vitter's algorithm R — uniform reservoir sampling.
        var idx = Random.Shared.NextInt64(_ringSeen);
        if (idx < sampleCap)
        {
            _ringSample[(int)idx] = item;
        }
    }

    private int ComputeRingSampleCap()
    {
        var ttlSeconds = Math.Max(_options.RawRingTtl.TotalSeconds, 1);
        var flushSeconds = Math.Max(_options.FlushInterval.TotalSeconds, 1);
        var perFlush = (int)Math.Ceiling(_options.RawRingCapacity * (flushSeconds / ttlSeconds));
        return Math.Max(perFlush, 1);
    }

    // ── Flush loop ───────────────────────────────────────────────────────

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_options.FlushInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await FlushAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telemetry flush failed; will retry next tick.");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Snapshot in-memory state, persist closed buckets, drain delayed
    /// clicks against DDS, sample the raw ring. Keeps the current minute open
    /// so it can still accumulate after the flush returns.
    /// </summary>
    internal Task FlushAsync()
    {
        Snapshot snapshot;
        lock (_lock)
        {
            snapshot = TakeSnapshot();
        }

        if (snapshot.IsEmpty) return Task.CompletedTask;

        var bucketStore = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var ringStore = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogRing));

        UpsertOpenBuckets(bucketStore, snapshot.Open);
        UpsertZeroOnlyBuckets(bucketStore, snapshot.ZeroOnly);
        ApplyDelayedClicks(bucketStore, snapshot.Delayed);
        AppendRingSample(ringStore, snapshot.RingSample);
        TrimRing(ringStore);

        return Task.CompletedTask;
    }

    private Snapshot TakeSnapshot()
    {
        // Hold the current minute open: events arriving mid-flush continue to
        // accumulate against the still-open bucket and we'll catch them next
        // tick. Without this we'd race events into a row we just deleted from
        // the dictionary.
        var openMinute = TruncateToMinute(DateTime.UtcNow);

        var openClosed = new List<KeyValuePair<BucketKey, BucketCounters>>();
        foreach (var kvp in _open)
        {
            if (kvp.Key.BucketUtc < openMinute) openClosed.Add(kvp);
        }
        foreach (var kvp in openClosed) _open.Remove(kvp.Key);

        var zeroClosed = new List<KeyValuePair<BucketKey, ZeroOnlyCounters>>();
        foreach (var kvp in _zeroOnly)
        {
            if (kvp.Key.BucketUtc < openMinute) zeroClosed.Add(kvp);
        }
        foreach (var kvp in zeroClosed) _zeroOnly.Remove(kvp.Key);

        var delayedSnapshot = _delayed.ToList();
        _delayed.Clear();

        var ringSnapshot = _ringSample.ToArray();
        _ringSample.Clear();
        _ringSeen = 0;

        return new Snapshot(openClosed, zeroClosed, delayedSnapshot, ringSnapshot);
    }

    private void UpsertOpenBuckets(DynamicDataStore store, IReadOnlyList<KeyValuePair<BucketKey, BucketCounters>> closed)
    {
        foreach (var (key, counters) in closed)
        {
            try
            {
                var existing = FindBucket(store, key);
                if (existing == null)
                {
                    store.Save(new SearchLogBucket
                    {
                        BucketUtc = key.BucketUtc,
                        PhraseNorm = key.PhraseNorm,
                        ChannelKey = key.ChannelKey,
                        Locale = key.Locale,
                        NodeId = _options.NodeId,
                        DisplayPhrase = counters.DisplayPhrase,
                        Hits = counters.Hits,
                        Zeroes = counters.Zeroes,
                        Clicks1 = counters.Clicks1,
                        Clicks2 = counters.Clicks2,
                        Clicks3 = counters.Clicks3,
                    });
                }
                else
                {
                    existing.Hits += counters.Hits;
                    existing.Zeroes += counters.Zeroes;
                    existing.Clicks1 += counters.Clicks1;
                    existing.Clicks2 += counters.Clicks2;
                    existing.Clicks3 += counters.Clicks3;
                    if (string.IsNullOrEmpty(existing.DisplayPhrase) && !string.IsNullOrEmpty(counters.DisplayPhrase))
                        existing.DisplayPhrase = counters.DisplayPhrase;
                    store.Save(existing);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert telemetry bucket {Key}.", key);
            }
        }
    }

    private void UpsertZeroOnlyBuckets(DynamicDataStore store, IReadOnlyList<KeyValuePair<BucketKey, ZeroOnlyCounters>> closed)
    {
        foreach (var (key, counters) in closed)
        {
            try
            {
                var existing = FindBucket(store, key);
                if (existing == null)
                {
                    store.Save(new SearchLogBucket
                    {
                        BucketUtc = key.BucketUtc,
                        PhraseNorm = key.PhraseNorm,
                        ChannelKey = key.ChannelKey,
                        Locale = key.Locale,
                        NodeId = _options.NodeId,
                        DisplayPhrase = counters.DisplayPhrase,
                        Hits = 0,
                        Zeroes = counters.Zeroes,
                    });
                }
                else
                {
                    existing.Zeroes += counters.Zeroes;
                    store.Save(existing);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert zero-only telemetry bucket {Key}.", key);
            }
        }
    }

    private void ApplyDelayedClicks(DynamicDataStore store, IReadOnlyList<KeyValuePair<BucketKey, ClickCounters>> delayed)
    {
        foreach (var (key, counters) in delayed)
        {
            try
            {
                var existing = FindBucket(store, key);
                if (existing == null)
                {
                    // Click for a bucket that was never opened on this node —
                    // nothing to attribute against. Drop and move on.
                    continue;
                }
                existing.Clicks1 += counters.Clicks1;
                existing.Clicks2 += counters.Clicks2;
                existing.Clicks3 += counters.Clicks3;
                store.Save(existing);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to attribute delayed click {Key}.", key);
            }
        }
    }

    private SearchLogBucket? FindBucket(DynamicDataStore store, BucketKey key)
    {
        // DDS LINQ providers vary by version; an in-memory filter on the
        // narrow indexed minute is reliable across both CMS 12 and 13. Only
        // looking at the same node's row keeps the working set small.
        var nodeId = _options.NodeId;
        return store.Items<SearchLogBucket>()
            .Where(b => b.BucketUtc == key.BucketUtc && b.NodeId == nodeId)
            .ToList()
            .FirstOrDefault(b =>
                b.PhraseNorm == key.PhraseNorm &&
                b.ChannelKey == key.ChannelKey &&
                b.Locale == key.Locale);
    }

    private void AppendRingSample(DynamicDataStore store, IReadOnlyList<TelemetryQueueItem> sample)
    {
        foreach (var item in sample)
        {
            try
            {
                store.Save(new SearchLogRing
                {
                    TimestampUtc = item.TimestampUtc,
                    Kind = item.Kind == TelemetryQueueItemKind.Search ? "search" : "click",
                    Phrase = item.Phrase,
                    ChannelKey = item.ChannelKey,
                    Locale = item.Locale,
                    ResultCount = item.Kind == TelemetryQueueItemKind.Search ? item.ResultCount : null,
                    ClickRank = item.Kind == TelemetryQueueItemKind.Click ? item.ClickRank : null,
                    NodeId = _options.NodeId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist raw ring sample.");
            }
        }
    }

    private void TrimRing(DynamicDataStore store)
    {
        var cutoff = DateTime.UtcNow - _options.RawRingTtl;
        try
        {
            var stale = store.Items<SearchLogRing>().Where(r => r.TimestampUtc < cutoff).ToList();
            foreach (var row in stale) store.Delete(row.Id);

            // Hard cap pass — uncommon in steady state because of the TTL,
            // but cheap insurance against ring-capacity downsizing.
            var nodeId = _options.NodeId;
            var overflow = store.Items<SearchLogRing>()
                .Where(r => r.NodeId == nodeId)
                .OrderByDescending(r => r.TimestampUtc)
                .Skip(_options.RawRingCapacity)
                .ToList();
            foreach (var row in overflow) store.Delete(row.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trim raw ring.");
        }
    }

    // ── Test seam ────────────────────────────────────────────────────────

    /// <summary>
    /// Snapshot the in-memory dictionaries for assertion in unit tests.
    /// Returns flat DTOs so tests don't need to know the internal record types.
    /// Lock-free; safe to call after a sequence of synchronous <see cref="Apply"/>
    /// calls in a test.
    /// </summary>
    internal TestStateSnapshot GetTestSnapshot()
    {
        lock (_lock)
        {
            var open = _open.Select(kvp => new TestBucketRow(
                kvp.Key.BucketUtc, kvp.Key.PhraseNorm, kvp.Key.ChannelKey, kvp.Key.Locale,
                kvp.Value.DisplayPhrase, kvp.Value.Hits, kvp.Value.Zeroes,
                kvp.Value.Clicks1, kvp.Value.Clicks2, kvp.Value.Clicks3)).ToList();
            var zero = _zeroOnly.Select(kvp => new TestZeroRow(
                kvp.Key.BucketUtc, kvp.Key.PhraseNorm, kvp.Key.ChannelKey, kvp.Key.Locale,
                kvp.Value.DisplayPhrase, kvp.Value.Zeroes)).ToList();
            var delayed = _delayed.Select(kvp => new TestClickRow(
                kvp.Key.BucketUtc, kvp.Key.PhraseNorm, kvp.Key.ChannelKey, kvp.Key.Locale,
                kvp.Value.Clicks1, kvp.Value.Clicks2, kvp.Value.Clicks3)).ToList();
            return new TestStateSnapshot(open, zero, delayed);
        }
    }

    internal sealed record TestStateSnapshot(
        IReadOnlyList<TestBucketRow> Open,
        IReadOnlyList<TestZeroRow> ZeroOnly,
        IReadOnlyList<TestClickRow> Delayed);

    internal sealed record TestBucketRow(
        DateTime BucketUtc, string PhraseNorm, string ChannelKey, string Locale,
        string DisplayPhrase, int Hits, int Zeroes, int Clicks1, int Clicks2, int Clicks3);

    internal sealed record TestZeroRow(
        DateTime BucketUtc, string PhraseNorm, string ChannelKey, string Locale,
        string DisplayPhrase, int Zeroes);

    internal sealed record TestClickRow(
        DateTime BucketUtc, string PhraseNorm, string ChannelKey, string Locale,
        int Clicks1, int Clicks2, int Clicks3);

    // ── Helpers ──────────────────────────────────────────────────────────

    internal static DateTime TruncateToMinute(DateTime utc)
        => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);

    internal static string NormalizePhrase(string? phrase)
        => string.IsNullOrWhiteSpace(phrase)
            ? string.Empty
            : phrase.Trim().ToLowerInvariant();

    private readonly record struct BucketKey(DateTime BucketUtc, string PhraseNorm, string ChannelKey, string Locale);

    private sealed class BucketCounters
    {
        public string DisplayPhrase = string.Empty;
        public int Hits;
        public int Zeroes;
        public int Clicks1;
        public int Clicks2;
        public int Clicks3;
    }

    private sealed class ZeroOnlyCounters
    {
        public string DisplayPhrase = string.Empty;
        public int Zeroes;
    }

    private sealed class ClickCounters
    {
        public int Clicks1;
        public int Clicks2;
        public int Clicks3;
    }

    private readonly record struct Snapshot(
        IReadOnlyList<KeyValuePair<BucketKey, BucketCounters>> Open,
        IReadOnlyList<KeyValuePair<BucketKey, ZeroOnlyCounters>> ZeroOnly,
        IReadOnlyList<KeyValuePair<BucketKey, ClickCounters>> Delayed,
        IReadOnlyList<TelemetryQueueItem> RingSample)
    {
        public bool IsEmpty => Open.Count == 0 && ZeroOnly.Count == 0 && Delayed.Count == 0 && RingSample.Count == 0;
    }
}
