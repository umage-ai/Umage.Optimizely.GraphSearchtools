# Pattern: headless deployment

The CMS hosts the add-on (and the marketer-facing admin UI), but the
public-facing search page lives in a separate frontend — SPA, Next.js,
Nuxt, Astro, native app, etc. That frontend talks to Optimizely Graph
directly; the add-on's role is to give marketers a curation surface and
to receive telemetry beacons.

## Where the add-on fits

```
┌──────────────────────┐         ┌────────────────────────┐
│  Frontend (SPA/SSR)  │ ──────▶ │   Optimizely Graph     │
│                      │         └────────────────────────┘
│  search input        │
│  result list   ─────────────┐
│  click handler              │
└──────────────────────┘      │
                              │ POST /api/telemetry/searchlog
                              ▼
                  ┌──────────────────────────────┐
                  │  CMS host                    │
                  │  ┌────────────────────────┐  │
                  │  │ GraphSearchtools addon │  │
                  │  │  ingest endpoint       │  │
                  │  │  local sink → DDS      │  │
                  │  │                        │  │
                  │  │  admin UI (Search      │  │
                  │  │  channels, Pinned,     │  │
                  │  │  Synonyms, Insights)   │  │
                  │  └────────────────────────┘  │
                  └──────────────────────────────┘
```

Two integration seams:

1. **Telemetry beacons** from the frontend to the add-on's public ingest
   endpoint.
2. **Pinned-result agreement**: the channel key the marketer curates
   against in the CMS shell and the pinned-collection name your frontend
   query references must be the same.

## Telemetry from the browser

The ingest endpoint is unauthenticated, rate-limited (20 RPS/IP, 4 KB
body cap by default) and accepts the same payload as in the
[quickstart](quickstart.md).

```typescript
type GstEvent =
  | { kind: 'search'; phrase: string; channelKey: string; locale: string; resultCount: number; ts?: string }
  | { kind: 'click';  phrase: string; channelKey: string; locale: string; rank: number;        ts?: string; originalBucketUtc?: string };

function reportSearch(event: GstEvent) {
    const body = JSON.stringify(event);
    // sendBeacon is preferred — survives navigation, no preflight.
    if (navigator.sendBeacon) {
        navigator.sendBeacon('https://cms.example.com/api/telemetry/searchlog', body);
        return;
    }
    fetch('https://cms.example.com/api/telemetry/searchlog', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body,
        keepalive: true,
    });
}
```

`sendBeacon` is right for clicks (no CORS preflight, survives unload).
`fetch` is fine for non-navigation events; remember to set CORS up — see
below.

## CORS

The add-on ships no CORS configuration of its own. If your frontend is
on a different origin than the CMS, configure CORS in the host project:

```csharp
services.AddCors(o => o.AddPolicy("frontend", b => b
    .WithOrigins("https://app.example.com")
    .AllowAnyHeader()
    .WithMethods("POST")));

app.UseCors("frontend");
```

`sendBeacon` does **not** trigger a CORS preflight (it sends as
`text/plain` by default), but `fetch` with `Content-Type: application/json`
does. Either set up CORS to allow the preflight, or send beacons as
`text/plain` — the ingest endpoint reads the body, not the content type:

```typescript
navigator.sendBeacon(url, new Blob([body], { type: 'text/plain' }));
```

## Pinned-result agreement

Marketers curate pinned results in the CMS shell against a channel. The
channel's `UsesPinnedKey("site-{locale}")` resolves to a Graph
pinned-collection name (`site-en`, `site-da`, …). Your frontend's
GraphQL query must reference that same collection — see Optimizely's
[Graph documentation](https://docs.developers.optimizely.com/) for the
pinned-result query modifiers.

A pinned result curated in the CMS shell against `site-en` will silently
do nothing if the frontend query points to a different collection name.
This is the single most common misconfiguration in headless setups; the
**Search channels** detail page surfaces the resolved key so marketers
can see exactly what to put in the query.

## Click attribution

Search events form per-minute buckets keyed by (phrase, channel, locale).
A click that lands a few minutes later won't tie back unless you pass
`originalBucketUtc` — the minute-truncated timestamp of the originating
search:

```typescript
// when rendering the result list
const bucket = new Date();
bucket.setSeconds(0, 0);
const originalBucketUtc = bucket.toISOString();

// then on click:
reportSearch({
    kind: 'click',
    phrase, channelKey, locale, rank,
    originalBucketUtc,
});
```

Without it, the flusher falls back to the click's own minute — fine for
volume metrics, lossy for CTR per phrase.

## Authentication

The ingest endpoint is intentionally open. Public visitors of a headless
site aren't authenticated and shouldn't be. Defense in depth is on the
add-on side: per-IP and global rate limits, body-bytes cap, phrase
truncation, and the channel's DropOldest semantics under overload. If
your traffic profile needs tighter ceilings, raise them in
`UmageAI:GraphSearchTools:Telemetry`:

```json
"Telemetry": {
  "MaxBodyBytes": 4096,
  "RatePerIpRps": 20,
  "RateGlobalRps": 2000
}
```

See [`LocalTelemetryOptions.cs`](../../src/GraphSearchtools/Configuration/LocalTelemetryOptions.cs)
for the full tuning surface.

## What the admin UI still owns

Even in a headless deployment, the CMS shell still hosts:

- **Search channels** — proof that the frontend's pinned-collection
  name and the channel's resolved key agree.
- **Pinned results** — marketer curation, scoped by channel.
- **Synonyms** — tenant-global pools (Optimizely Graph itself does not
  support per-channel synonyms).
- **Insights** — read-side analytics over the events your frontend
  beaconed in, on both the cross-channel dashboard and the per-channel
  Insights tab.

Marketers reach all of these from **Edit → Add-ons → Graph Search
Tools** in the CMS shell, not on the frontend origin.
