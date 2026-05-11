using System.Globalization;
using System.Text;
using System.Text.Json;
using EPiServer.Data.Dynamic;
using Microsoft.Extensions.DependencyInjection;
using UmageAI.Optimizely.GraphSearchTools.Configuration;
using UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.SavedQueries;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.RelevancyLab;

/// <summary>
/// DDS-backed orchestration for the Relevancy Lab. Three responsibilities:
/// <list type="bullet">
///   <item>CRUD for golden query sets (<see cref="GoldenSetRecord"/>).</item>
///   <item>Run engine — for each phrase, call <see cref="QueryRunnerService"/>
///         and score the result with NDCG@10 + MRR.</item>
///   <item>History — persist runs (<see cref="RunRecord"/>) so two runs can be
///         compared side-by-side.</item>
/// </list>
/// </summary>
/// <remarks>
/// Mirrors the tolerant DDS pattern used by <c>SearchProfileEditService</c>:
/// when DDS isn't reachable (running outside an Optimizely host, e.g. in unit
/// tests) reads return empty / null and writes silently no-op. Singleton
/// because <c>DynamicDataStoreFactory</c> is process-global.
/// </remarks>
public class RelevancyLabService
{
    private const int RunFetchLimit = 25;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        WriteIndented = false
    };

    // Pure-function scorer is testable without DDS or HTTP — see RelevancyLabScoringTests.
    // Run dispatch resolves QueryRunnerService through the scope factory because
    // the runner is HttpClient-bound (transient) and this service is a singleton.
    // The factory hook is also what controller tests mock to avoid standing up
    // the runner.
    private readonly IServiceScopeFactory? _scopeFactory;

    public RelevancyLabService() : this(null) { }

    public RelevancyLabService(IServiceScopeFactory? scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    // ──────────────────────────────────────────────────────────────────
    //   Golden-set CRUD
    // ──────────────────────────────────────────────────────────────────

    public virtual IReadOnlyList<GoldenSet> ListSets()
    {
        var store = TryGetStore<GoldenSetRecord>();
        if (store == null) return Array.Empty<GoldenSet>();
        try
        {
            return store.Items<GoldenSetRecord>()
                .OrderByDescending(r => r.UpdatedAt)
                .ToList()
                .Select(MapSet)
                .ToList();
        }
        catch
        {
            return Array.Empty<GoldenSet>();
        }
    }

    public virtual GoldenSet? GetSet(Guid id)
    {
        var store = TryGetStore<GoldenSetRecord>();
        if (store == null) return null;
        var record = store.Items<GoldenSetRecord>().FirstOrDefault(r => r.SetId == id);
        return record == null ? null : MapSet(record);
    }

    /// <summary>Create or update a golden set. Caller-provided
    /// <see cref="GoldenSet.Id"/> wins; an empty <see cref="Guid"/> means create.
    /// Returns the persisted snapshot (with timestamps and Id resolved).</summary>
    public virtual GoldenSet UpsertSet(GoldenSet input, string? actorName = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var error = ValidateSet(input);
        if (error != null) throw new ArgumentException(error, nameof(input));

        var now = DateTime.UtcNow;
        var store = TryGetStore<GoldenSetRecord>();
        if (store == null)
        {
            // Tolerant fallback — return what we'd save without persisting.
            return new GoldenSet
            {
                Id = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id,
                Name = input.Name.Trim(),
                Description = input.Description?.Trim(),
                Locale = (input.Locale ?? string.Empty).Trim().ToLowerInvariant(),
                CreatedAt = now,
                UpdatedAt = now,
                Items = input.Items ?? new()
            };
        }

        GoldenSetRecord? existing = null;
        if (input.Id != Guid.Empty)
        {
            existing = store.Items<GoldenSetRecord>().FirstOrDefault(r => r.SetId == input.Id);
        }

        var record = existing ?? new GoldenSetRecord
        {
            SetId = input.Id == Guid.Empty ? Guid.NewGuid() : input.Id,
            CreatedAt = now
        };

        record.Name = input.Name.Trim();
        record.Description = input.Description?.Trim() ?? string.Empty;
        record.Locale = (input.Locale ?? string.Empty).Trim().ToLowerInvariant();
        record.UpdatedAt = now;
        record.ItemsJson = JsonSerializer.Serialize(input.Items ?? new List<GoldenItem>(), SerializerOptions);

        store.Save(record);
        return MapSet(record);
    }

    public virtual bool DeleteSet(Guid id)
    {
        var store = TryGetStore<GoldenSetRecord>();
        if (store == null) return false;
        var record = store.Items<GoldenSetRecord>().FirstOrDefault(r => r.SetId == id);
        if (record == null) return false;
        store.Delete(record.Id);
        return true;
    }

    /// <summary>Returns null when the input is well-formed, otherwise a
    /// human-readable validation error. Surfaces as a 400 in the API layer.</summary>
    public static string? ValidateSet(GoldenSet input)
    {
        if (input == null) return "Golden set is required.";
        if (string.IsNullOrWhiteSpace(input.Name)) return "Name is required.";
        if (input.Items == null) return null;
        for (var i = 0; i < input.Items.Count; i++)
        {
            var item = input.Items[i];
            if (item == null) return $"Item #{i + 1} is null.";
            if (string.IsNullOrWhiteSpace(item.Phrase)) return $"Item #{i + 1} phrase is required.";
            if (item.ExpectedTop == null) continue;
            for (var j = 0; j < item.ExpectedTop.Count; j++)
            {
                var hit = item.ExpectedTop[j];
                if (hit == null || string.IsNullOrWhiteSpace(hit.ContentLink))
                    return $"Expected hit #{j + 1} on '{item.Phrase}' is missing a contentLink.";
            }
        }
        return null;
    }

    // ──────────────────────────────────────────────────────────────────
    //   Run engine
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Execute every phrase in <paramref name="set"/> against <paramref name="config"/>,
    /// score each result list with NDCG@10 and MRR, and persist a
    /// <see cref="RunRecord"/> snapshot. Returns the in-memory <see cref="Run"/>.
    /// </summary>
    public virtual async Task<Run> RunAsync(GoldenSet set, RankingConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(config);
        if (_scopeFactory == null)
            throw new InvalidOperationException("Run engine requires the service scope factory — instantiate the service via DI.");

        var perQuery = new List<QueryEval>();
        using var scope = _scopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<QueryRunnerService>();
        foreach (var item in set.Items ?? new List<GoldenItem>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eval = new QueryEval { Phrase = item.Phrase };
            try
            {
                var request = new RunnerRequest
                {
                    Query = item.Phrase,
                    Locale = string.IsNullOrEmpty(set.Locale) ? null : set.Locale,
                    Ranking = config.Ranking.ToString().ToUpperInvariant() switch
                    {
                        "BOOSTONLY" => "BOOST_ONLY",
                        var other => other
                    },
                    SemanticWeight = config.SemanticWeight,
                    MinimumScore = config.MinScore,
                    Limit = 10
                };
                var result = await runner.RunAsync(request, cancellationToken);
                eval.Hits = result.TotalCount;
                eval.ActualTop = result.Hits.Select(NormaliseLink).ToList();
                eval.Ndcg10 = ComputeNdcgAt10(item.ExpectedTop ?? new(), eval.ActualTop);
                eval.Mrr = ComputeMrr(item.ExpectedTop ?? new(), eval.ActualTop);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                eval.Error = ex.Message;
            }
            perQuery.Add(eval);
        }

        var run = new Run
        {
            Id = Guid.NewGuid(),
            GoldenSetId = set.Id,
            GoldenSetName = set.Name,
            At = DateTime.UtcNow,
            Config = config,
            PerQuery = perQuery,
            Ndcg10 = perQuery.Count == 0 ? 0d : perQuery.Average(p => p.Ndcg10),
            Mrr = perQuery.Count == 0 ? 0d : perQuery.Average(p => p.Mrr)
        };

        PersistRun(run);
        return run;
    }

    /// <summary>List recent runs for a golden set (newest-first), or all
    /// recent runs when <paramref name="goldenSetId"/> is null/empty.</summary>
    public virtual IReadOnlyList<Run> ListRecentRuns(Guid? goldenSetId, int take = RunFetchLimit)
    {
        var store = TryGetStore<RunRecord>();
        if (store == null) return Array.Empty<Run>();
        var clamped = Math.Clamp(take, 1, 200);
        try
        {
            IEnumerable<RunRecord> q = store.Items<RunRecord>();
            if (goldenSetId.HasValue && goldenSetId.Value != Guid.Empty)
                q = q.Where(r => r.GoldenSetId == goldenSetId.Value);
            return q.OrderByDescending(r => r.At).Take(clamped).Select(MapRun).ToList();
        }
        catch
        {
            return Array.Empty<Run>();
        }
    }

    public virtual Run? GetRun(Guid id)
    {
        var store = TryGetStore<RunRecord>();
        if (store == null) return null;
        var record = store.Items<RunRecord>().FirstOrDefault(r => r.RunId == id);
        return record == null ? null : MapRun(record);
    }

    /// <summary>
    /// Pair two runs and emit per-phrase deltas. Phrases not present in both
    /// runs are emitted with a 0 placeholder for the missing side — this
    /// surfaces dropped phrases in the comparison view rather than hiding them.
    /// </summary>
    public virtual CompareResult CompareRuns(Guid runIdA, Guid runIdB)
    {
        var a = GetRun(runIdA);
        var b = GetRun(runIdB);
        var entries = new List<CompareEntry>();
        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (a != null) foreach (var q in a.PerQuery) phrases.Add(q.Phrase);
        if (b != null) foreach (var q in b.PerQuery) phrases.Add(q.Phrase);

        foreach (var phrase in phrases)
        {
            var qa = a?.PerQuery.FirstOrDefault(p => string.Equals(p.Phrase, phrase, StringComparison.OrdinalIgnoreCase));
            var qb = b?.PerQuery.FirstOrDefault(p => string.Equals(p.Phrase, phrase, StringComparison.OrdinalIgnoreCase));
            entries.Add(new CompareEntry
            {
                Phrase = phrase,
                NdcgA = qa?.Ndcg10 ?? 0,
                NdcgB = qb?.Ndcg10 ?? 0,
                NdcgDelta = (qb?.Ndcg10 ?? 0) - (qa?.Ndcg10 ?? 0),
                MrrA = qa?.Mrr ?? 0,
                MrrB = qb?.Mrr ?? 0,
                MrrDelta = (qb?.Mrr ?? 0) - (qa?.Mrr ?? 0)
            });
        }

        return new CompareResult
        {
            RunA = a,
            RunB = b,
            Entries = entries.OrderByDescending(e => Math.Abs(e.NdcgDelta)).ToList(),
            NdcgDelta = (b?.Ndcg10 ?? 0) - (a?.Ndcg10 ?? 0),
            MrrDelta = (b?.Mrr ?? 0) - (a?.Mrr ?? 0)
        };
    }

    /// <summary>
    /// Render a run's per-query rows as CSV. Schema:
    /// <c>phrase,ndcg10,mrr,topResults</c> where <c>topResults</c> is a
    /// pipe-delimited list of the actual top-N IDs.
    /// </summary>
    public static string ToCsv(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var sb = new StringBuilder();
        sb.AppendLine("phrase,ndcg10,mrr,topResults");
        foreach (var q in run.PerQuery ?? new())
        {
            sb.Append(EscapeCsv(q.Phrase)).Append(',');
            sb.Append(q.Ndcg10.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(q.Mrr.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(EscapeCsv(string.Join("|", q.ActualTop ?? new())));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────
    //   Scoring
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Standard NDCG@10. Relevance for a returned doc = the matching expected
    /// hit's <c>Weight</c> (0 if not in <see cref="GoldenItem.ExpectedTop"/>).
    /// Ideal DCG sorts the expected weights descending and uses the same K=10
    /// cutoff. Returns 0 when there are no positive expected weights.
    /// </summary>
    public static double ComputeNdcgAt10(IReadOnlyList<ExpectedHit> expected, IReadOnlyList<string> actualTop)
        => ComputeNdcgAtK(expected, actualTop, 10);

    /// <summary>
    /// Generalised NDCG@K — kept public so the test suite can exercise a
    /// shorter cutoff than 10 against terse fixtures.
    /// </summary>
    public static double ComputeNdcgAtK(IReadOnlyList<ExpectedHit> expected, IReadOnlyList<string> actualTop, int k)
    {
        if (k <= 0) return 0d;
        var weights = BuildWeightLookup(expected);
        if (weights.Count == 0) return 0d;

        // DCG of actual ranking, capped at K.
        var dcg = 0d;
        var actualCount = Math.Min(actualTop?.Count ?? 0, k);
        for (var i = 0; i < actualCount; i++)
        {
            var id = actualTop![i];
            if (id != null && weights.TryGetValue(id, out var w) && w > 0)
            {
                dcg += w / Math.Log2(i + 2);
            }
        }

        // Ideal DCG: take the K largest positive weights, descending.
        var idealWeights = expected
            .Where(e => e.Weight > 0)
            .Select(e => (double)e.Weight)
            .OrderByDescending(w => w)
            .Take(k)
            .ToList();
        if (idealWeights.Count == 0) return 0d;

        var idcg = 0d;
        for (var i = 0; i < idealWeights.Count; i++)
        {
            idcg += idealWeights[i] / Math.Log2(i + 2);
        }

        return idcg <= 0d ? 0d : dcg / idcg;
    }

    /// <summary>
    /// Mean Reciprocal Rank — 1/(rank of first expected hit), 0 when none of
    /// the expected hits appear in the actual top-N. Rank is 1-indexed.
    /// </summary>
    public static double ComputeMrr(IReadOnlyList<ExpectedHit> expected, IReadOnlyList<string> actualTop)
    {
        if (expected == null || expected.Count == 0) return 0d;
        if (actualTop == null || actualTop.Count == 0) return 0d;
        var expectedSet = new HashSet<string>(
            expected.Where(e => !string.IsNullOrEmpty(e.ContentLink)).Select(e => e.ContentLink),
            StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < actualTop.Count; i++)
        {
            if (actualTop[i] != null && expectedSet.Contains(actualTop[i]))
            {
                return 1d / (i + 1);
            }
        }
        return 0d;
    }

    private static Dictionary<string, double> BuildWeightLookup(IReadOnlyList<ExpectedHit> expected)
    {
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (expected == null) return dict;
        foreach (var e in expected)
        {
            if (e == null || string.IsNullOrEmpty(e.ContentLink)) continue;
            // Last write wins on duplicates — the editor would have to do
            // something silly to hit this; we don't sum.
            dict[e.ContentLink] = e.Weight;
        }
        return dict;
    }

    /// <summary>
    /// Prefer the GUID (stable across environments) and fall back to the
    /// numeric Id. Editor fixtures may reference either form, so we surface
    /// both via <see cref="NormaliseLink"/>'s output and let the matcher pick.
    /// We keep this single-string for simplicity — duplicate the expected
    /// content's hit row by guid AND id if you need to match both.
    /// </summary>
    private static string NormaliseLink(RunnerHit hit)
    {
        if (!string.IsNullOrEmpty(hit.ContentGuid)) return hit.ContentGuid;
        if (hit.ContentId.HasValue) return hit.ContentId.Value.ToString(CultureInfo.InvariantCulture);
        return string.Empty;
    }

    // ──────────────────────────────────────────────────────────────────
    //   Persistence helpers
    // ──────────────────────────────────────────────────────────────────

    private void PersistRun(Run run)
    {
        var store = TryGetStore<RunRecord>();
        if (store == null) return;
        var record = new RunRecord
        {
            RunId = run.Id,
            GoldenSetId = run.GoldenSetId,
            GoldenSetName = run.GoldenSetName,
            At = run.At,
            ConfigJson = JsonSerializer.Serialize(run.Config, SerializerOptions),
            PerQueryJson = JsonSerializer.Serialize(run.PerQuery, SerializerOptions),
            Ndcg10 = run.Ndcg10,
            Mrr = run.Mrr
        };
        store.Save(record);
    }

    private static GoldenSet MapSet(GoldenSetRecord r)
    {
        var items = new List<GoldenItem>();
        if (!string.IsNullOrWhiteSpace(r.ItemsJson))
        {
            try
            {
                items = JsonSerializer.Deserialize<List<GoldenItem>>(r.ItemsJson, SerializerOptions) ?? new();
            }
            catch
            {
                items = new();
            }
        }
        return new GoldenSet
        {
            Id = r.SetId,
            Name = r.Name,
            Description = string.IsNullOrEmpty(r.Description) ? null : r.Description,
            Locale = r.Locale,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            Items = items
        };
    }

    private static Run MapRun(RunRecord r)
    {
        RankingConfig config = new();
        List<QueryEval> perQuery = new();
        try
        {
            if (!string.IsNullOrWhiteSpace(r.ConfigJson))
                config = JsonSerializer.Deserialize<RankingConfig>(r.ConfigJson, SerializerOptions) ?? new();
        }
        catch { /* tolerate corrupt rows */ }
        try
        {
            if (!string.IsNullOrWhiteSpace(r.PerQueryJson))
                perQuery = JsonSerializer.Deserialize<List<QueryEval>>(r.PerQueryJson, SerializerOptions) ?? new();
        }
        catch { /* tolerate corrupt rows */ }

        return new Run
        {
            Id = r.RunId,
            GoldenSetId = r.GoldenSetId,
            GoldenSetName = r.GoldenSetName,
            At = r.At,
            Config = config,
            PerQuery = perQuery,
            Ndcg10 = r.Ndcg10,
            Mrr = r.Mrr
        };
    }

    private static DynamicDataStore? TryGetStore<T>()
    {
        try
        {
            return DynamicDataStoreFactory.Instance?.GetStore(typeof(T))
                ?? DynamicDataStoreFactory.Instance?.CreateStore(typeof(T));
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
