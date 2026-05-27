using System.Collections.Concurrent;
using System.Net;

namespace UmageAI.Optimizely.GraphSearchTools.LoadTest;

/// <summary>
/// Lock-free hot-path counters + a coarse latency histogram. The histogram
/// uses log-bucketed slots in microseconds so we don't lose tail detail
/// without paying for full-precision tracking.
/// </summary>
internal sealed class Metrics
{
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private long _accepted;
    private long _rateLimited;
    private long _tooLarge;
    private long _otherClient;
    private long _server;
    private long _network;
    private long _clickAccepted;
    private long _clickRejected;

    /// <summary>Histogram bucket = log2(microseconds). Bucket 10 = ~1ms, 20 = ~1s.</summary>
    private readonly long[] _searchHistogram = new long[32];
    private readonly long[] _clickHistogram = new long[32];
    private long _searchTotalUs;
    private long _searchSamples;
    private long _searchMaxUs;

    public bool HasFailures => Volatile.Read(ref _server) > 0 || Volatile.Read(ref _network) > 0;

    public void RecordSearch(TimeSpan elapsed, HttpStatusCode status)
    {
        Categorize(status,
            ref _accepted, ref _rateLimited, ref _tooLarge,
            ref _otherClient, ref _server, ref _network);

        var us = (long)(elapsed.TotalMilliseconds * 1000);
        Interlocked.Add(ref _searchTotalUs, us);
        Interlocked.Increment(ref _searchSamples);
        var bucket = Bucket(us);
        Interlocked.Increment(ref _searchHistogram[bucket]);
        UpdateMax(ref _searchMaxUs, us);
    }

    public void RecordClick(TimeSpan elapsed, HttpStatusCode status)
    {
        if (status == HttpStatusCode.NoContent) Interlocked.Increment(ref _clickAccepted);
        else Interlocked.Increment(ref _clickRejected);
        var us = (long)(elapsed.TotalMilliseconds * 1000);
        var bucket = Bucket(us);
        Interlocked.Increment(ref _clickHistogram[bucket]);
    }

    public Snapshot TakeSnapshot() => new(
        _startedAt,
        Volatile.Read(ref _accepted) + Volatile.Read(ref _rateLimited) + Volatile.Read(ref _tooLarge) + Volatile.Read(ref _otherClient) + Volatile.Read(ref _server) + Volatile.Read(ref _network),
        Volatile.Read(ref _accepted),
        Volatile.Read(ref _rateLimited),
        Volatile.Read(ref _tooLarge),
        Volatile.Read(ref _otherClient),
        Volatile.Read(ref _server),
        Volatile.Read(ref _network));

    public void PrintSummary(TimeSpan elapsed, int targetRps)
    {
        var samples = Volatile.Read(ref _searchSamples);
        var totalUs = Volatile.Read(ref _searchTotalUs);
        var meanMs = samples == 0 ? 0 : totalUs / 1000.0 / samples;
        var snap = TakeSnapshot();
        var achievedRps = elapsed.Ticks == 0 ? 0 : snap.Total / elapsed.TotalSeconds;

        Console.WriteLine($"Sent:    {snap.Total:N0} search events in {elapsed.TotalSeconds:F1}s = {achievedRps:F0} rps achieved (target {targetRps:N0})");
        Console.WriteLine();
        Console.WriteLine("Status (search):");
        Console.WriteLine($"  204 No Content (accepted)        : {snap.Accepted,9:N0}  ({Pct(snap.Accepted, snap.Total)})");
        Console.WriteLine($"  429 Too Many Requests            : {snap.RateLimited,9:N0}  ({Pct(snap.RateLimited, snap.Total)})");
        Console.WriteLine($"  413 Payload Too Large            : {snap.TooLarge,9:N0}  ({Pct(snap.TooLarge, snap.Total)})");
        Console.WriteLine($"  4xx other (mostly 4xx misroutes) : {snap.OtherClient,9:N0}  ({Pct(snap.OtherClient, snap.Total)})");
        Console.WriteLine($"  5xx server                       : {snap.Server,9:N0}  ({Pct(snap.Server, snap.Total)})");
        Console.WriteLine($"  network failures / timeouts      : {snap.Network,9:N0}  ({Pct(snap.Network, snap.Total)})");
        Console.WriteLine();
        Console.WriteLine($"Search latency: mean={meanMs:F2}ms  p50={Percentile(_searchHistogram, 0.50):F2}ms  p95={Percentile(_searchHistogram, 0.95):F2}ms  p99={Percentile(_searchHistogram, 0.99):F2}ms  max={Volatile.Read(ref _searchMaxUs)/1000.0:F2}ms");
        Console.WriteLine();
        Console.WriteLine($"Clicks: accepted={Volatile.Read(ref _clickAccepted):N0}  rejected={Volatile.Read(ref _clickRejected):N0}");
        Console.WriteLine($"Click latency: p50={Percentile(_clickHistogram, 0.50):F2}ms  p95={Percentile(_clickHistogram, 0.95):F2}ms  p99={Percentile(_clickHistogram, 0.99):F2}ms");
    }

    private static string Pct(long part, long total) => total == 0 ? "0.0%" : $"{part * 100.0 / total:F1}%";

    private static int Bucket(long us)
    {
        if (us <= 1) return 0;
        var bucket = (int)Math.Log2(us);
        return bucket < 0 ? 0 : bucket > 31 ? 31 : bucket;
    }

    private static double Percentile(long[] histogram, double p)
    {
        long total = 0;
        for (var i = 0; i < histogram.Length; i++) total += Volatile.Read(ref histogram[i]);
        if (total == 0) return 0;
        var target = (long)(total * p);
        long running = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            running += Volatile.Read(ref histogram[i]);
            if (running >= target)
            {
                // Bucket midpoint, in milliseconds.
                var lowUs = i == 0 ? 1 : 1L << i;
                var highUs = 1L << (i + 1);
                return (lowUs + highUs) / 2.0 / 1000.0;
            }
        }
        return 0;
    }

    private static void UpdateMax(ref long target, long candidate)
    {
        long current;
        do
        {
            current = Volatile.Read(ref target);
            if (candidate <= current) return;
        } while (Interlocked.CompareExchange(ref target, candidate, current) != current);
    }

    private static void Categorize(HttpStatusCode status,
        ref long accepted, ref long rateLimited, ref long tooLarge,
        ref long otherClient, ref long server, ref long network)
    {
        switch ((int)status)
        {
            case 0: Interlocked.Increment(ref network); break;
            case 204: Interlocked.Increment(ref accepted); break;
            case 429: Interlocked.Increment(ref rateLimited); break;
            case 413: Interlocked.Increment(ref tooLarge); break;
            case >= 500: Interlocked.Increment(ref server); break;
            case >= 400: Interlocked.Increment(ref otherClient); break;
            default: Interlocked.Increment(ref otherClient); break;
        }
    }

    public readonly record struct Snapshot(
        DateTime StartedAt,
        long Total,
        long Accepted,
        long RateLimited,
        long TooLarge,
        long OtherClient,
        long Server,
        long Network);
}
