# Search Telemetry — Design Proposal

> **Status (2026-05-10):** Proposal. Replaces the Phase 4 SearchLog ingest path
> (`SearchLogService.Append` → DDS row per request) with a pluggable read-side
> abstraction and an aggregate-first local store. Phase 4 ingest stays in
> place until the new pipeline is shipped behind a feature flag.

The Phase 4 telemetry ingest works for the SampleSite demo but won't survive a
real customer deployment. Two problems compound:

1. **Hot path is a SQL insert.** Every public-search hit causes one DDS
   `Save` on the request thread. At a target load of 10²–10³ requests/second
   we run out of headroom on a single instance and spend most of the wall
   clock on row inserts the marketer never reads.
2. **Read path scans raw rows.** Each Search Logs aggregation pulls every
   `SearchLogEntry` in the window into memory and LINQ-groups it. A one-week
   window at modest traffic is millions of rows, fetched in full per page
   render.

Layered on top, the addon needs to play nicely with **3rd-party telemetry
backends** (App Insights, Mixpanel, Matomo, …) that customers already pay for
and trust. Forcing all telemetry through the CMS is the wrong shape — it
double-hops the data and turns the CMS into a bottleneck for a workload it has
no advantage on.

This proposal restructures telemetry around three ideas:

- **Pluggability lives on the read side, not the write side.** The CMS asks
  *some* `ITelemetryReader` for aggregates; how the data got there is the
  reader's problem.
- **The local sink writes aggregates, not raw rows.** Per-minute buckets keyed
  by `(phrase, profile, locale, node)` collapse 1000 RPS into ~hundreds of
  upserts per minute per node.
- **The hot path never blocks on storage.** A bounded in-memory channel
  decouples the HTTP handler from the flusher; under overload we drop, never
  stall.

---

## 1. Architecture

```
                                  ┌─────────────────────────────┐
                                  │  3rd-party telemetry SDK    │
              host page  ────────►│  (App Insights / Mixpanel / │
              JS                  │   Matomo / Segment / …)     │
                  │               └──────────────┬──────────────┘
                  │                              │
                  │ (default)                    │ (optional, customer-wired)
                  ▼                              ▼
       ┌──────────────────────┐        ┌────────────────────────┐
       │ /api/telemetry/      │        │ vendor-hosted store    │
       │  searchlog           │        └────────────┬───────────┘
       └──────────┬───────────┘                     │
                  │                                 │
                  ▼                                 │
       ┌──────────────────────┐                     │
       │ Channel<SearchEvent> │                     │
       │  (bounded, drop-     │                     │
       │   oldest)            │                     │
       └──────────┬───────────┘                     │
                  │                                 │
                  ▼                                 │
       ┌──────────────────────┐                     │
       │ BucketFlusher        │                     │
       │ BackgroundService    │                     │
       └──────────┬───────────┘                     │
                  │                                 │
                  ▼                                 │
       ┌──────────────────────┐                     │
       │ DDS:                 │                     │
       │  SearchLogBucket     │                     │
       │  SearchLogRing       │                     │
       └──────────┬───────────┘                     │
                  │                                 │
                  ▼                                 ▼
              ┌─────────────────────────────────────────┐
              │           ITelemetryReader              │
              │  ┌──────────────┐  ┌────────────────┐   │
              │  │ LocalReader  │  │ AppInsights /  │   │
              │  │ (DDS-backed) │  │ Mixpanel / …   │   │
              │  └──────────────┘  └────────────────┘   │
              └─────────────────┬───────────────────────┘
                                ▼
                       Search Logs UI,
                       Pinned Coverage,
                       Synonym Coverage,
                       Profile Insights
```

The CMS-side write pipeline (the centre column) is **opt-out**: customers who
forward search events to a 3rd-party backend can disable the local sink
entirely (`UseTelemetryReader<AppInsightsTelemetryReader>()` and skip
`AddLocalTelemetrySink`) and the addon makes zero DDS rows. The read APIs the
admin UIs consume only ever talk to `ITelemetryReader`.

---

## 2. Read seam: `ITelemetryReader`

The single dependency the analytics UIs take.

```csharp
public interface ITelemetryReader
{
    Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(
        TelemetryQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(
        TelemetryQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(
        TelemetryQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<RawEvent>> RecentRawAsync(
        TelemetryQuery query, CancellationToken ct = default);
}

public sealed record TelemetryQuery(
    DateTime SinceUtc,
    DateTime UntilUtc,
    int Take,
    string? ProfileKey = null,
    string? Locale = null);

public sealed record PhraseAggregate(
    string Phrase, int Hits, double ZeroResultRate, double Ctr,
    string Locale, string ProfileKey);
```

