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

        Console.WriteLine($"Target:  {opts.BaseUrl}");
        Console.WriteLine($"Plan:    {opts.Concurrency} workers × {opts.PerWorkerRps:F1} rps for {opts.Duration.TotalSeconds:F0}s = {opts.TotalRps} rps target");
        Console.WriteLine($"Phrases: {opts.PhraseCount} (Zipfian, exponent {opts.ZipfExponent})");
        Console.WriteLine($"Clicks:  {opts.ClickRatio:P0} of accepted hit-bearing searches get a follow-up click");
        if (opts.BackfillDays > 0)
        {
            Console.WriteLine($"Backfill: events stamped uniformly across the past {opts.BackfillDays} days (diurnal hour weighting)");
        }
        else
        {
            Console.WriteLine("Live mode: events stamped at the current wall clock");
        }
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

        var phrases = BuildKeywords(opts.PhraseCount);
        var sampler = new ZipfSampler(phrases.Count, opts.ZipfExponent, seed: 42);

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
        IReadOnlyList<KeywordSpec> phrases,
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
            var spec = phrases[phraseIdx];
            var locale = Locales[rng.Next(Locales.Length)];
            var profile = Profiles[rng.Next(Profiles.Length)];

            // resultCount draws from the keyword's profile so "real" terms
            // mostly hit and "intentional zero" terms always miss — the
            // distribution makes the analytics surfaces feel realistic.
            var resultCount = spec.AlwaysZero || rng.NextDouble() < spec.ZeroResultRate
                ? 0
                : rng.Next(1, 50);

            var ts = SampleTimestamp(opts, rng);
            var bucketUtc = TruncateToMinute(ts);

            var sw = Stopwatch.StartNew();
            var status = await PostAsync(http, BuildSearchPayload(spec.Phrase, profile, locale, resultCount, ts), ct);
            sw.Stop();
            metrics.RecordSearch(sw.Elapsed, status);

            // Fire a follow-up click for a random fraction of accepted, non-zero
            // searches. Zero-result searches don't generate clicks (no result to
            // click). Click rank weighted toward 1 — most users click the top hit.
            if (status == HttpStatusCode.NoContent && resultCount > 0 && rng.NextDouble() < opts.ClickRatio)
            {
                var rank = WeightedClickRank(rng);
                // Clicks happen seconds-to-minutes after the search; sample
                // within a 2-minute window so OriginalBucketUtc still maps cleanly.
                var clickTs = ts.AddSeconds(rng.Next(2, 90));
                var clickSw = Stopwatch.StartNew();
                var clickStatus = await PostAsync(http, BuildClickPayload(spec.Phrase, profile, locale, rank, clickTs, bucketUtc), ct);
                clickSw.Stop();
                metrics.RecordClick(clickSw.Elapsed, clickStatus);
            }
        }
    }

    /// <summary>
    /// Click rank weighted toward 1 (~70%), 2 (~20%), 3 (~7%), 4+ (~3%).
    /// Mirrors real CTR distributions where the top hit dominates engagement.
    /// </summary>
    private static int WeightedClickRank(Random rng)
    {
        var u = rng.NextDouble();
        if (u < 0.70) return 1;
        if (u < 0.90) return 2;
        if (u < 0.97) return 3;
        return rng.Next(4, 11);
    }

    /// <summary>
    /// In live mode, the timestamp is "now". In backfill mode, the timestamp
    /// is uniformly sampled across the past <c>BackfillDays</c> at the day
    /// level, then weighted within the day by a cosine peaked at 14:00 UTC
    /// — gives the analytics charts a realistic diurnal silhouette without
    /// modeling per-keyword seasonality.
    /// </summary>
    private static DateTime SampleTimestamp(Options opts, Random rng)
    {
        if (opts.BackfillDays <= 0) return DateTime.UtcNow;

        var now = DateTime.UtcNow;
        var dayOffset = rng.NextDouble() * opts.BackfillDays;
        var hourWeight = SampleHourWithDiurnalWeight(rng);
        var ts = now.AddDays(-dayOffset).Date.AddHours(hourWeight);
        // Don't sample beyond now (a same-day draw might overshoot).
        return ts > now ? now : DateTime.SpecifyKind(ts, DateTimeKind.Utc);
    }

    /// <summary>
    /// Rejection-sample an hour-of-day [0, 24) weighted by
    /// <c>1 + 0.7·cos((h-14)·π/12)</c>. Peak at 14:00 UTC, trough at 02:00 UTC,
    /// peak/trough ratio ~5.7×. Cheap enough at a few attempts per call.
    /// </summary>
    private static double SampleHourWithDiurnalWeight(Random rng)
    {
        const double maxWeight = 1.7;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hour = rng.NextDouble() * 24.0;
            var weight = 1.0 + 0.7 * Math.Cos((hour - 14.0) * Math.PI / 12.0);
            if (rng.NextDouble() * maxWeight < weight) return hour;
        }
        return rng.NextDouble() * 24.0;
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

    private static string BuildSearchPayload(string phrase, string profile, string locale, int resultCount, DateTime ts)
    {
        // Hand-rolled to dodge per-request serializer overhead — this is a load
        // generator, not the system under test.
        var iso = ts.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return $"{{\"kind\":\"search\",\"phrase\":\"{Escape(phrase)}\",\"profileKey\":\"{profile}\",\"locale\":\"{locale}\",\"resultCount\":{resultCount},\"ts\":\"{iso}\"}}";
    }

    private static string BuildClickPayload(string phrase, string profile, string locale, int rank, DateTime ts, DateTime originalBucketUtc)
    {
        var tsIso = ts.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var bucketIso = originalBucketUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return $"{{\"kind\":\"click\",\"phrase\":\"{Escape(phrase)}\",\"profileKey\":\"{profile}\",\"locale\":\"{locale}\",\"rank\":{rank},\"ts\":\"{tsIso}\",\"originalBucketUtc\":\"{bucketIso}\"}}";
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// One synthetic search term plus its "realism profile" — how often the
    /// term returns zero results. <see cref="AlwaysZero"/> short-circuits the
    /// dice for terms that should always miss (typos, deliberately
    /// uncovered topics) so the zero-result analytics surfaces fill up.
    /// </summary>
    internal sealed record KeywordSpec(string Phrase, double ZeroResultRate, bool AlwaysZero = false);

    /// <summary>
    /// Builds the synthetic keyword set the loader draws from. Curated to
    /// resemble what a typical content-site SERP receives:
    ///   - Alloy demo content terms (mostly hit)
    ///   - General customer-support / e-commerce terms (mixed)
    ///   - Common typos (always zero, drives the synonym-mining surface)
    ///   - Long-tail synthetic noise (random Zipf padding)
    /// The first <c>baseSet.Count</c> entries dominate under the Zipf
    /// distribution; the rest tail off into the noise.
    /// </summary>
    internal static IReadOnlyList<KeywordSpec> BuildKeywords(int count)
    {
        var baseSet = new List<KeywordSpec>
        {
            // Alloy demo content (these match real Alloy pages — low zero rate).
            new("alloy plan",       0.05),
            new("alloy track",      0.05),
            new("alloy meet",       0.05),
            new("alloy share",      0.05),
            new("alloy planning",   0.10),

            // Customer support — fairly well-indexed.
            new("warranty",         0.10),
            new("shipping",         0.10),
            new("returns",          0.10),
            new("delivery",         0.12),
            new("tracking",         0.12),
            new("contact",          0.05),
            new("support",          0.08),
            new("billing",          0.20),
            new("invoice",          0.30),
            new("refund",           0.25),

            // Product attribute searches — mid-tail, partial coverage.
            new("size guide",       0.20),
            new("color options",    0.40),
            new("fabric care",      0.35),
            new("fit guide",        0.30),
            new("wash care",        0.25),

            // Topic searches likely missing from index — feed the synonym-coverage card.
            new("phone number",     0.0,  AlwaysZero: true),
            new("store locator",    0.0,  AlwaysZero: true),
            new("gift card",        0.0,  AlwaysZero: true),
            new("loyalty program",  0.0,  AlwaysZero: true),

            // Common typos — always zero, drive the suggested-synonyms list.
            new("shippinig",        0.0,  AlwaysZero: true),
            new("warrenty",         0.0,  AlwaysZero: true),
            new("trakcing",         0.0,  AlwaysZero: true),
            new("recieve",          0.0,  AlwaysZero: true),
        };

        var list = new List<KeywordSpec>(Math.Max(count, baseSet.Count));
        list.AddRange(baseSet);

        // Long-tail noise: random suffixes on the curated roots so the Zipf
        // sampler has a tail to draw from. Mostly hit; some zero.
        var rng = new Random(7);
        var roots = baseSet.Select(k => k.Phrase.Split(' ')[0]).Distinct().ToArray();
        while (list.Count < count)
        {
            var root = roots[list.Count % roots.Length];
            var phrase = $"{root} {rng.Next(100, 999)}";
            var zeroRate = rng.NextDouble() < 0.30 ? 0.6 : 0.15;
            list.Add(new KeywordSpec(phrase, zeroRate));
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
        Console.Error.WriteLine("Usage: gst-loadtest --url <baseUrl> [--rps N] [--duration Ns] [--concurrency N] [--phrases N] [--zipf 1.0] [--click-ratio 0.3] [--backfill-days N]");
        Console.Error.WriteLine("Defaults: --rps 500 --duration 30s --concurrency 50 --phrases 100 --zipf 1.0 --click-ratio 0.3 --backfill-days 0");
        Console.Error.WriteLine("With --backfill-days N, each event's timestamp is uniformly sampled across the past N days with diurnal hour weighting.");
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
        public required int BackfillDays { get; init; }
        public double PerWorkerRps => (double)TotalRps / Concurrency;

        public static Options? Parse(string[] args)
        {
            string? url = null;
            var rps = 500;
            var duration = TimeSpan.FromSeconds(30);
            var concurrency = 50;
            var phrases = 100;
            var zipf = 1.0;
            var clickRatio = 0.3;
            var backfillDays = 0;

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
                    case "--backfill-days" when i + 1 < args.Length: backfillDays = int.Parse(args[++i]); break;
                    case "-h" or "--help": return null;
                }
            }
            if (string.IsNullOrEmpty(url)) return null;
            if (rps < 1 || concurrency < 1 || phrases < 1 || backfillDays < 0) return null;
            return new Options
            {
                BaseUrl = url.TrimEnd('/'),
                TotalRps = rps,
                Duration = duration,
                Concurrency = concurrency,
                PhraseCount = phrases,
                ZipfExponent = zipf,
                ClickRatio = clickRatio,
                BackfillDays = backfillDays,
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
