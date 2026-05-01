using EPiServer.Data;
using EPiServer.Data.Dynamic;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

/// <summary>
/// CRUD over <see cref="SavedQueryRecord"/> via DDS. Single store, no per-user
/// scoping in v1 — saved queries are shared across editors.
/// </summary>
public sealed class SavedQueriesService
{
    public IReadOnlyList<SavedQueryDto> List()
    {
        var store = GetStore();
        return store.Items<SavedQueryRecord>()
            .OrderBy(r => r.Name)
            .ToList()
            .Select(Map)
            .ToList();
    }

    public SavedQueryDto? Get(string id)
    {
        if (!TryParseIdentity(id, out var identity)) return null;
        var store = GetStore();
        var record = store.Load<SavedQueryRecord>(identity);
        return record == null ? null : Map(record);
    }

    public SavedQueryDto Create(SavedQueryPayload payload)
    {
        var store = GetStore();
        var record = new SavedQueryRecord
        {
            Name = payload.Name?.Trim() ?? string.Empty,
            Description = payload.Description?.Trim() ?? string.Empty,
            Query = payload.Query?.Trim() ?? string.Empty,
            Locale = string.IsNullOrWhiteSpace(payload.Locale) ? null : payload.Locale,
            Ranking = string.IsNullOrWhiteSpace(payload.Ranking) ? "RELEVANCE" : payload.Ranking,
            SemanticWeight = Math.Clamp(payload.SemanticWeight, -1.0, 1.0),
            MinimumScore = payload.MinimumScore,
            Limit = Math.Clamp(payload.Limit, 1, 100),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        store.Save(record);
        return Map(record);
    }

    public SavedQueryDto? Update(string id, SavedQueryPayload payload)
    {
        if (!TryParseIdentity(id, out var identity)) return null;
        var store = GetStore();
        var existing = store.Load<SavedQueryRecord>(identity);
        if (existing == null) return null;

        existing.Name = payload.Name?.Trim() ?? existing.Name;
        existing.Description = payload.Description?.Trim() ?? existing.Description;
        existing.Query = payload.Query?.Trim() ?? existing.Query;
        existing.Locale = string.IsNullOrWhiteSpace(payload.Locale) ? null : payload.Locale;
        existing.Ranking = string.IsNullOrWhiteSpace(payload.Ranking) ? "RELEVANCE" : payload.Ranking;
        existing.SemanticWeight = Math.Clamp(payload.SemanticWeight, -1.0, 1.0);
        existing.MinimumScore = payload.MinimumScore;
        existing.Limit = Math.Clamp(payload.Limit, 1, 100);
        existing.UpdatedAt = DateTime.UtcNow;
        store.Save(existing);
        return Map(existing);
    }

    public bool Delete(string id)
    {
        if (!TryParseIdentity(id, out var identity)) return false;
        var store = GetStore();
        var existing = store.Load<SavedQueryRecord>(identity);
        if (existing == null) return false;
        store.Delete(identity);
        return true;
    }

    private static SavedQueryDto Map(SavedQueryRecord r) =>
        new(r.Id.ToString(), r.Name, r.Description, r.Query, r.Locale, r.Ranking,
            r.SemanticWeight, r.MinimumScore, r.Limit, r.CreatedAt, r.UpdatedAt);

    private static bool TryParseIdentity(string id, out Identity identity)
    {
        identity = Identity.NewIdentity();
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { identity = Identity.Parse(id); return true; }
        catch (FormatException) { return false; }
    }

    private static DynamicDataStore GetStore()
        => DynamicDataStoreFactory.Instance.CreateStore(typeof(SavedQueryRecord));
}
