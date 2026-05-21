# Pattern: 3rd-party telemetry

Two distinct seams. Pick one — or both — depending on what your
analytics platform owns:

| Goal                                                            | Seam                                                                                                                  |
| --------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| Fan events out to App Insights / GA / Matomo / Mixpanel **as well as** the addon's local store | Decorate [`ITelemetrySink`](../../src/GraphSearchtools/Abstractions/ITelemetrySink.cs) — *write side*               |
| Drive the **Insights** tool from your own data warehouse instead of the addon's DDS aggregates | Replace [`ITelemetryReader`](../../src/GraphSearchtools/Abstractions/ITelemetryReader.cs) via `UseExternalTelemetryReader<T>()` — *read side* |

## Forwarding events (decorator on `ITelemetrySink`)

The sink contract is non-blocking, total, and must not throw. A
decorator that forwards to a 3rd-party SDK has to honour the same
constraints — wrap any failures, never block the caller.

```csharp
public sealed class AppInsightsSinkDecorator : ITelemetrySink
{
    private readonly ITelemetrySink _inner;
    private readonly TelemetryClient _appInsights;

    public AppInsightsSinkDecorator(ITelemetrySink inner, TelemetryClient appInsights)
    {
        _inner = inner;
        _appInsights = appInsights;
    }

    public void Record(in SearchEvent e)
    {
        try
        {
            _appInsights.TrackEvent("site.search", new Dictionary<string, string>
            {
                ["phrase"] = e.Phrase,
                ["channelKey"] = e.ChannelKey,
                ["locale"] = e.Locale,
                ["resultCount"] = e.ResultCount.ToString(),
            });
        }
        catch { /* never throw from a sink */ }

        _inner.Record(in e);
    }

    public void Record(in ClickEvent e)
    {
        try
        {
            _appInsights.TrackEvent("site.search.click", new Dictionary<string, string>
            {
                ["phrase"] = e.Phrase,
                ["channelKey"] = e.ChannelKey,
                ["locale"] = e.Locale,
                ["rank"] = e.Rank.ToString(),
            });
        }
        catch { }

        _inner.Record(in e);
    }
}
```

Register the decorator *after* `AddGraphSearchtools(...)`, replacing the
default singleton with one that captures the existing implementation:

```csharp
services.AddGraphSearchtools(...);

services.Decorate<ITelemetrySink, AppInsightsSinkDecorator>();
// Or by hand if you don't use Scrutor:
// var inner = services.Single(d => d.ServiceType == typeof(ITelemetrySink));
// services.Remove(inner);
// services.AddSingleton<ITelemetrySink>(sp =>
//     new AppInsightsSinkDecorator(
//         (ITelemetrySink)ActivatorUtilities.CreateInstance(sp, inner.ImplementationType!),
//         sp.GetRequiredService<TelemetryClient>()));
```

Events still land in DDS and the addon's Insights surfaces (the
top-level dashboard and the per-channel Insights tab) continue to work
normally; the 3rd-party platform gets a parallel copy.

### Fan-out without the local store

If you want the 3rd-party platform to be the *only* destination — no
local DDS aggregates — pair the decorator approach with
`UseExternalTelemetryReader<T>()` (next section). The ingest endpoint
will return `410 Gone` (because no local sink is wired) and the addon's
own beacons stop calling. Your frontend should beacon directly to the
3rd-party SDK instead.

## Sourcing aggregates from an external system (`ITelemetryReader`)

When your analytics platform is the source of truth for aggregates, the
addon's Insights tool can read from it directly. Implement
[`ITelemetryReader`](../../src/GraphSearchtools/Abstractions/ITelemetryReader.cs)
and register it via the builder:

```csharp
public sealed class AppInsightsTelemetryReader : ITelemetryReader
{
    public Task<IReadOnlyList<PhraseAggregate>> TopPhrasesAsync(TelemetryQuery q, CancellationToken ct) { /* ... */ }
    public Task<IReadOnlyList<PhraseAggregate>> ZeroResultPhrasesAsync(TelemetryQuery q, CancellationToken ct) { /* ... */ }
    public Task<IReadOnlyList<PhraseAggregate>> LowCtrPhrasesAsync(TelemetryQuery q, CancellationToken ct) { /* ... */ }
    public Task<IReadOnlyList<RawEvent>>        RecentRawAsync(TelemetryQuery q, CancellationToken ct)        { /* ... */ }
    public Task<IReadOnlyList<DailyAggregate>>  DailyTotalsAsync(TelemetryQuery q, CancellationToken ct)      { /* ... */ }
}

services.AddGraphSearchtools(...)
    .UseExternalTelemetryReader<AppInsightsTelemetryReader>();
```

What this does:

- Removes `LocalTelemetrySink`, `LocalTelemetryReader`, and the
  `BucketFlusher` background service from DI.
- The public ingest endpoint starts returning `410 Gone` so any
  browser-side beacon SDK stops calling. Your frontend must beacon to
  your analytics platform's SDK instead.
- The **Insights** UI (top-level dashboard + per-channel Insights tab)
  queries your reader; the internal `RecentRawAsync` endpoint falls
  back to whatever your reader returns (return an empty list if your
  platform doesn't expose raw events).
- The JSON **Health** endpoint on the Overview controller reports
  `sink: "external"` and omits the queue/drop counters since there's no
  local queue.

## Choosing

- **Decorator only** — addon owns the analytics surface, 3rd party gets
  a copy for cross-product dashboards or marketing-ops queries.
- **External reader only** — your analytics platform is the source of
  truth; the addon's UI is a thin façade over it.
- **Both** — exotic, but valid: forward events to your warehouse via
  the decorator, then read them back via the reader. The local DDS
  store sits in the middle as a hot cache the reader can ignore.

Most integrations only need the decorator. Reach for the reader
replacement only when retention requirements, query-shape requirements,
or compliance constraints rule out the local DDS store.
