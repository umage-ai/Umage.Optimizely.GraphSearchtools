namespace UmageAI.Optimizely.GraphSearchTools.Abstractions;

/// <summary>
/// Hot-path write seam for search/click events. The contract is non-blocking
/// and total: implementations MUST NOT throw, MUST NOT await, and MUST NOT
/// stall the caller — under overload they drop, never block. The HTTP handler
/// returns 204 immediately after Record, so storage failures are invisible to
/// the host page.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>
    /// Records a search event for later aggregation into a (minute, phrase,
    /// channel, locale) bucket.
    /// </summary>
    void Record(in SearchEvent searchEvent);

    /// <summary>
    /// Records a click event. Attribution to the originating search bucket
    /// uses <see cref="ClickEvent.OriginalBucketUtc"/>; when absent, the
    /// flusher folds into the click's own minute as a best-effort fallback.
    /// </summary>
    void Record(in ClickEvent clickEvent);
}