We ship two implementations:

- **`LocalTelemetryReader`** — reads the DDS bucket table this proposal
  introduces. Default. Wired up automatically when `AddLocalTelemetrySink` is
  registered.
- **`NullTelemetryReader`** — empty results, used when no reader is wired and
  nothing is configured. Lets the UIs render an empty state with a
  "configure telemetry" link instead of crashing.

Customer adapters (`AppInsightsTelemetryReader`, `MixpanelTelemetryReader`,
`MatomoTelemetryReader`) live outside this package and only need to implement
the four methods. They translate `TelemetryQuery` to whatever query language
the backend speaks (Kusto/KQL for AI, JQL-like for Mixpanel) and shape the
result back into `PhraseAggregate`. The addon never knows the difference.

`SearchLogService` keeps its current name as a thin compatibility shim over
`ITelemetryReader` for the existing call sites in the Search Logs and
Coverage tools — it stops doing the LINQ aggregation itself.

---

## 3. Local write path

### 3.1. Hot path

`POST /api/telemetry/searchlog` → `ITelemetrySink.Record(in SearchEvent evt)` →
`Channel<SearchEvent>.Writer.TryWrite(evt)` → return 204. No awaiting, no
storage in the request thread, no exceptions surface.

```csharp
internal sealed class LocalTelemetrySink : ITelemetrySink
{
    private readonly Channel<SearchEvent> _channel;

    public LocalTelemetrySink(LocalTelemetryOptions opts)
    {
        _channel = Channel.CreateBounded<SearchEvent>(
            new BoundedChannelOptions(opts.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public void Record(in SearchEvent evt) => _channel.Writer.TryWrite(evt);

    internal ChannelReader<SearchEvent> Reader => _channel.Reader;
}
```

`SearchEvent` is a flat readonly struct (~80 bytes) so `TryWrite` is a
struct-copy + a single channel-internal slot bump. No allocations on the
caller side beyond the JSON deserialization the controller already does.

**Drop policy:** `DropOldest` over `DropNewest`. Reasoning: a stalled flusher
should not silently swallow the present minute's data — it should drop the
backlog. Marketers read Search Logs to see *what's happening now*; under
overload showing the last 30s honestly is better than showing the previous
hour and pretending the present is empty. Default capacity 64K events
(~minutes of buffer at 1000 RPS).

### 3.2. Bucket flusher

A `BackgroundService` reads from the channel and folds events into an
in-memory dictionary keyed by `(minute, phraseNorm, profileKey, locale)`.
Each value carries `Hits`, `Zeroes`, and `Clicks[1..3]`.

```csharp
private readonly Dictionary<BucketKey, BucketCounters> _open = new();

protected override async Task ExecuteAsync(CancellationToken ct)
{
    var flushTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));

    var read = ReadLoop(ct);
    var flush = FlushLoop(flushTimer, ct);
    await Task.WhenAll(read, flush);
}
```

The flusher upserts closed buckets to DDS (see §4) every 60s, stamping each
row with the local `NodeId` (process Guid generated on startup). Open buckets
for the current minute stay in memory until they roll over.

Observability: expose two counters via the existing `Health` tool —
`telemetry.queue.depth` and `telemetry.dropped.total` (cumulative since
startup). The `/health` snapshot already plumbs runtime metrics; we tack
these on.

### 3.3. Raw ring

A small bounded ring buffer keeps the last ~1h of raw events for forensic
drill-down (the "Raw events" card in the Search Logs UI). Implementation: a
DDS table `SearchLogRing` with a hard cap (default 10 000 rows) trimmed on
each flush — oldest rows deleted FIFO once over the cap. This is
*per-instance*; cross-node forensics requires `ITelemetryReader.RecentRawAsync`
to query each node's ring (or accept a single-node sample, which is what we
default to — forensics is rarely "I need the rows from every machine").

The ring is the only non-aggregated storage we keep. Everything else reads
buckets.

---

## 4. Bucket data model

