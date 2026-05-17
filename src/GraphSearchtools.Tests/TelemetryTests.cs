using EPiServer.Shell.Modules;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Infrastructure;
using UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

namespace UmageAI.Optimizely.GraphSearchTools.Tests;

/// <summary>
/// Telemetry data-plane tests. Persistence-side behaviour (DDS upserts, ring
/// trimming, retention) needs an EPiServer runtime and is exercised end-to-end
/// against a sample-site instance instead of mocked here.
/// </summary>
public class TelemetryTests
{
    // ── Sink: hot-path contract ──────────────────────────────────────────

    [Fact]
    public void Sink_Record_NeverThrowsAndAlwaysReturnsImmediately()
    {
        var sink = NewSink(opts => opts.QueueCapacity = 8);

        // Many writers, tiny channel: we must not throw, block, or backpressure.
        var act = () =>
        {
            for (var i = 0; i < 1000; i++)
            {
                sink.Record(new SearchEvent($"phrase-{i}", "p", "en", 1, DateTime.UtcNow));
                sink.Record(new ClickEvent($"phrase-{i}", "p", "en", 1, DateTime.UtcNow, null));
            }
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Sink_Record_DropsOldestUnderOverloadAndReportsDropped()
    {
        var sink = NewSink(opts => opts.QueueCapacity = 16);

        // 100x overload — DropOldest semantics keep the channel at cap.
        for (var i = 0; i < 1600; i++)
        {
            sink.Record(new SearchEvent("p", "p", "en", 1, DateTime.UtcNow));
        }

        sink.ApproximateQueueDepth.Should().Be(16);
        sink.ApproximateDroppedTotal.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Sink_TruncatesPhraseLongerThanMaxPhraseLength()
    {
        var sink = NewSink(opts => opts.MaxPhraseLength = 10);
        var huge = new string('x', 1000);

        sink.Record(new SearchEvent(huge, "p", "en", 1, DateTime.UtcNow));

        // Read the truncated event back from the channel.
        sink.Reader.TryRead(out var item).Should().BeTrue();
        item.Phrase.Length.Should().Be(10);
    }

    // ── AbuseGuard: rate caps ────────────────────────────────────────────

    [Fact]
    public void AbuseGuard_AdmitsUpToPerIpLimitWithinOneSecond()
    {
        var guard = NewGuard(opts =>
        {
            opts.PerIpEventsPerSecond = 5;
            opts.GlobalEventsPerSecond = 1000;
        });

        var admitted = 0;
        for (var i = 0; i < 20; i++) if (guard.TryAdmit("1.1.1.1")) admitted++;

        admitted.Should().Be(5);
    }

    [Fact]
    public void AbuseGuard_GlobalLimitTrumpsPerIp()
    {
        var guard = NewGuard(opts =>
        {
            opts.PerIpEventsPerSecond = 100;
            opts.GlobalEventsPerSecond = 3;
        });

        var admittedA = guard.TryAdmit("1.1.1.1") && guard.TryAdmit("1.1.1.1");
        var admittedB = guard.TryAdmit("2.2.2.2"); // within global cap so far (3rd request)
        var admittedC = guard.TryAdmit("3.3.3.3"); // 4th — global exhausted

        admittedA.Should().BeTrue();
        admittedB.Should().BeTrue();
        admittedC.Should().BeFalse();
    }

    [Fact]
    public void AbuseGuard_NullIpFallsBackToBucketedKey()
    {
        var guard = NewGuard(opts =>
        {
            opts.PerIpEventsPerSecond = 2;
            opts.GlobalEventsPerSecond = 1000;
        });

        guard.TryAdmit(null).Should().BeTrue();
        guard.TryAdmit(null).Should().BeTrue();
        guard.TryAdmit(null).Should().BeFalse();
    }

    // ── BucketFlusher: aggregation logic ─────────────────────────────────

    [Fact]
    public void BucketFlusher_FoldsRepeatedSearchIntoOneBucket()
    {
        var flusher = NewFlusher(out var sink, opts => opts.MaxOpenBuckets = 100);
        var t = new DateTime(2026, 5, 10, 12, 30, 45, DateTimeKind.Utc);

        for (var i = 0; i < 5; i++)
            flusher.Apply(TelemetryQueueItem.ForSearch("Warranty", "kb", "en", 12, t));

        var snap = flusher.GetTestSnapshot();
        snap.Open.Should().HaveCount(1);
        snap.Open[0].Hits.Should().Be(5);
        snap.Open[0].Zeroes.Should().Be(0);
        snap.Open[0].PhraseNorm.Should().Be("warranty");
        snap.Open[0].DisplayPhrase.Should().Be("Warranty");
        snap.Open[0].BucketUtc.Should().Be(new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void BucketFlusher_CountsZeroResultEventsSeparately()
    {
        var flusher = NewFlusher(out _, _ => { });
        var t = new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc);

        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 0, t));
        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 5, t));
        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 0, t));

        var snap = flusher.GetTestSnapshot();
        snap.Open[0].Hits.Should().Be(3);
        snap.Open[0].Zeroes.Should().Be(2);
    }

    [Fact]
    public void BucketFlusher_AttributesClickToOriginalBucketWhenSet()
    {
        var flusher = NewFlusher(out _, _ => { });
        var searchAt = new DateTime(2026, 5, 10, 12, 30, 5, DateTimeKind.Utc);
        var clickAt = new DateTime(2026, 5, 10, 12, 31, 30, DateTimeKind.Utc); // next minute

        flusher.Apply(TelemetryQueueItem.ForSearch("warranty", "kb", "en", 1, searchAt));
        // OriginalBucketUtc points back to the searchAt minute.
        flusher.Apply(TelemetryQueueItem.ForClick("warranty", "kb", "en", 1, clickAt, searchAt));

        var snap = flusher.GetTestSnapshot();
        snap.Open.Should().HaveCount(1);
        snap.Open[0].BucketUtc.Should().Be(new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc));
        snap.Open[0].Clicks1.Should().Be(1);
        snap.Delayed.Should().BeEmpty();
    }

    [Fact]
    public void BucketFlusher_StagesClickAsDelayedWhenBucketAlreadyClosed()
    {
        var flusher = NewFlusher(out _, _ => { });
        var earlierMinute = new DateTime(2026, 5, 10, 11, 0, 0, DateTimeKind.Utc);
        var clickAt = DateTime.UtcNow;

        // No matching open bucket — click should land in the delayed staging dict
        // for next-flush DDS reconciliation.
        flusher.Apply(TelemetryQueueItem.ForClick("warranty", "kb", "en", 2, clickAt, earlierMinute));

        var snap = flusher.GetTestSnapshot();
        snap.Open.Should().BeEmpty();
        snap.Delayed.Should().HaveCount(1);
        snap.Delayed[0].BucketUtc.Should().Be(earlierMinute);
        snap.Delayed[0].Clicks2.Should().Be(1);
    }

    [Fact]
    public void BucketFlusher_FallsBackToClickMinuteWhenOriginalBucketAbsent()
    {
        var flusher = NewFlusher(out _, _ => { });
        var clickAt = new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc);
        flusher.Apply(TelemetryQueueItem.ForSearch("warranty", "kb", "en", 1, clickAt));

        // No OriginalBucketUtc — the click attributes against its own minute.
        flusher.Apply(TelemetryQueueItem.ForClick("warranty", "kb", "en", 3, clickAt, null));

        var snap = flusher.GetTestSnapshot();
        snap.Open[0].Clicks3.Should().Be(1);
        snap.Delayed.Should().BeEmpty();
    }

    [Fact]
    public void BucketFlusher_OverflowZeroResultGoesToUncappedSubDictionary()
    {
        // Cap of 2 — third distinct phrase can't enter the main dict but if
        // it's a zero-result event the sub-dict still tracks it (per design §6.2).
        var flusher = NewFlusher(out _, opts => opts.MaxOpenBuckets = 2);
        var t = new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc);

        flusher.Apply(TelemetryQueueItem.ForSearch("first", "kb", "en", 1, t));
        flusher.Apply(TelemetryQueueItem.ForSearch("second", "kb", "en", 1, t));
        // Third phrase, hits the zero-result fallback path:
        flusher.Apply(TelemetryQueueItem.ForSearch("rare", "kb", "en", 0, t));
        flusher.Apply(TelemetryQueueItem.ForSearch("rare", "kb", "en", 0, t));
        // A non-zero-result third phrase is dropped entirely:
        flusher.Apply(TelemetryQueueItem.ForSearch("hot", "kb", "en", 5, t));

        var snap = flusher.GetTestSnapshot();
        snap.Open.Should().HaveCount(2);
        snap.ZeroOnly.Should().HaveCount(1);
        snap.ZeroOnly[0].PhraseNorm.Should().Be("rare");
        snap.ZeroOnly[0].Zeroes.Should().Be(2);
    }

    [Fact]
    public void BucketFlusher_PartitionsBucketsByMinute()
    {
        var flusher = NewFlusher(out _, _ => { });
        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 1,
            new DateTime(2026, 5, 10, 12, 30, 5, DateTimeKind.Utc)));
        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 1,
            new DateTime(2026, 5, 10, 12, 31, 5, DateTimeKind.Utc)));
        flusher.Apply(TelemetryQueueItem.ForSearch("p", "kb", "en", 1,
            new DateTime(2026, 5, 10, 12, 31, 59, DateTimeKind.Utc))); // same minute as previous

        var snap = flusher.GetTestSnapshot();
        snap.Open.Should().HaveCount(2);
        snap.Open.Should().ContainSingle(b => b.BucketUtc.Minute == 30 && b.Hits == 1);
        snap.Open.Should().ContainSingle(b => b.BucketUtc.Minute == 31 && b.Hits == 2);
    }

    // ── Static helpers ───────────────────────────────────────────────────

    [Fact]
    public void BucketFlusher_NormalizePhrase_LowersAndTrims()
    {
        BucketFlusher.NormalizePhrase("  Hello WORLD  ").Should().Be("hello world");
        BucketFlusher.NormalizePhrase(null).Should().Be("");
        BucketFlusher.NormalizePhrase("   ").Should().Be("");
    }

    [Fact]
    public void BucketFlusher_TruncateToMinute_ZerosSubminuteFields()
    {
        var t = new DateTime(2026, 5, 10, 12, 30, 45, 123, DateTimeKind.Utc);
        var truncated = BucketFlusher.TruncateToMinute(t);
        truncated.Should().Be(new DateTime(2026, 5, 10, 12, 30, 0, DateTimeKind.Utc));
        truncated.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ── NullTelemetryReader: no-op contract ──────────────────────────────

    [Fact]
    public async Task NullReader_ReturnsEmptyForEveryQuery()
    {
        var reader = new NullTelemetryReader();
        var q = new TelemetryQuery(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 100);

        (await reader.TopPhrasesAsync(q)).Should().BeEmpty();
        (await reader.ZeroResultPhrasesAsync(q)).Should().BeEmpty();
        (await reader.LowCtrPhrasesAsync(q)).Should().BeEmpty();
        (await reader.RecentRawAsync(q)).Should().BeEmpty();
    }

    // ── DI: registration surface ─────────────────────────────────────────

    [Fact]
    public void AddGraphSearchtools_RegistersLocalSinkFlusherAndReader()
    {
        var services = NewServices();
        services.AddGraphSearchtools(_ => { });

        services.Should().Contain(d => d.ServiceType == typeof(ITelemetrySink));
        services.Should().Contain(d => d.ServiceType == typeof(ITelemetryReader));
        services.Should().Contain(d => d.ServiceType == typeof(ITelemetryMetrics));
        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(BucketFlusher));
    }

    [Fact]
    public void UseExternalTelemetryReader_RemovesLocalSinkAndFlusherAndReplacesReader()
    {
        var services = NewServices();
        var builder = services.AddGraphSearchtools(_ => { });
        builder.UseExternalTelemetryReader<FakeExternalReader>();

        services.Should().NotContain(d => d.ServiceType == typeof(ITelemetrySink));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(BucketFlusher));
        services.Should().Contain(d =>
            d.ServiceType == typeof(ITelemetryReader) && d.ImplementationType == typeof(FakeExternalReader));
    }

    private sealed class FakeExternalReader : ITelemetryReader
    {
        public Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());
        public Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(TelemetryQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());
        public Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(TelemetryQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PhraseAggregate>>(Array.Empty<PhraseAggregate>());
        public Task<IReadOnlyList<RawEvent>> RecentRawAsync(TelemetryQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RawEvent>>(Array.Empty<RawEvent>());
        public Task<IReadOnlyList<DailyAggregate>> DailyTotalsAsync(TelemetryQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DailyAggregate>>(Array.Empty<DailyAggregate>());
    }

    // ── Construction helpers ─────────────────────────────────────────────

    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddOptions();
        services.AddAuthorizationCore();
        return services;
    }

    private static LocalTelemetrySink NewSink(Action<LocalTelemetryOptions>? configure = null)
        => new(BuildOptions(configure));

    private static TelemetryAbuseGuard NewGuard(Action<LocalTelemetryOptions> configure)
        => new(BuildOptions(configure));

    private static BucketFlusher NewFlusher(out LocalTelemetrySink sink, Action<LocalTelemetryOptions> configure)
    {
        var opts = BuildOptions(configure);
        sink = new LocalTelemetrySink(opts);
        return new BucketFlusher(sink, opts, NullLogger<BucketFlusher>.Instance);
    }

    private static IOptions<GraphSearchtoolsOptions> BuildOptions(Action<LocalTelemetryOptions>? configure)
    {
        var opts = new GraphSearchtoolsOptions();
        configure?.Invoke(opts.Telemetry);
        return Options.Create(opts);
    }
}
