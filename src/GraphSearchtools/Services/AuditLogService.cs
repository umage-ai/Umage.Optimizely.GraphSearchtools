using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Services;

/// <summary>
/// Append-and-read access to the <see cref="AuditLogEntry"/> DDS table.
/// Pinned / Synonyms controllers write here on every Graph mutation; the
/// Channels index reads the latest entry per channel for its "Last edited"
/// hint and the top-level Pinned / Synonyms tools read recent slices for
/// their changelog tabs.
/// </summary>
/// <remarks>
/// DDS is process-global, so this is registered as a singleton. Methods
/// degrade to no-op / empty when the store isn't available (e.g. outside an
/// Optimizely host) rather than throwing.
/// </remarks>
internal class AuditLogService
{
    /// <summary>Append a new audit row. Populates <see cref="AuditLogEntry.At"/> if unset.</summary>
    public virtual void Append(AuditLogEntry entry)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (entry.At == default) entry.At = DateTime.UtcNow;

        var store = TryGetStore();
        if (store == null) return;
        store.Save(entry);
    }

    /// <summary>
    /// Append many entries in one call. Used by the synonym diff path —
    /// a single PUT typically produces N add/remove rows. Each entry is
    /// saved independently so a partial failure doesn't lose the rest.
    /// </summary>
    public virtual void AppendMany(IEnumerable<AuditLogEntry> entries)
    {
        if (entries == null) return;
        var store = TryGetStore();
        if (store == null) return;
        foreach (var e in entries)
        {
            if (e.At == default) e.At = DateTime.UtcNow;
            try { store.Save(e); }
            catch { /* one bad row mustn't break the rest */ }
        }
    }

    /// <summary>
    /// Newest-first slice of the log, optionally filtered by kind. When
    /// <paramref name="kinds"/> is null or empty, all kinds are returned.
    /// </summary>
    public virtual IReadOnlyList<AuditLogEntry> ListRecent(int take = 200, IReadOnlyCollection<string>? kinds = null)
    {
        var store = TryGetStore();
        if (store == null) return Array.Empty<AuditLogEntry>();

        var clamped = Math.Clamp(take, 1, 1000);
        var q = store.Items<AuditLogEntry>().AsQueryable();
        if (kinds != null && kinds.Count > 0)
        {
            var allowed = new HashSet<string>(kinds, StringComparer.OrdinalIgnoreCase);
            // DDS LINQ doesn't support .Contains over a HashSet — materialise
            // before filtering. The store is small (audit log), so this is
            // cheap; the take-clamp protects the payload size.
            return store.Items<AuditLogEntry>()
                .OrderByDescending(e => e.At)
                .AsEnumerable()
                .Where(e => allowed.Contains(e.Kind))
                .Take(clamped)
                .ToList();
        }
        return q.OrderByDescending(e => e.At).Take(clamped).ToList();
    }

    /// <summary>
    /// Latest entry for a channel, or <c>null</c>. Drives the Channels index
    /// "Last edited" hint. Looks across all kinds — a collection edit through
    /// a channel-scoped flyout still counts as that channel's last touch.
    /// </summary>
    public virtual AuditLogEntry? LatestForChannel(string channelKey)
    {
        if (string.IsNullOrEmpty(channelKey)) return null;
        var store = TryGetStore();
        if (store == null) return null;
        return store.Items<AuditLogEntry>()
            .Where(e => e.ChannelKey == channelKey)
            .OrderByDescending(e => e.At)
            .FirstOrDefault();
    }

    private static DynamicDataStore? TryGetStore()
    {
        try
        {
            return DynamicDataStoreFactory.Instance?.GetStore(typeof(AuditLogEntry))
                ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(AuditLogEntry));
        }
        catch
        {
            return null;
        }
    }
}
