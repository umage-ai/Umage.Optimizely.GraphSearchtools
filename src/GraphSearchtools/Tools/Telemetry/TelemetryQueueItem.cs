namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// What goes into the channel. A flat readonly struct so <c>TryWrite</c> is a
/// struct-copy + a single internal slot bump — no per-event allocation beyond
/// the strings the controller already deserialised.
/// </summary>
internal readonly struct TelemetryQueueItem
{
    public readonly TelemetryQueueItemKind Kind;
    public readonly DateTime TimestampUtc;
    public readonly string Phrase;
    public readonly string ProfileKey;
    public readonly string Locale;

    /// <summary>Set on search events; <c>0</c> on click events.</summary>
    public readonly int ResultCount;

    /// <summary>1-based click rank. Set on click events; <c>0</c> on search events.</summary>
    public readonly int ClickRank;

    /// <summary>Click bucket attribution. Set only on click events; <c>null</c> means best-effort current-minute fallback.</summary>
    public readonly DateTime? OriginalBucketUtc;

    private TelemetryQueueItem(
        TelemetryQueueItemKind kind,
        DateTime timestampUtc,
        string phrase,
        string profileKey,
        string locale,
        int resultCount,
        int clickRank,
        DateTime? originalBucketUtc)
    {
        Kind = kind;
        TimestampUtc = timestampUtc;
        Phrase = phrase;
        ProfileKey = profileKey;
        Locale = locale;
        ResultCount = resultCount;
        ClickRank = clickRank;
        OriginalBucketUtc = originalBucketUtc;
    }

    public static TelemetryQueueItem ForSearch(string phrase, string profileKey, string locale, int resultCount, DateTime timestampUtc)
        => new(TelemetryQueueItemKind.Search, timestampUtc, phrase, profileKey, locale, resultCount, 0, null);

    public static TelemetryQueueItem ForClick(string phrase, string profileKey, string locale, int rank, DateTime timestampUtc, DateTime? originalBucketUtc)
        => new(TelemetryQueueItemKind.Click, timestampUtc, phrase, profileKey, locale, 0, rank, originalBucketUtc);
}

internal enum TelemetryQueueItemKind : byte
{
    Search,
    Click
}
