using EPiServer;
using EPiServer.Core;
using EPiServer.Data.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Controllers;

/// <summary>
/// Dev-only seeder used to prepare the SampleSite for README / marketing
/// screenshots. POST <c>/internal/seed-screenshots</c> from a loopback caller
/// to wipe + repopulate Graph pinned collections, Graph synonyms, and the
/// addon's local telemetry buckets with a realistic mid-tier marketing
/// dataset.
/// </summary>
/// <remarks>
/// <para>
/// Guarded three ways: <see cref="IWebHostEnvironment.IsDevelopment"/> must
/// be true, the request must originate on the loopback interface, and the
/// caller must include <c>?token={SEED_TOKEN}</c> matching the environment
/// variable. Anything else returns 404 so the endpoint is invisible to
/// scanners.
/// </para>
/// <para>
/// Idempotent: running it twice resets to the same canonical fixture rather
/// than doubling everything up. Random seeds are deterministic.
/// </para>
/// </remarks>
[Route("internal/seed-screenshots")]
public class ScreenshotSeedController : Controller
{
    private const string ChannelKey = "alloy-search";
    private const string PinnedCollectionEn = "alloy-en";
    private const string PinnedCollectionSv = "alloy-sv";

    private readonly IGraphAdminClient _graph;
    private readonly IContentLoader _contentLoader;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ScreenshotSeedController> _logger;

    public ScreenshotSeedController(
        IGraphAdminClient graph,
        IContentLoader contentLoader,
        IWebHostEnvironment env,
        ILogger<ScreenshotSeedController> logger)
    {
        _graph = graph;
        _contentLoader = contentLoader;
        _env = env;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Seed([FromQuery] string? token, CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment()) return NotFound();
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote == null || !System.Net.IPAddress.IsLoopback(remote)) return NotFound();

        var expected = Environment.GetEnvironmentVariable("SCREENSHOT_SEED_TOKEN");
        if (string.IsNullOrEmpty(expected) || token != expected) return NotFound();

        var report = new SeedReport();

        try
        {
            await ResetPinsAsync(report, cancellationToken);
            await ResetSynonymsAsync(report, cancellationToken);
            ResetTelemetry(report);

            var targets = ResolveAlloyTargets();
            report.ResolvedTargetCount = targets.Count;

            await SeedPinsAsync(report, targets, cancellationToken);
            await SeedSynonymsAsync(report, cancellationToken);
            SeedTelemetry(report);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Screenshot seed failed.");
            report.Error = ex.Message;
            return StatusCode(500, report);
        }

