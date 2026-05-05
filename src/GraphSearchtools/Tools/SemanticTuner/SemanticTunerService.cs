using System.Text.Json;
using EPiServer.Data.Dynamic;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner.Models;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SemanticTuner;

/// <summary>
/// DDS-backed persistence for the Semantic Weight Tuner policy. Single-row
/// store keyed by <see cref="SemanticTuningPolicyRecord.DefaultKey"/> — v1 has
/// no per-tenant variation.
/// </summary>
/// <remarks>
/// Mirrors the tolerant pattern used by <c>SearchProfileEditService</c>: when
/// DDS isn't reachable (e.g. unit tests outside a host) read methods return
/// the bound-config fallback (or empty) and writes silently no-op. The
/// in-process consumption helper <see cref="TierForTokenCount"/> is used by
/// future query helpers — wire-up is a follow-up per the implementation
/// plan §5.
/// </remarks>
public class SemanticTunerService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly IOptions<GraphSearchtoolsOptions> _options;

    public SemanticTunerService(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options;
    }

    /// <summary>
    /// Returns the saved policy. Falls back to the bound configuration
    /// (<see cref="SemanticTuningOptions.Tiers"/>) when DDS has no row yet,
    /// and to an empty policy when neither surface is populated.
    /// </summary>
    public virtual SemanticTuningPolicy GetPolicy()
    {
        var record = TryRead();
        if (record != null && !string.IsNullOrWhiteSpace(record.TiersJson))
        {
            try
            {
                var tiers = JsonSerializer.Deserialize<List<SemanticTier>>(record.TiersJson, SerializerOptions);
                return new SemanticTuningPolicy { Tiers = tiers ?? new List<SemanticTier>() };
            }
            catch
            {
                // Corrupt row — fall through to the config-bound default so the
                // editor isn't locked out of the page.
            }
        }

        var configured = _options.Value?.SemanticTuning?.Tiers;
        return new SemanticTuningPolicy
        {
            Tiers = configured != null && configured.Count > 0
                ? new List<SemanticTier>(configured)
                : new List<SemanticTier>()
        };
    }

    /// <summary>
    /// Persists the policy. Throws <see cref="ArgumentException"/> on
    /// validation failure — call <see cref="Validate"/> first to surface a
    /// 400 to the caller rather than letting the exception escape.
    /// </summary>
    public virtual void SavePolicy(SemanticTuningPolicy policy, string? actorName = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var error = Validate(policy);
        if (error != null) throw new ArgumentException(error, nameof(policy));

        var store = TryGetStore();
        if (store == null) return; // tolerant to host-less environments

        var existing = store.Items<SemanticTuningPolicyRecord>()
            .FirstOrDefault(r => r.Key == SemanticTuningPolicyRecord.DefaultKey)
            ?? new SemanticTuningPolicyRecord { Key = SemanticTuningPolicyRecord.DefaultKey };

        existing.TiersJson = JsonSerializer.Serialize(policy.Tiers ?? new List<SemanticTier>(), SerializerOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(actorName)) existing.UpdatedBy = actorName!;

        store.Save(existing);
    }

    /// <summary>
    /// Validates a policy before save. Returns <c>null</c> when the policy is
    /// well-formed, otherwise a human-readable error suitable for a 400
    /// response body.
    /// </summary>
    /// <remarks>
    /// Decision: tier ranges must NOT overlap (a token count maps to exactly
    /// one tier). At most one tier may be open-ended (<c>MaxTokens == null</c>),
    /// and if present that tier must come last when sorted by
    /// <c>MinTokens</c>. The semantic weight is clamped to <c>[0, 1]</c>.
    /// </remarks>
    public static string? Validate(SemanticTuningPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var tiers = policy.Tiers ?? new List<SemanticTier>();
        if (tiers.Count == 0) return null; // empty policy is valid (no-op fallback)

        // Per-tier sanity.
        foreach (var t in tiers)
        {
            if (t.MinTokens < 0)
                return $"MinTokens must be >= 0 (got {t.MinTokens}).";
            if (t.MaxTokens.HasValue && t.MaxTokens.Value < t.MinTokens)
                return $"MaxTokens ({t.MaxTokens}) must be >= MinTokens ({t.MinTokens}).";
            if (t.SemanticWeight < 0 || t.SemanticWeight > 1)
                return $"SemanticWeight must be within [0, 1] (got {t.SemanticWeight}).";
        }

        // Range-overlap detection. We sort by MinTokens, then walk: each tier
        // must start strictly after the previous tier's MaxTokens. An
        // open-ended tier (MaxTokens == null) is only valid as the last entry.
        var sorted = tiers.OrderBy(t => t.MinTokens).ToList();
        for (var i = 0; i < sorted.Count; i++)
        {
            var current = sorted[i];
            if (i < sorted.Count - 1)
            {
                if (!current.MaxTokens.HasValue)
                    return "Open-ended tier (MaxTokens unset) must be the last tier.";
                var next = sorted[i + 1];
                if (next.MinTokens <= current.MaxTokens.Value)
                    return $"Overlapping tiers: [{current.MinTokens}..{current.MaxTokens}] and [{next.MinTokens}..{next.MaxTokens?.ToString() ?? "∞"}].";
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the tier matching a given query token count. Returns <c>null</c>
    /// when no tier covers it (the caller should fall back to the
    /// platform-default ranking).
    /// </summary>
    public virtual SemanticTier? TierForTokenCount(int tokenCount)
    {
        if (tokenCount < 0) return null;
        var policy = GetPolicy();
        return TierForTokenCount(policy, tokenCount);
    }

    /// <summary>
    /// Pure-function variant that doesn't read DDS — useful in unit tests and
    /// for callers that already hold a policy snapshot.
    /// </summary>
    public static SemanticTier? TierForTokenCount(SemanticTuningPolicy policy, int tokenCount)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (tokenCount < 0) return null;
        // Iterate sorted-by-MinTokens so the first matching range wins
        // deterministically when validation has been bypassed.
        foreach (var t in (policy.Tiers ?? new List<SemanticTier>()).OrderBy(t => t.MinTokens))
        {
            if (tokenCount < t.MinTokens) continue;
            if (!t.MaxTokens.HasValue || tokenCount <= t.MaxTokens.Value) return t;
        }
        return null;
    }

    private static SemanticTuningPolicyRecord? TryRead()
    {
        var store = TryGetStore();
        if (store == null) return null;
        try
        {
            return store.Items<SemanticTuningPolicyRecord>()
                .FirstOrDefault(r => r.Key == SemanticTuningPolicyRecord.DefaultKey);
        }
        catch
        {
            return null;
        }
    }

    private static DynamicDataStore? TryGetStore()
    {
        try
        {
            return DynamicDataStoreFactory.Instance?.GetStore(typeof(SemanticTuningPolicyRecord))
                ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(SemanticTuningPolicyRecord));
        }
        catch
        {
            return null;
        }
    }
}
