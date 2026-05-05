// STUB: belongs to foundation agent — to be replaced at integration.
//
// In-memory placeholder for the audit-log + last-edited-summary store
// described in docs/search-profiles-design.md §3.2. The real version is
// DDS-backed and writes a row per pinned/synonym edit. This stub returns
// empty enumerables so the Audit tab + last-edited column can render
// against it before the foundation agent's table lands.

namespace UmageAI.Optimizely.GraphSearchTools.Services;

public sealed class SearchProfileEditService
{
    /// <summary>Newest-first list of edits for a profile, capped at <paramref name="take"/>.</summary>
    public IEnumerable<SearchProfileEdit> ListForProfile(string profileKey, int take)
        => Array.Empty<SearchProfileEdit>();

    /// <summary>Most recent edit for the profile, or null if none.</summary>
    public SearchProfileEdit? LatestForProfile(string profileKey) => null;
}

/// <summary>
/// Single audit-log entry. Field set per design §3.2 — kept as a POCO with
/// public setters so the foundation agent's DDS POCO can target the same
/// shape without ceremony.
/// </summary>
public sealed class SearchProfileEdit
{
    public string ProfileKey { get; set; } = string.Empty;
    public string? Site { get; set; }
    public string? Locale { get; set; }
    /// <summary>"pinned" | "synonym" — kept open as string for forward-compat.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>"create" | "update" | "delete" | "reorder".</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Phrase, slot, or other short identifier for the affected entity.</summary>
    public string? Subject { get; set; }
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}
