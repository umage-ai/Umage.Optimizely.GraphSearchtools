namespace UmageAI.Optimizely.GraphSearchTools.Tools.ContentSearchabilityAudit.Models;

/// <summary>
/// Phase 4 — Content Searchability Audit. The result wire shape is intentionally
/// flat so the page JS can group/filter rows client-side without re-shaping
/// data per category. Issue lists are capped at <see cref="ItemCap"/> per kind
/// so a catastrophic content tree (every page missing Tags, every page over
/// the 1024-char sortable limit) doesn't ship a multi-megabyte JSON to the
/// browser.
/// </summary>
public sealed class AuditResult
{
    /// <summary>Cap per <see cref="AuditIssueKind"/> inside <see cref="Issues"/>.</summary>
    public const int ItemCap = 200;

    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Total <c>IContent</c> items walked (all kinds / locales).</summary>
    public int ItemsScanned { get; init; }

    /// <summary>Wall-clock duration of the scan, milliseconds.</summary>
    public long ElapsedMs { get; init; }

    /// <summary>
    /// True when at least one category hit <see cref="ItemCap"/>. UI surfaces
    /// the "showing first N — fix these and rescan" hint when so.
    /// </summary>
    public bool Truncated { get; init; }

    public IReadOnlyList<AuditIssue> Issues { get; init; } = Array.Empty<AuditIssue>();
}

/// <summary>
/// One per-(content, kind) violation. Detail is human-readable (e.g.
/// "Heading is 1843 chars (limit 1024)") so the UI can render it verbatim
/// without per-kind formatting branches.
/// </summary>
public sealed class AuditIssue
{
    /// <summary>One of <see cref="AuditIssueKinds"/> — string-typed on the
    /// wire for forward compatibility with future categories.</summary>
    public string Kind { get; init; } = string.Empty;

    public int ContentLink { get; init; }
    public string ContentGuid { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Locale { get; init; } = string.Empty;

    /// <summary>Free-text describing the specific violation.</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>
/// Wire-stable issue kind values. Kept as constants (not enum) so the JSON
/// payload stays a string and the JS can use them as object keys without a
/// reverse-lookup table.
/// </summary>
public static class AuditIssueKinds
{
    public const string MissingName = "MissingName";
    public const string MissingMainBody = "MissingMainBody";
    public const string NoTags = "NoTags";
    public const string OversizeSortField = "OversizeSortField";
}
