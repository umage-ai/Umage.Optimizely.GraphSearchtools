using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// Append-and-read access to the <see cref="SearchProfileEdit"/> DDS table.
/// Used by Pinned / Synonyms controllers to log changes; used by the Profiles
/// index and detail UI to display "last edited" hints and the audit log.
/// </summary>
/// <remarks>
/// DDS is process-global (singleton factory), so this service is registered as
/// a singleton too. Methods are tolerant of the store not being available (e.g.
/// when running outside an Optimizely host) — they degrade to no-op / empty
/// rather than throwing.
/// </remarks>
public class SearchProfileEditService
{
    /// <summary>Append a new audit row.</summary>
    public virtual void Append(SearchProfileEdit entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (entry.At == default) entry.At = DateTime.UtcNow;

        var store = TryGetStore();
        if (store == null) return;
        store.Save(entry);
    }

    /// <summary>
    /// List recent edits for a profile, newest-first. <paramref name="take"/> is
    /// clamped to <c>[1, 1000]</c>; default is 100 (the Audit log tab paginates
    /// at this size).
    /// </summary>
    public virtual IEnumerable<SearchProfileEdit> ListForProfile(string profileKey, int take = 100)
    {
        if (string.IsNullOrEmpty(profileKey)) return Array.Empty<SearchProfileEdit>();

        var store = TryGetStore();
        if (store == null) return Array.Empty<SearchProfileEdit>();

        var clamped = Math.Clamp(take, 1, 1000);
        return store.Items<SearchProfileEdit>()
            .Where(e => e.ProfileKey == profileKey)
            .OrderByDescending(e => e.At)
            .Take(clamped)
            .ToList();
    }

    /// <summary>
    /// List the most recent edits across every profile, newest-first. Backs
    /// the Insights "activity strip" panel. <paramref name="take"/> is clamped
    /// to <c>[1, 200]</c> so the dashboard can't accidentally pull the whole
    /// audit log.
    /// </summary>
    public virtual IEnumerable<SearchProfileEdit> ListRecent(int take = 20)
    {
        var store = TryGetStore();
        if (store == null) return Array.Empty<SearchProfileEdit>();

        var clamped = Math.Clamp(take, 1, 200);
        return store.Items<SearchProfileEdit>()
            .OrderByDescending(e => e.At)
            .Take(clamped)
            .ToList();
    }

    /// <summary>
    /// Most recent edit for a profile, or <c>null</c> when none exists. Drives
    /// the Profiles index "Last edited" column.
    /// </summary>
    public virtual SearchProfileEdit? LatestForProfile(string profileKey)
    {
        if (string.IsNullOrEmpty(profileKey)) return null;

        var store = TryGetStore();
        if (store == null) return null;

        return store.Items<SearchProfileEdit>()
            .Where(e => e.ProfileKey == profileKey)
            .OrderByDescending(e => e.At)
            .FirstOrDefault();
    }

    private static DynamicDataStore? TryGetStore()
    {
        try
        {
            return DynamicDataStoreFactory.Instance?.GetStore(typeof(SearchProfileEdit))
                ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(SearchProfileEdit));
        }
        catch
        {
            return null;
        }
    }
}