        return Json(report);
    }

    // ── Reset ──────────────────────────────────────────────────────────

    private async Task ResetPinsAsync(SeedReport report, CancellationToken ct)
    {
        var collections = await _graph.GetCollectionsAsync(ct);
        foreach (var col in collections)
        {
            // Drain items first — Graph's DELETE on a non-empty collection 400s
            // with VALIDATION_ERROR. The addon's PinnedService.DeleteCollection
            // does the same dance.
            var offset = 0;
            while (true)
            {
                var page = await _graph.GetItemsAsync(col.Id, ct, offset);
                if (page.Count == 0) break;
                foreach (var item in page)
                {
                    ct.ThrowIfCancellationRequested();
                    await _graph.DeleteItemAsync(col.Id, item.Id, ct);
                    report.PinsDeleted++;
                }
                if (page.Count < 20) break;
            }
            await _graph.DeleteCollectionAsync(col.Id, ct);
            report.CollectionsDeleted++;
        }
    }

    private async Task ResetSynonymsAsync(SeedReport report, CancellationToken ct)
    {
        foreach (var lang in new[] { "en", "sv", null })
        {
            try
            {
                await _graph.DeleteSynonymsAsync(new SynonymsQuery { LanguageRouting = lang }, ct);
                report.SynonymSlotsCleared++;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Slot may not exist yet — DELETE of an absent slot is fine.
            }
        }
    }

    private void ResetTelemetry(SeedReport report)
    {
        var bucketStore = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        report.BucketsDeleted = bucketStore.LoadAll<SearchLogBucket>().Count();
        bucketStore.DeleteAll();

        var ringStore = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogRing));
        report.RingDeleted = ringStore.LoadAll<SearchLogRing>().Count();
        ringStore.DeleteAll();
    }

    // ── Target resolution ──────────────────────────────────────────────

    /// <summary>
    /// Walk the published English page tree and return the page GUIDs we'll
    /// use as pin targets. We don't need every page — just enough variety
    /// that the Pinned UI's "Target" column renders distinct real names for
    /// each row. Falls back to whatever we find if the Alloy demo content
    /// has been altered.
    /// </summary>
    private IReadOnlyList<TargetCandidate> ResolveAlloyTargets()
    {
        var candidates = new List<TargetCandidate>();
        Walk(ContentReference.StartPage, candidates, depth: 0, maxDepth: 4);
        return candidates;
    }

    private void Walk(ContentReference parent, List<TargetCandidate> sink, int depth, int maxDepth)
    {
        if (depth > maxDepth || sink.Count >= 60) return;
        IEnumerable<PageData> children;
        try { children = _contentLoader.GetChildren<PageData>(parent); }
        catch (Exception) { return; }

        foreach (var child in children)
        {
            if (child.IsDeleted) continue;
            if (child.ContentGuid == Guid.Empty) continue;
            sink.Add(new TargetCandidate(child.Name, child.ContentGuid.ToString("D").ToLowerInvariant()));
            Walk(child.ContentLink, sink, depth + 1, maxDepth);
        }
    }

    /// <summary>Round-robin a target by name match (or first available).</summary>
    private static string PickTarget(IReadOnlyList<TargetCandidate> targets, string preferredNameContains)
    {
        var match = targets.FirstOrDefault(t =>
            t.Name.Contains(preferredNameContains, StringComparison.OrdinalIgnoreCase));
        return (match?.Guid) ?? targets.FirstOrDefault()?.Guid ?? Guid.NewGuid().ToString("D");
    }

    // ── Pin seeds ──────────────────────────────────────────────────────

    private async Task SeedPinsAsync(SeedReport report, IReadOnlyList<TargetCandidate> targets, CancellationToken ct)
    {
        var en = await EnsureCollectionAsync(PinnedCollectionEn, ct);
        var sv = await EnsureCollectionAsync(PinnedCollectionSv, ct);

        // 10 English-only pins. Each pin row uses a single phrase — Graph's
        // `phrases` field is matched as a literal string, not a comma-split
        // alias list, so multi-phrase rows would only elevate the result when
        // the user typed the whole comma-joined string verbatim. Aliases are
        // handled by the Synonyms tool seeded below.
        var enPins = new (string Phrases, string TargetName, double Priority)[]
        {
            ("alloy plan",          "Alloy Plan",      95),
            ("alloy track",         "Alloy Track",     92),
            ("alloy meet",          "Alloy Meet",      90),
            ("pricing",             "Reseller",        85),
            ("demo",                "Contact Us",      80),
            ("support",             "Contact Us",      78),
            ("leadership",          "Management",      75),
            ("press release",       "Press Releases",  72),
            ("events",              "Events",          68),
            ("partner",             "Reseller",        65)
        };

        foreach (var p in enPins)
        {
            await _graph.CreateItemAsync(en.Id, new PinnedItemPayload
            {
                Phrases = p.Phrases,
                TargetKey = PickTarget(targets, p.TargetName),
                Language = "en",
                Priority = p.Priority,
                IsActive = true
            }, ct);
            report.PinsCreated++;
        }

        // 10 Swedish-only pins — single phrase each, same reason. We point at
        // the EN target GUIDs because the Alloy demo doesn't ship a Swedish
        // page tree; the Pinned UI renders the English name, which is fine
        // for screenshots and still exercises the Language tag.
        var svPins = new (string Phrases, string TargetName, double Priority)[]
        {
            ("alloy plan",          "Alloy Plan",      95),
            ("alloy track",         "Alloy Track",     92),
            ("alloy meet",          "Alloy Meet",      90),
            ("pris",                "Reseller",        85),
            ("demo",                "Contact Us",      80),
            ("support",             "Contact Us",      78),
            ("ledning",             "Management",      75),
            ("pressmeddelande",     "Press Releases",  72),
            ("evenemang",           "Events",          68),
            ("partner",             "Reseller",        65)
        };

        foreach (var p in svPins)
        {
            await _graph.CreateItemAsync(sv.Id, new PinnedItemPayload
            {
                Phrases = p.Phrases,
                TargetKey = PickTarget(targets, p.TargetName),
                Language = "sv",
                Priority = p.Priority,
                IsActive = true
            }, ct);
            report.PinsCreated++;
        }

        // 10 global pins — Language=null, single phrase each, added to BOTH
        // collections so the rule applies regardless of the locale the
        // storefront resolves. Each global rule shows up twice in raw storage
        // but reads as one rule in the Pinned UI's "Global" filter.
        var globalPins = new (string Phrases, string TargetName, double Priority)[]
        {
            ("alloy",               "Start",              100),
            ("logo",                "About us",            70),
            ("headquarters",        "Contact Us",          60),
            ("amar gupta",          "Amar Gupta",          55),
            ("fiona miller",        "Fiona Miller",        55),
            ("robert carlsson",     "Robert Carlsson",     55),
            ("team alloy",          "Management",          50),
            ("careers",             "About us",            48),
            ("acclaimed",           "Alloy Meet Acclaim",  45),
            ("contact",             "Contact Us",          88)
        };

        foreach (var p in globalPins)
        {
            var targetKey = PickTarget(targets, p.TargetName);
            await _graph.CreateItemAsync(en.Id, new PinnedItemPayload
            {
                Phrases = p.Phrases,
                TargetKey = targetKey,
                Language = null,
                Priority = p.Priority,
                IsActive = true
            }, ct);
            await _graph.CreateItemAsync(sv.Id, new PinnedItemPayload
            {
                Phrases = p.Phrases,
                TargetKey = targetKey,
                Language = null,
                Priority = p.Priority,
                IsActive = true
            }, ct);
            report.PinsCreated += 2;
        }
    }

    private async Task<PinnedCollectionResult> EnsureCollectionAsync(string key, CancellationToken ct)
    {
        var existing = (await _graph.GetCollectionsAsync(ct))
            .FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        return await _graph.CreateCollectionAsync(new PinnedCollectionPayload
        {
            Key = key,
            Title = key,
            IsActive = true
        }, ct);
    }

    // ── Synonym seeds ──────────────────────────────────────────────────

    private async Task SeedSynonymsAsync(SeedReport report, CancellationToken ct)
    {
        // English: a mix of equivalent groups + a single replacement rule
        // ("whitepaper => white paper") so the Synonyms UI's "Type" column has
        // both styles in the screenshot.
        var en = string.Join('\n', new[]
        {
            "bug, issue, ticket, defect",
            "pricing, price, cost, plans, plan pricing",
            "support, help, assistance, customer service",
            "demo, trial, free trial, evaluation",
            "careers, jobs, hiring, employment",
            "contact, contact us, get in touch, reach out",
            "news, press, press release, announcements",
            "team, staff, employees, people",
            "partner, reseller, channel partner, integrator",
            "whitepaper => white paper"
        });

        var sv = string.Join('\n', new[]
        {
            "bugg, ärende, fel, defekt",
            "pris, prislista, kostnad, kostnader",
            "support, hjälp, kundtjänst",
            "demo, testversion, provversion, utvärdering",
            "karriär, jobb, lediga jobb, anställning",
            "kontakt, kontakta oss, hör av dig",
            "nyheter, pressmeddelande, pressmeddelanden",
            "team, personal, anställda, medarbetare",
            "partner, återförsäljare, samarbetspartner",
            "okej => ok"
        });

        var global = string.Join('\n', new[]
        {
            "epi, episerver, optimizely",
            "ai, artificial intelligence, machine learning, ml",
            "seo, search engine optimization",
            "cms, content management, content management system",
            "b2b, business to business",
            "saas, software as a service, cloud software",
            "crm, customer relationship management",
            "kpi, key performance indicator, metric",
            "roi, return on investment",
            "qa => quality assurance"
        });

        await _graph.UpdateSynonymsAsync(new SynonymsRequest { Content = en, LanguageRouting = "en" }, ct);
        await _graph.UpdateSynonymsAsync(new SynonymsRequest { Content = sv, LanguageRouting = "sv" }, ct);
        await _graph.UpdateSynonymsAsync(new SynonymsRequest { Content = global }, ct);
        report.SynonymSlotsWritten = 3;
        report.SynonymRulesWritten = 30;
    }

    // ── Telemetry seeds ────────────────────────────────────────────────

    /// <summary>
    /// Synthesise 30 days of search-log buckets for a mid-tier marketing site.
    /// Calibrated for visible-but-not-extreme dashboards:
    ///   • ~700 searches/day mean, with a small linear ramp + weekday/weekend
    ///     dip so the sparkline isn't flat.
    ///   • EN ≈ 85%, SV ≈ 15% of traffic.
    ///   • CTR ≈ 28%, with the long tail under that and head phrases above.
    ///   • Zero-result rate ≈ 10%, concentrated in phrases that lack pins.
    ///   • A handful of phrases trend in the last 7 days so Insights' top-
    ///     phrases list looks alive.
    /// All values are written directly to <see cref="SearchLogBucket"/> with
    /// minute-truncated UTC bucket keys — same shape <see cref="BucketFlusher"/>
    /// produces.
    /// </summary>
    private void SeedTelemetry(SeedReport report)
    {
        var rng = new Random(20260519);  // Deterministic — same seed → same fixture
        var bucketStore = DynamicDataStoreFactory.Instance.CreateStore(typeof(SearchLogBucket));
        var nodeId = Environment.MachineName;
        var nowUtc = DateTime.UtcNow;
        var todayUtc = nowUtc.Date;

        // Phrase catalogue: (display, weight, ctr, zeroRate, locale). Higher
        // weight = more share of the day's volume. Locale="" means "either".
        var phrases = new (string Display, double Weight, double Ctr, double ZeroRate, string Locale)[]
        {
            // Head — pinned, high CTR, low zero
            ("Alloy Plan",          12.0, 0.42, 0.02, "en"),
            ("Alloy Track",         11.0, 0.40, 0.02, "en"),
            ("Alloy Meet",          10.0, 0.41, 0.02, "en"),
            ("pricing",              8.0, 0.38, 0.05, "en"),
            ("demo",                 7.0, 0.35, 0.04, "en"),
            ("contact",              6.5, 0.45, 0.02, "en"),
            ("support",              6.0, 0.30, 0.10, "en"),
            ("careers",              4.5, 0.28, 0.08, "en"),
            ("partner",              4.0, 0.32, 0.10, "en"),
            ("news",                 3.5, 0.26, 0.08, "en"),
            // Mid
            ("api documentation",    3.0, 0.22, 0.18, "en"),
            ("case study",           2.8, 0.24, 0.20, "en"),
            ("white paper",          2.6, 0.20, 0.22, "en"),
            ("team",                 2.4, 0.30, 0.05, "en"),
            ("about us",             2.2, 0.45, 0.02, "en"),
            ("events",               2.0, 0.28, 0.10, "en"),
            ("press release",        1.8, 0.24, 0.12, "en"),
            ("management",           1.6, 0.34, 0.05, "en"),
            ("logo",                 1.4, 0.22, 0.30, "en"),
            ("download",             1.2, 0.18, 0.32, "en"),
            // Long tail — these often show as zero-result candidates
            ("integration",          1.0, 0.16, 0.40, "en"),
            ("compliance",           0.9, 0.14, 0.45, "en"),
            ("security",             0.9, 0.20, 0.30, "en"),
            ("gdpr",                 0.8, 0.10, 0.55, "en"),
            ("sla",                  0.7, 0.12, 0.50, "en"),
            ("changelog",            0.7, 0.14, 0.48, "en"),
            ("status page",          0.6, 0.10, 0.62, "en"),
            ("invoice",              0.5, 0.08, 0.70, "en"),
            ("cancel subscription",  0.4, 0.06, 0.75, "en"),
            ("training",             0.6, 0.18, 0.40, "en"),
            // SV traffic
            ("Alloy Plan",           2.0, 0.40, 0.04, "sv"),
            ("Alloy Track",          1.8, 0.38, 0.04, "sv"),
            ("Alloy Meet",           1.6, 0.40, 0.04, "sv"),
            ("pris",                 1.6, 0.34, 0.08, "sv"),
            ("demo",                 1.4, 0.32, 0.06, "sv"),
            ("kontakt",              1.4, 0.44, 0.02, "sv"),
            ("support",              1.2, 0.28, 0.12, "sv"),
            ("karriär",              0.9, 0.26, 0.10, "sv"),
            ("nyheter",              0.7, 0.22, 0.10, "sv"),
            ("om oss",               0.6, 0.36, 0.04, "sv"),
            ("partner",              0.5, 0.28, 0.12, "sv"),
            ("integration",          0.4, 0.14, 0.45, "sv"),
            ("gdpr",                 0.3, 0.10, 0.55, "sv"),
            // "Trending in last 7d" — start near zero, ramp
            ("ai assistant",         0.0, 0.30, 0.20, "en"),  // boosted later
            ("copilot",              0.0, 0.28, 0.25, "en"),  // boosted later
            ("alloy 13",             0.0, 0.32, 0.18, "en"),  // boosted later
        };

        double totalWeight = phrases.Sum(p => p.Weight);

        for (int dayIdx = 0; dayIdx < 30; dayIdx++)
        {
            // Day 29 = today (UTC), day 0 = 29 days ago. Walk backwards so the
            // index aligns with the Insights sparkline orientation.
            var dayUtc = todayUtc.AddDays(-(29 - dayIdx));

            // Volume model — mid-tier marketing site:
            //   base 650/day → small growth to 800/day at end of window
            //   weekday/weekend dip
            //   ±10% jitter per day
            var growth = 650 + (dayIdx * 5);
            var weekend = dayUtc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var dayVolume = (int)Math.Round(growth * (weekend ? 0.55 : 1.0) * (0.9 + rng.NextDouble() * 0.2));

            // Late-window trending phrases get a per-day boost so the
            // sparkline+top phrases on the Insights tab tell a story.
            double trendBoost = dayIdx >= 22 ? (dayIdx - 21) * 0.6 : 0.0;

            // Per-day CTR shaping so the Insights CTR sparkline isn't a flat
            // line tracking the mean. Three ingredients:
            //   • Day-of-week pattern. Weekends behave differently — visitors
            //     on a marketing site are more browse-y, less convert-y.
            //   • Two narrative beats: a "bad week" early in the window (a
            //     half-broken hero pin) and a recovery + small overshoot once
            //     it's fixed. Makes the chart investigation-worthy.
            //   • Random per-day jitter on top so adjacent days don't track.
            double dowMultiplier = dayUtc.DayOfWeek switch
            {
                DayOfWeek.Monday    => 0.95,
                DayOfWeek.Tuesday   => 1.05,
                DayOfWeek.Wednesday => 1.08,
                DayOfWeek.Thursday  => 1.04,
                DayOfWeek.Friday    => 0.96,
                DayOfWeek.Saturday  => 0.82,
                DayOfWeek.Sunday    => 0.78,
                _ => 1.0
            };
            double storyMultiplier = dayIdx switch
            {
                // Days 8–12: "bad week" — hero pin pointed at a 404'd page,
                // CTR tanks across the head.
                >= 8 and <= 12 => 0.55 + (rng.NextDouble() * 0.15),
                // Day 13: editor catches it, partial recovery.
                13 => 0.85,
                // Days 24–27: marketing push lands, sustained CTR lift.
                >= 24 and <= 27 => 1.20 + (rng.NextDouble() * 0.10),
                _ => 1.0
            };
            // ±18% gaussian-ish jitter (sum of two uniforms ≈ triangular,
            // centred near 1.0). Day-to-day independent.
            double jitter = 0.82 + ((rng.NextDouble() + rng.NextDouble()) / 2.0) * 0.36;
            double dayCtrMultiplier = dowMultiplier * storyMultiplier * jitter;

            foreach (var p in phrases)
            {
                var weight = p.Weight;
                if (p.Weight == 0.0 && trendBoost > 0)
                {
                    weight = trendBoost;
                }
                if (weight <= 0) continue;

                var shareDenominator = totalWeight + (trendBoost > 0 ? trendBoost * 3 : 0);
                var phraseHits = (int)Math.Round(dayVolume * (weight / shareDenominator));
                if (phraseHits <= 0) continue;

                var zeroes = (int)Math.Round(phraseHits * p.ZeroRate);
                var clicks = phraseHits - zeroes; // sessions that found results
                // Per-phrase jitter on top of the day-wide CTR multiplier so
                // phrases drift independently — without it every phrase moves
                // in lockstep and the head/tail CTR ratio stays artificial.
                var phraseJitter = 0.85 + rng.NextDouble() * 0.30;
                var effectiveCtr = Math.Clamp(p.Ctr * dayCtrMultiplier * phraseJitter, 0.0, 0.92);
                var totalClicks = (int)Math.Round(clicks * effectiveCtr);

                // Spread clicks 60/25/15 across ranks 1/2/3.
                var c1 = (int)Math.Round(totalClicks * 0.60);
                var c2 = (int)Math.Round(totalClicks * 0.25);
                var c3 = totalClicks - c1 - c2;
                if (c3 < 0) c3 = 0;

                // Spread the day's phrase volume across ~6 distinct minute
                // buckets so the row count looks live rather than a single
                // big lump per phrase per day.
                var bucketCount = Math.Min(6, Math.Max(1, phraseHits / 8));
                for (int b = 0; b < bucketCount; b++)
                {
                    // 09:00–18:00 working-hours spread; minute offset varies
                    // per bucket so buckets land at distinct minutes.
                    var hour = 9 + (b * 9 / bucketCount);
                    var minute = (rng.Next(0, 60));
                    var bucketUtc = dayUtc.AddHours(hour).AddMinutes(minute);

                    var hShare = SplitInteger(phraseHits, bucketCount, b);
                    var zShare = SplitInteger(zeroes, bucketCount, b);
                    var c1Share = SplitInteger(c1, bucketCount, b);
                    var c2Share = SplitInteger(c2, bucketCount, b);
                    var c3Share = SplitInteger(c3, bucketCount, b);

                    bucketStore.Save(new SearchLogBucket
                    {
                        BucketUtc = bucketUtc,
                        PhraseNorm = p.Display.Trim().ToLowerInvariant(),
                        DisplayPhrase = p.Display,
                        ChannelKey = ChannelKey,
                        Locale = p.Locale,
                        NodeId = nodeId,
                        Hits = hShare,
                        Zeroes = zShare,
                        Clicks1 = c1Share,
                        Clicks2 = c2Share,
                        Clicks3 = c3Share
                    });
                    report.BucketsCreated++;
                }
            }
        }
    }

    /// <summary>Even-as-possible split of an integer total across <paramref name="parts"/> buckets.</summary>
    private static int SplitInteger(int total, int parts, int index)
    {
        if (parts <= 0) return total;
        var quotient = total / parts;
        var remainder = total - (quotient * parts);
        return quotient + (index < remainder ? 1 : 0);
    }

    private sealed record TargetCandidate(string Name, string Guid);

    public sealed class SeedReport
    {
        public int PinsDeleted { get; set; }
        public int CollectionsDeleted { get; set; }
        public int SynonymSlotsCleared { get; set; }
        public int BucketsDeleted { get; set; }
        public int RingDeleted { get; set; }

        public int ResolvedTargetCount { get; set; }
        public int PinsCreated { get; set; }
        public int SynonymSlotsWritten { get; set; }
        public int SynonymRulesWritten { get; set; }
        public int BucketsCreated { get; set; }

        public string? Error { get; set; }
    }
}
