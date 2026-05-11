using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace UmageAI.Optimizely.GraphSearchTools.LoadTest;

/// <summary>
/// Closed-loop load generator for the telemetry beacon endpoint. Spins up
/// <c>--concurrency</c> workers that paces themselves to a per-worker share
/// of the target <c>--rps</c>, drawing phrases from a Zipfian distribution so
/// the cardinality mix resembles real traffic (a few head terms get hammered,
/// a long tail trickles).
///
/// Reports at end:
///   - latency percentiles (p50/p95/p99/max) for the hot-path POST
///   - status code breakdown (204 / 429 / 413 / 4xx / 5xx / network-fail)
///   - achieved RPS vs target
///   - one read-path call (Top phrases over the run window) with its latency
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var opts = Options.Parse(args);
        if (opts == null)
        {
            PrintUsage();
            return 2;
        }

        Console.WriteLine($"Target: {opts.BaseUrl}");
        Console.WriteLine($"Plan:   {opts.Concurrency} workers × {opts.PerWorkerRps:F1} rps for {opts.Duration.TotalSeconds:F0}s = {opts.TotalRps} rps target");
        Console.WriteLine($"Phrases: {opts.PhraseCount} (Zipfian, exponent {opts.ZipfExponent})");
        Console.WriteLine($"Click ratio: {opts.ClickRatio:P0} of search events get a follow-up click");
        Console.WriteLine();

        // One HttpClient shared across workers — connection pool reuse is
        // load-bearing at high concurrency. Bump max conns per server above
        // the default 10 so workers don't contend for sockets.
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = Math.Max(opts.Concurrency * 2, 100),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };

        var phrases = BuildPhrases(opts.PhraseCount);
        var sampler = new ZipfSampler(opts.PhraseCount, opts.ZipfExponent, seed: 42);

        var metrics = new Metrics();
        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

        using var cts = new CancellationTokenSource(opts.Duration);
        var workers = Enumerable.Range(0, opts.Concurrency)
            .Select(workerId => Task.Run(() => RunWorkerAsync(workerId, opts, http, phrases, sampler, metrics, cts.Token)))
            .ToArray();

        // Live progress every second.
        var progress = Task.Run(() => PrintProgressAsync(metrics, opts, cts.Token));

        try { await Task.WhenAll(workers); } catch (OperationCanceledException) { }
        sw.Stop();
        cts.Cancel();
        try { await progress; } catch (OperationCanceledException) { }

        var endedAt = DateTime.UtcNow;
        Console.WriteLine();
        Console.WriteLine("── Run complete ─────────────────────────────────");
        metrics.PrintSummary(sw.Elapsed, opts.TotalRps);

        Console.WriteLine();
        Console.WriteLine("── Read path ────────────────────────────────────");
        await ProbeReadPathAsync(http, startedAt, endedAt);

        return metrics.HasFailures ? 1 : 0;
    }

    private static async Task RunWorkerAsync(
        int workerId,
        Options opts,
        HttpClient http,
        IReadOnlyList<string> phrases,
        ZipfSampler sampler,
        Metrics metrics,
        CancellationToken ct)
    {
        var rng = new Random(unchecked(workerId * 17 + 13));
        var perEventInterval = TimeSpan.FromSeconds(1.0 / opts.PerWorkerRps);
        var nextDispatch = DateTime.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var wait = nextDispatch - now;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, ct); }
                catch (OperationCanceledException) { return; }
            }
            nextDispatch += perEventInterval;

            var phraseIdx = sampler.Sample(rng);
            var phrase = phrases[phraseIdx];
            var locale = Locales[rng.Next(Locales.Length)];
            var profile = Profiles[rng.Next(Profiles.Length)];
            var resultCount = rng.NextDouble() < 0.10 ? 0 : rng.Next(1, 50); // 10% zero-result
            var bucketUtc = TruncateToMinute(DateTime.UtcNow);

            var sw = Stopwatch.StartNew();
            var status = await PostAsync(http, BuildSearchPayload(phrase, profile, locale, resultCount), ct);
            sw.Stop();
            metrics.RecordSearch(sw.Elapsed, status);

            // Fire a follow-up click for a random fraction of accepted searches.
            if (status == HttpStatusCode.NoContent && rng.NextDouble() < opts.ClickRatio)
            {
                var rank = rng.Next(1, 5); // 1..4 — exercises both hot ranks (1..3) and the silently-dropped tail (4)
                var clickSw = Stopwatch.StartNew();
                var clickStatus = await PostAsync(http, BuildClickPayload(phrase, profile, locale, rank, bucketUtc), ct);
                clickSw.Stop();
                metrics.RecordClick(clickSw.Elapsed, clickStatus);
            }
        }
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient http, string body, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync("/api/telemetry/searchlog", content, ct);
            return resp.StatusCode;
        }
        catch (TaskCanceledException) { return HttpStatusCode.RequestTimeout; }
        catch (HttpRequestException) { return 0; } // network failure sentinel
    }

    private static string BuildSearchPayload(string phrase, string profile, string locale, int resultCount)
    {
        // Hand-rolled to dodge per-request serializer overhead — this is a load
        // generator, not the system under test.
        return $"{{\"kind\":\"search\",\"phrase\":\"{Escape(phrase)}\",\"profileKey\":\"{profile}\",\"locale\":\"{locale}\",\"resultCount\":{resultCount}}}";
    }

    private static string BuildClickPayload(string phrase, string profile, string locale, int rank, DateTime originalBucketUtc)
    {
        var iso = originalBucketUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return $"{{\"kind\":\"click\",\"phrase\":\"{Escape(phrase)}\",\"profileKey\":\"{profile}\",\"locale\":\"{locale}\",\"rank\":{rank},\"originalBucketUtc\":\"{iso}\"}}";
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IReadOnlyList<string> BuildPhrases(int count)
    {
        // Synthetic phrases that look like real search terms (alphabet soup
        // works fine for the sink — normalization just lower-cases and trims).
        var roots = new[] { "warranty", "shipping", "return", "size", "delivery", "support", "track", "refund", "address", "discount", "voucher", "billing", "invoice", "stock", "available", "color", "fit", "fabric", "wash", "care", "manual", "guide", "spec", "review", "compare" };
        var list = new List<string>(count);
        var rng = new Random(7);
        for (var i = 0; i < count; i++)
        {
            var root = roots[i % roots.Length];
            list.Add(i < roots.Length ? root : $"{root} {rng.Next(1000, 9999)}");
        }
        return list;
    }

    private static DateTime TruncateToMinute(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);

    private static readonly string[] Locales = { "en", "sv" };
    // Default to the Alloy SampleSite's registered profile so load-test data
    // appears in the per-profile Insights tab without further wiring. Sites
    // with multiple profiles can supply --profiles on the command line.
    private static readonly string[] Profiles = { "alloy-search" };

    private static async Task ProbeReadPathAsync(HttpClient http, DateTime sinceUtc, DateTime untilUtc)
    {
        // The admin reader is policy-gated, so this will return 401/403 on a
        // logged-out client. We still record the latency — round-trip + auth
        // gate is a useful sanity check that the read endpoint is responsive
        // under write load.
        var qs = $"?since={sinceUtc:O}&until={untilUtc:O}&take=20";
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await http.GetAsync("/EPiServer/cms/graphsearchtools/TelemetryAdminApi/Top" + qs);
            sw.Stop();
            Console.WriteLine($"Top phrases (admin): {(int)resp.StatusCode} {resp.StatusCode} in {sw.Elapsed.TotalMilliseconds:F1}ms");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                Console.WriteLine($"  rows: {doc.RootElement.GetArrayLength()}");
            }
            else
            {
                Console.WriteLine("  (read endpoint requires admin auth — non-2xx is expected from an unauthenticated client)");
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"Top phrases (admin): EXCEPTION in {sw.Elapsed.TotalMilliseconds:F1}ms — {ex.GetType().Name}");
        }
    }

    private static async Task PrintProgressAsync(Metrics metrics, Options opts, CancellationToken ct)
    {
        var lastTotal = 0L;
        var lastTick = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct); }
            catch (OperationCanceledException) { return; }

            var snapshot = metrics.TakeSnapshot();
            var now = DateTime.UtcNow;
            var dt = (now - lastTick).TotalSeconds;
            var dn = snapshot.Total - lastTotal;
            var inFlightRps = dn / dt;
            lastTotal = snapshot.Total;
            lastTick = now;

            Console.WriteLine($"[t+{(now - snapshot.StartedAt).TotalSeconds,5:F0}s] sent={snapshot.Total,7:N0}  rps={inFlightRps,7:F0}  204={snapshot.Accepted,6:N0}  429={snapshot.RateLimited,5:N0}  413={snapshot.TooLarge,4:N0}  4xx={snapshot.OtherClient,4:N0}  5xx={snapshot.Server,4:N0}  err={snapshot.Network,4:N0}");
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: gst-loadtest --url <baseUrl> [--rps N] [--duration Ns] [--concurrency N] [--phrases N] [--zipf 1.0] [--click-ratio 0.3]");
        Console.Error.WriteLine("Defaults: --rps 500 --duration 30s --concurrency 50 --phrases 1000 --zipf 1.0 --click-ratio 0.3");
    }

    // ── Options ──────────────────────────────────────────────────────────

    private sealed class Options
    {
        public required string BaseUrl { get; init; }
        public required int TotalRps { get; init; }
        public required TimeSpan Duration { get; init; }
        public required int Concurrency { get; init; }
        public required int PhraseCount { get; init; }
        public required double ZipfExponent { get; init; }
        public required double ClickRatio { get; init; }
        public double PerWorkerRps => (double)TotalRps / Concurrency;

        public static Options? Parse(string[] args)
        {
            string? url = null;
            var rps = 500;
            var duration = TimeSpan.FromSeconds(30);
            var concurrency = 50;
            var phrases = 1000;
            var zipf = 1.0;
            var clickRatio = 0.3;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--url" when i + 1 < args.Length: url = args[++i]; break;
                    case "--rps" when i + 1 < args.Length: rps = int.Parse(args[++i]); break;
                    case "--duration" when i + 1 < args.Length: duration = ParseDuration(args[++i]); break;
                    case "--concurrency" when i + 1 < args.Length: concurrency = int.Parse(args[++i]); break;
                    case "--phrases" when i + 1 < args.Length: phrases = int.Parse(args[++i]); break;
                    case "--zipf" when i + 1 < args.Length: zipf = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--click-ratio" when i + 1 < args.Length: clickRatio = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "-h" or "--help": return null;
                }
            }
            if (string.IsNullOrEmpty(url)) return null;
            if (rps < 1 || concurrency < 1 || phrases < 1) return null;
            return new Options
            {
                BaseUrl = url.TrimEnd('/'),
                TotalRps = rps,
                Duration = duration,
                Concurrency = concurrency,
                PhraseCount = phrases,
                ZipfExponent = zipf,
                ClickRatio = clickRatio,
            };
        }

        private static TimeSpan ParseDuration(string s)
        {
            if (s.EndsWith("ms")) return TimeSpan.FromMilliseconds(int.Parse(s[..^2]));
            if (s.EndsWith("s")) return TimeSpan.FromSeconds(int.Parse(s[..^1]));
            if (s.EndsWith("m")) return TimeSpan.FromMinutes(int.Parse(s[..^1]));
            return TimeSpan.FromSeconds(int.Parse(s));
        }
    }
}
