using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// DDS row recording one observed public-search event. Phase 4 analytics tools
/// (Search Logs UI, Pinned Result Coverage, Synonym Coverage) consume this
/// table; the Phase-2.5 <see cref="SearchProfileEdit"/> table is the closest
/// analog.
/// </summary>
/// <remarks>
/// Two ingestion paths feed this table:
/// <list type="bullet">
///   <item><c>Source = "host-sdk"</c> — the host site POSTs to
///   <c>/api/telemetry/searchlog</c> after each public-search hit. Preferred,
///   because only the host knows which result the visitor clicked (CTR).</item>
///   <item><c>Source = "graph-poll"</c> — fallback poller that scrapes Graph's
///   request-log API. No click data, so <see cref="TopResultRank"/> will be
///   <c>null</c> for these rows.</item>
/// </list>
/// Phrase / Locale / Site / ProfileKey are indexed because the Phase 4 UIs
/// always slice by at least one of them; rank/duration/etc. are read-only
/// columns that come along for the ride.
/// </remarks>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_SearchLog")]
public class SearchLogEntry : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>UTC timestamp when the search ran. Server-stamped on ingest.</summary>
    [EPiServerDataIndex]
    public DateTime At { get; set; }

    /// <summary>Raw phrase the visitor typed; empty string when unknown.</summary>
    [EPiServerDataIndex]
    public string Phrase { get; set; } = string.Empty;

    /// <summary>BCP-47 locale, lower-case; empty string means "unknown".</summary>
    [EPiServerDataIndex]
    public string Locale { get; set; } = string.Empty;

    /// <summary><c>SiteDefinition.Name</c>; empty string means "unknown".</summary>
    [EPiServerDataIndex]
    public string Site { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="Configuration.SearchProfile.Key"/> when the host SDK can
    /// attribute the hit to a registered profile; empty string means
    /// "unattributed" (Graph poll path can't classify).
    /// </summary>
    [EPiServerDataIndex]
    public string ProfileKey { get; set; } = string.Empty;

    /// <summary>Number of hits returned. <c>-1</c> when unknown.</summary>
    public int ResultCount { get; set; }

    /// <summary>
    /// 1-based rank of the result the visitor clicked, or <c>null</c> when no
    /// click was recorded. CTR aggregations treat <c>null</c> as "no click".
    /// </summary>
    public int? TopResultRank { get; set; }

    /// <summary><c>ContentLink.Id</c> or external id of the clicked result; empty when none.</summary>
    public string TopResultId { get; set; } = string.Empty;

    /// <summary>Wall-clock latency the host observed for the query, milliseconds.</summary>
    public int DurationMs { get; set; }

    /// <summary><c>"Relevance"</c>, <c>"Semantic"</c>, etc. Empty when unknown.</summary>
    public string Ranking { get; set; } = string.Empty;

    /// <summary>Visitor session id (host-supplied). Empty when not available.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary><c>"desktop"</c>, <c>"mobile"</c>, <c>"bot"</c>, or empty when unknown.</summary>
    public string UserAgentClass { get; set; } = string.Empty;

    /// <summary>Which ingestion path produced the row: <c>"host-sdk"</c> or <c>"graph-poll"</c>.</summary>
    [EPiServerDataIndex]
    public string Source { get; set; } = string.Empty;
}
