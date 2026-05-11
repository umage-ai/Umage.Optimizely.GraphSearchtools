using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// One row per (minute, phrase, profile, locale, node). The flusher upserts
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

    /// <summary>Trim/lower of the originating phrase. The dictionary key.</summary>
    [EPiServerDataIndex] public string PhraseNorm { get; set; } = string.Empty;

    [EPiServerDataIndex] public string ProfileKey { get; set; } = string.Empty;
    [EPiServerDataIndex] public string Locale { get; set; } = string.Empty;
    [EPiServerDataIndex] public string NodeId { get; set; } = string.Empty;

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
    public string ProfileKey { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;

    /// <summary>Set on search events. Null on click events.</summary>
    public int? ResultCount { get; set; }

    /// <summary>Set on click events (1-based). Null on search events.</summary>
    public int? ClickRank { get; set; }

    public string NodeId { get; set; } = string.Empty;
}
