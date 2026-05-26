using System.Text.Json.Serialization;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.PinnedCoverage.Models;

/// <summary>
/// Outcome of one Pinned Result Coverage audit pass. The result is a snapshot
/// — the service composes it on every <c>GET /PinnedCoverageApi/Audit</c> from
/// live Graph data, content-loader lookups and 7-day search-log aggregation,
/// so there is no DDS table behind it.
/// </summary>
internal sealed record PinnedCoverageResult
{
    /// <summary>UTC moment when the audit was composed; surfaced as "Generated 30s ago".</summary>
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; init; }

    /// <summary>Per-pin issues — unpublished/deleted/expired/low-CTR/no-activity rows.</summary>
    [JsonPropertyName("issues")]
    public List<PinnedIssue> Issues { get; init; } = new();

    /// <summary>
    /// Phrases pinned in two or more collections. Per phase 4 §6, this is the
    /// "pin overlap heatmap" — the same phrase reaching different content
    /// across collections is the most common cause of non-deterministic
    /// pinning behaviour.
    /// </summary>
    [JsonPropertyName("overlaps")]
    public List<PinnedOverlap> Overlaps { get; init; } = new();
}

/// <summary>
/// One row in the issues table. <see cref="Kind"/> is a literal string rather
/// than an enum so the JS can switch on it without a parsing layer; the values
/// are stable contracts the view's badge logic depends on.
/// </summary>
internal sealed record PinnedIssue
{
    /// <summary>
    /// One of <c>"Unpublished"</c>, <c>"Deleted"</c>, <c>"Expired"</c>,
    /// <c>"LowCtr"</c>, <c>"NoActivity"</c>. Lower-case-first variants are
    /// not produced; the JS compares with case-sensitive equality.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>Graph collection key (e.g. <c>corp-en</c>) the pin belongs to.</summary>
    [JsonPropertyName("collectionKey")]
    public string CollectionKey { get; init; } = string.Empty;

    /// <summary>
    /// Search channel this collection maps to, or <c>null</c> when the
    /// collection key doesn't match any registered channel (Generic catchment
    /// or legacy pin). Used to deep-link "Fix in channel" to the right
    /// detail page.
    /// </summary>
    [JsonPropertyName("channelKey")]
    public string? ChannelKey { get; init; }

    /// <summary>The phrase(s) the pin matches; verbatim from the Graph item.</summary>
    [JsonPropertyName("phrase")]
    public string Phrase { get; init; } = string.Empty;

    /// <summary>The pin target — typically a content GUID or external id.</summary>
    [JsonPropertyName("targetId")]
    public string TargetId { get; init; } = string.Empty;

    /// <summary>Resolved display name of the target content; null when unresolvable.</summary>
    [JsonPropertyName("targetName")]
    public string? TargetName { get; init; }

    /// <summary>
    /// Free-form one-line explanation for the row (e.g. "Expired 3 days ago",
    /// "0 / 38 sessions clicked through"). The audit composes this server-side
    /// so the view doesn't need to re-encode the policy.
    /// </summary>
    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// One overlap row — a phrase pinned in two-or-more collections. The
/// collections list carries the raw collection keys (not the localized
/// channel names) because some collections aren't channel-bound and we want
/// the row to be diagnostic even then.
/// </summary>
internal sealed record PinnedOverlap
{
    /// <summary>Phrase that's pinned across multiple collections.</summary>
    [JsonPropertyName("phrase")]
    public string Phrase { get; init; } = string.Empty;

    /// <summary>Collection keys where the phrase appears. At least 2 entries.</summary>
    [JsonPropertyName("collections")]
    public List<string> Collections { get; init; } = new();
}
