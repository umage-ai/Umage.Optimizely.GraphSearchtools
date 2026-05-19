namespace UmageAI.Optimizely.GraphSearchTools.Configuration;

/// <summary>
/// Tunables for the local telemetry sink (channel + bucket flusher + raw ring +
/// retention + abuse caps). Bound from <c>CodeArt:GraphSearchtools:Telemetry</c>.
/// </summary>
/// <remarks>
/// Defaults are sized for the design's ~1000 RPS target with headroom: 64K queue
/// (≈ minutes of buffer), 60s flush, 10K-row raw ring, 90-day bucket retention.
/// Abuse caps assume a public-facing ingest endpoint reachable by anonymous
/// visitors — a malicious caller cannot exceed the rate limiter regardless of
/// what they post.
/// </remarks>
public class LocalTelemetryOptions
{
    /// <summary>
    /// Bounded channel capacity. Above this, <c>DropOldest</c> applies — newer
    /// events stay, older ones spill. Per design §3.1: showing the last 30s
    /// honestly under overload beats showing the previous hour and pretending
    /// the present is empty.
    /// </summary>
    public int QueueCapacity { get; set; } = 65_536;

    /// <summary>How often the bucket flusher upserts closed minute buckets to DDS.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Hard row cap on the per-instance forensic raw ring (oldest deleted FIFO once over).</summary>
    public int RawRingCapacity { get; set; } = 10_000;

    /// <summary>Time-based cap on the raw ring; rows older than this are dropped on the next trim pass.</summary>
    public TimeSpan RawRingTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long to keep aggregate buckets. Longer = fatter DDS table, more
    /// expensive cross-node sums on long windows. The default lets a forgotten
    /// dev instance forget itself; production should override per requirements.
    /// </summary>
    public TimeSpan BucketRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Stamped on every bucket row so cross-node sums can keep instances
    /// distinct. Defaults to the machine name; override when machine names
    /// collide (e.g. ephemeral container hosts).
    /// </summary>
    public string NodeId { get; set; } = Environment.MachineName;

    // ── Abuse caps (defense in depth around an open ingest endpoint) ─────

    /// <summary>
    /// Maximum POST body size accepted by the ingest endpoint, in bytes.
    /// A search/click event JSON fits in ≤ 1 KB; the default leaves room for
    /// future fields without enabling pathological payloads.
    /// </summary>
    public int MaxBodyBytes { get; set; } = 4 * 1024;

    /// <summary>
    /// Maximum normalized phrase length kept in memory and persisted. Longer
    /// phrases are truncated. Stops one giant phrase from dominating bucket
    /// dictionary memory.
    /// </summary>
    public int MaxPhraseLength { get; set; } = 256;

    /// <summary>
    /// Per-IP rate cap on the ingest endpoint. Excess requests are rejected
    /// with HTTP 429. Sized for a busy logged-in editor running search-heavy
    /// browsing without false positives.
    /// </summary>
    public int PerIpEventsPerSecond { get; set; } = 20;

    /// <summary>
    /// Global rate cap across all callers. Defense in depth — caps total cost
    /// of ingest even when an attacker rotates IPs faster than the per-IP
    /// limiter notices.
    /// </summary>
    public int GlobalEventsPerSecond { get; set; } = 2_000;

    /// <summary>
    /// Maximum number of distinct (phrase, channel, locale) keys held in the
    /// bucket flusher's open-minute dictionary. Above this, new keys evict
    /// the smallest existing entry to keep memory bounded under adversarial
    /// cardinality. Per design §6.2 the zero-result sub-dictionary is exempt.
    /// </summary>
    public int MaxOpenBuckets { get; set; } = 10_000;
}
