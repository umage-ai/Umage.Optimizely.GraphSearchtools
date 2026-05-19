using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// One row per (minute, phrase, channel, locale, node). The flusher upserts
/// closed-minute buckets in batches; the local reader sums across nodes at
/// query time. Cardinality budget per design §4: ~150K rows/day per node,
/// well within DDS comfort.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SearchLogBucket")]
public class SearchLogBucket : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>Minute-truncated UTC timestamp. Indexed because every read filters by window.</summary>
    [EPiServerDataIndex] public DateTime BucketUtc { get; set; }

    /// <summary>
    /// Trim/lower of the originating phrase. Not indexed: the reader never
    /// filters by phrase (it groups by phrase as the result), and the
    /// flusher's upsert-lookup runs over the small per-minute slice the
    /// BucketUtc index already reduces to.
    /// </summary>
    public string PhraseNorm { get; set; } = string.Empty;

    /// <summary>
    /// Channel key. Indexed because Channel Insights filters by it on every
    /// read, and a per-channel dashboard is the most common drill-down.
    /// </summary>
    [EPiServerDataIndex] public string ChannelKey { get; set; } = string.Empty;

    /// <summary>
    /// Locale. Indexed because the per-locale view of the Search Logs UI
    /// filters by it. DDS supports three Indexed_String columns and this is
    /// the third — adding a fourth would silently collide with one of the
    /// other indexed strings, which is what NodeId / PhraseNorm gave up.
    /// </summary>
    [EPiServerDataIndex] public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Originating instance. Not indexed: the reader sums across nodes at
    /// query time so this is never a filter; the flusher's upsert lookup
    /// uses it equality-only on a small per-minute slice.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Most-common cased form observed for the phrase in this bucket. Kept so
    /// the UI doesn't render every phrase lowercased — the normalized key is
    /// for grouping, this is for display.
    /// </summary>
    public string DisplayPhrase { get; set; } = string.Empty;

    public int Hits { get; set; }
    public int Zeroes { get; set; }

    /// <summary>Click counts at top three result positions. Higher ranks are not tracked
    /// — the read side cares about whether the top hit was useful, not whether
    /// position 17 was clicked.</summary>
    public int Clicks1 { get; set; }
    public int Clicks2 { get; set; }
    public int Clicks3 { get; set; }
}

/// <summary>
/// Per-instance forensic ring buffer. Only non-aggregated storage we keep.
/// Hard cap (<see cref="Configuration.LocalTelemetryOptions.RawRingCapacity"/>)
/// trimmed FIFO on each flush; rows older than
/// <see cref="Configuration.LocalTelemetryOptions.RawRingTtl"/> dropped on the
/// retention pass.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SearchLogRing")]
public class SearchLogRing : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex] public DateTime TimestampUtc { get; set; }

    /// <summary>"search" or "click".</summary>
    public string Kind { get; set; } = "search";

    public string Phrase { get; set; } = string.Empty;
    public string ChannelKey { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;

    /// <summary>Set on search events. Null on click events.</summary>
    public int? ResultCount { get; set; }

    /// <summary>Set on click events (1-based). Null on search events.</summary>
    public int? ClickRank { get; set; }

    public string NodeId { get; set; } = string.Empty;
}
