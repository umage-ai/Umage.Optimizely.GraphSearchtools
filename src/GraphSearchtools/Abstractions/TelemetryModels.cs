namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// What an analytics UI asks the reader for: a window, an optional filter pair,
/// and a row cap. Times are UTC; an unset <see cref="ProfileKey"/> /
/// <see cref="Locale"/> means "all".
/// </summary>
public sealed record TelemetryQuery(
    DateTime SinceUtc,
    DateTime UntilUtc,
    int Take,
    string? ProfileKey = null,
    string? Locale = null);

/// <summary>
/// Per-(phrase, profile, locale) roll-up returned by the reader. Counts cover
/// the full <see cref="TelemetryQuery"/> window. Hits and click counts are
/// summed across instances; ratios are recomputed at the cluster level.
/// </summary>
public sealed record PhraseAggregate(
    string Phrase,
    int Hits,
    double ZeroResultRate,
    double Ctr,
    string Locale,
    string ProfileKey);

/// <summary>
/// One raw search or click as recorded in the per-instance forensic ring.
/// Cross-instance forensics is a non-goal; readers may return rows from a
/// single node only.
/// </summary>
public sealed record RawEvent(
    DateTime TimestampUtc,
    string Kind,
    string Phrase,
    string ProfileKey,
    string Locale,
    int? ResultCount,
    int? ClickRank,
    string NodeId);

/// <summary>
/// Search-side event the host SDK posts to <c>/api/telemetry/searchlog</c>
/// with <c>kind = "search"</c>. Mirrored by <see cref="ClickEvent"/> for clicks.
/// </summary>
public sealed record SearchEvent(
    string Phrase,
    string ProfileKey,
    string Locale,
    int ResultCount,
    DateTime TimestampUtc);

/// <summary>
/// Click-side event the host SDK posts to <c>/api/telemetry/searchlog</c>
/// with <c>kind = "click"</c>. <see cref="OriginalBucketUtc"/> is the
/// minute-truncated timestamp of the originating search, used by the flusher
/// to attribute clicks to their bucket even when they arrive after the bucket
/// has rolled over. When unset, the flusher folds into the click's own minute
/// as a best-effort fallback (per design §5).
/// </summary>
public sealed record ClickEvent(
    string Phrase,
    string ProfileKey,
    string Locale,
    int Rank,
    DateTime TimestampUtc,
    DateTime? OriginalBucketUtc);
