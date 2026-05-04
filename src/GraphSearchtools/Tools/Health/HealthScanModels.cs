using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Health;

/// <summary>
/// Singleton settings + rotation state for the background health scan job.
/// One row per addon installation; the service treats the first row found as
/// canonical and creates a default if missing.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_HealthSettings")]
public class HealthSettingsRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    /// <summary>When true, the scheduled job actually runs probes + content checks.
    /// When false, it returns immediately. Toggled from the Health page Auto switch.</summary>
    public bool ScanEnabled { get; set; }

    /// <summary>The last <c>ContentLink.ID</c> we processed in the rotation. The
    /// next scan picks items with ID greater than this; when no more items
    /// remain, the cursor wraps to 0.</summary>
    public int RotationCursor { get; set; }

    public DateTime? LastScanAt { get; set; }
}

/// <summary>
/// One row per scan — the timeline chart reads these (oldest 12h trimmed on write).
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_HealthScans")]
public class HealthScanRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex]
    public DateTime RanAt { get; set; }

    public long ElapsedMs { get; set; }
    public int ProbesPassed { get; set; }
    public int ProbesTotal { get; set; }
    public int ContentChecked { get; set; }
    public int ContentMissing { get; set; }

    /// <summary>"green" | "amber" | "red". Drives the color of the timeline bar.
    /// Red when at least one probe failed, amber when probes passed but
    /// content was missing, green otherwise.</summary>
    public string OverallStatus { get; set; } = "green";

    /// <summary>Optional message rendered on hover/expansion. Kept short.</summary>
    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// One row per (content) issue discovered. Issues stay open until the same
/// item is verified in Graph on a later scan or has been removed from the CMS
/// repo. Resolved rows are kept around briefly so editors can see what
/// recently came back online.
/// </summary>
[EPiServerDataStore(AutomaticallyCreateStore = true, AutomaticallyRemapStore = true, StoreName = "GraphSearchtools_HealthIssues")]
public class HealthIssueRecord : IDynamicData
{
    public Identity Id { get; set; } = Identity.NewIdentity();

    [EPiServerDataIndex]
    public string ContentGuid { get; set; } = string.Empty;

    public int ContentId { get; set; }
    public string ContentName { get; set; } = string.Empty;
    public string ContentTypeName { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>Null while the issue is open. Set once the item appears in
    /// Graph again or is removed from the CMS repo.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>"missing-from-graph" | "removed-from-cms". The latter is a
    /// terminal status (the issue is auto-resolved by the next scan that
    /// notices the content is gone).</summary>
    public string Reason { get; set; } = "missing-from-graph";
}

// ── DTOs the API serializes ────────────────────────────────────────

public sealed record HealthScanDto(
    string Id,
    DateTime RanAt,
    long ElapsedMs,
    int ProbesPassed,
    int ProbesTotal,
    int ContentChecked,
    int ContentMissing,
    string OverallStatus,
    string Note);

public sealed record HealthIssueDto(
    string Id,
    string ContentGuid,
    int ContentId,
    string ContentName,
    string ContentTypeName,
    string Locale,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    DateTime? ResolvedAt,
    string Reason);

public sealed record HealthSettingsDto(
    bool ScanEnabled,
    DateTime? LastScanAt);

public sealed record HealthHistoryDto(
    HealthSettingsDto Settings,
    IReadOnlyList<HealthScanDto> Scans,
    IReadOnlyList<HealthIssueDto> OpenIssues,
    IReadOnlyList<HealthIssueDto> RecentlyResolved,
    int TotalContent);