```csharp
[EPiServerDataStore(StoreName = "GraphSearchtools_SearchLogBucket")]
public class SearchLogBucket : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex] public DateTime BucketUtc { get; set; }   // minute-truncated
    [EPiServerDataIndex] public string PhraseNorm { get; set; }    // trim/lower
    [EPiServerDataIndex] public string ProfileKey { get; set; }
    [EPiServerDataIndex] public string Locale { get; set; }
    [EPiServerDataIndex] public string NodeId { get; set; }

    public string DisplayPhrase { get; set; }                      // most-common cased form
    public int Hits { get; set; }
    public int Zeroes { get; set; }
    public int Clicks1 { get; set; }                               // rank == 1
    public int Clicks2 { get; set; }                               // rank == 2
    public int Clicks3 { get; set; }                               // rank == 3
}
```

Cardinality budget: at 1000 RPS with a realistic distinct-phrase rate of
~10²/min, expect ~100–500 bucket rows per minute per node. Per day per node
that's ~150K rows — well within DDS comfort, and easy to retention-prune.

**Retention:** drop buckets older than 90 days (default). A `ISchedulerJob`
wakes nightly and deletes old rows. The default exists so a forgotten dev
instance doesn't accrue forever; production deployments with long retention
needs override the value.

**Cross-node read:** `LocalTelemetryReader` sums across nodes at read time:

```csharp
var rows = store.Items<SearchLogBucket>()
    .Where(b => b.BucketUtc >= q.SinceUtc && b.BucketUtc < q.UntilUtc)
    .Where(filterByProfile/locale)
    .GroupBy(b => new { b.PhraseNorm, b.ProfileKey, b.Locale })
    .Select(g => new PhraseAggregate(...));
```

Optional later: a periodic compaction job (singleton across the cluster via
`IExclusiveLock`) merges per-node minute buckets into a single per-cluster
hour bucket once they're more than 24h old, dropping the `NodeId` column.
Cuts the read-side fan-in for long windows. Not needed in v1.

---

## 5. Click attribution

Search and click are two separate events arriving seconds apart from the host
page. Today's row-mutation approach (`SearchLogEntry.TopResultRank` updated in
place) doesn't survive aggregate-only — there is no row to mutate.

The new contract: the host SDK echoes back the bucket key on the click event.

```jsonc
// Search event
{ "kind": "search", "phrase": "warranty", "profileKey": "kb-search",
  "locale": "en", "resultCount": 12, "ts": "..." }

// Click event (sent on result click, after the search event)
{ "kind": "click", "phrase": "warranty", "profileKey": "kb-search",
  "locale": "en", "rank": 1, "originalBucketUtc": "..." }
```

The flusher folds clicks into the matching open bucket if `originalBucketUtc`
is the current open minute, otherwise into a delayed-update closed-bucket
(small staging dictionary, applied on next DDS upsert).

`originalBucketUtc` is what makes the contract reliable: if the user clicks
30 seconds after searching, the bucket may have already rolled over — the
flusher uses that timestamp to find the right closed bucket. Keep it ISO-8601
and minute-truncated client-side; the SDK does this once.

The `GraphSearchtools.Telemetry` host SDK (referenced in the implementation
plan but not yet shipped) is where this contract lives. The host integrator
calls `Telemetry.LogSearch(...)` and `Telemetry.LogClick(...)` and the SDK
takes care of the bookkeeping.

---

## 6. What we lose by aggregating

Five things degrade meaningfully relative to "raw rows for everything":

1. **Forensic drill-down across nodes.** The raw ring is per-instance and
   capped. *Mitigation:* the ring is enough for the SearchLogs "Raw events"
   card on a given instance; cross-node forensics is rare and a clear
   non-goal for v1.
2. **Long-tail phrase visibility.** If we top-K cap the in-memory bucket
   dictionary to defend against adversarial cardinality (which we have to —
   bots can blow it up), rare phrases get dropped. That kills the main signal
   the Synonym Coverage tool runs on. *Mitigation:* the zero-result dimension
   is uncapped — an unbounded sub-dictionary tracks `(minute, phraseNorm)`
   for events with `resultCount == 0` only. Healthy systems have low zero-
   result volume; broken systems have a lot, but those are exactly the ones
   we want to see in full.
3. **New aggregations after the fact.** The bucket schema bakes in the
   dimensions: phrase, profile, locale. Adding "split by user-agent class"
   tomorrow means starting collection over for that dimension. *Mitigation:*
   accept it. The current dimensions are the ones every Phase 4 UI uses;
   we'll add dimensions intentionally with a schema bump if we ever need to.
4. **Session reconstruction / refinement chains.** "User searched X, got
   nothing, then searched Y" is a session story that needs ordered raw
   events. *Mitigation:* none in v1 — the existing UIs don't surface this,
   and 3rd-party readers (Mixpanel especially) are better at it anyway.
5. **Click ↔ search correlation breaks if the SDK contract isn't honoured.**
   A custom integrator who hand-rolls the POST body and forgets
   `originalBucketUtc` gets clicks dropped. *Mitigation:* make the host SDK
   the strongly recommended path; document the protocol; on the server side,
   when `originalBucketUtc` is missing, fold into the click's own current
   minute as a best-effort fallback.

---

## 7. Configuration surface

```csharp
services.AddGraphSearchtools(opt => { /* … */ })
    .AddLocalTelemetrySink(opt =>
    {
        opt.QueueCapacity = 65_536;            // events
        opt.FlushInterval = TimeSpan.FromSeconds(60);
        opt.RawRingCapacity = 10_000;
        opt.RawRingTtl = TimeSpan.FromHours(1);
        opt.BucketRetention = TimeSpan.FromDays(90);
        opt.NodeId = Environment.MachineName;  // override if needed
    });
```

Customers using a 3rd-party reader instead:

```csharp
services.AddGraphSearchtools(opt => { /* … */ })
    .AddTelemetryReader<AppInsightsTelemetryReader>(opt =>
    {
        opt.WorkspaceId = "...";
        opt.QueryTimeout = TimeSpan.FromSeconds(10);
    });
// No AddLocalTelemetrySink — local store is not registered, no DDS writes.
```

The `/api/telemetry/searchlog` endpoint stays mounted in both modes (so the
host SDK doesn't need to branch on backend), but in the 3rd-party-only mode
it returns 410 Gone with a "telemetry sink not configured" body and emits no
DDS row. The host SDK detects the 410 once and disables the call. This
preserves a single host-SDK code path across deployments.

---

## 8. Migration

The existing Phase 4 ingest stays in place, behind the new flag:

1. **v0.5.x patch (this work):** Add `ITelemetryReader`, `ITelemetrySink`,
   `LocalTelemetrySink`, `LocalTelemetryReader`, `SearchLogBucket`,
   `SearchLogRing`. Keep `SearchLogEntry` untouched. Default DI wiring uses
   the new path; an opt-in `UseLegacySearchLogIngest()` extension keeps the
   old `SearchLogService.Append` path active for staging comparison.
2. **One minor version of dual-running.** Customers verify that the new
   bucket data lines up with the legacy raw-row aggregations on their own
   traffic. Phase 4 UIs read from `ITelemetryReader` exclusively — they
   stop touching `SearchLogEntry` — but the legacy ingest still appends
   rows that nothing reads from, for safety.
3. **v0.6.0:** Delete `SearchLogService.Append`/`AppendBatch` and the
   `SearchLogEntry` table. The legacy migration script drops the DDS table.

The host SDK never changes. The wire format gains the optional
`originalBucketUtc` field on click events — old clients that don't send it
fall through to the best-effort path described in §5.

---

## 9. Open questions

- **Raw retention configurable, or always-1h?** Leaning configurable
  (`RawRingCapacity` + `RawRingTtl`), defaulting to 1h × 10K — but a
  customer with a privacy posture that says "no raw at all" should be able
  to set capacity to 0 and disable the ring entirely. Confirm we expose that.
- **Compaction job in v1, or read-time sum forever?** Read-time sum scales
  fine to ~50 nodes × 90 days at the cardinality we expect (rough
  back-of-envelope: 50 × 150K × 90 = 675M rows worst case, probably 10–50×
  smaller in practice). Not free, but not blocking. Defer compaction to v0.7
  unless a customer hits a wall.
- **Public surface of `SearchLogService`.** Today it's a concrete class with
  virtual methods, `public` for testability. Internal callers are all in our
  own assembly. Consider making `SearchLogService` `internal` and exposing
  only `ITelemetryReader` publicly — would free us to refactor more
  aggressively in later phases.
- **Multi-tenant deployments.** `NodeId` is per-process. If a single Optimizely
  install hosts multiple sites we may want a `SiteKey` dimension on the
  bucket. Drop for v1; revisit if asked.

---

## 10. Out of scope

- Real-time streaming (websocket/SSE) of telemetry to the admin UI. The
  Search Logs page polls today and that's fine.
- A self-hosted ClickHouse / TimescaleDB sink. Customers who outgrow DDS
  should wire up a 3rd-party reader; we don't compete with that market.
- PII redaction in raw events. The host SDK already controls what it sends;
  scrubbing happens upstream of `/api/telemetry/searchlog`.
- Cross-region aggregation. A single Optimizely install is one region;
  multi-region is the customer's load balancer's problem.
