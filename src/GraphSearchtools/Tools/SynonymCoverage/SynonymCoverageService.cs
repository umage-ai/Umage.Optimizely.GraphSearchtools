using UmageAI.Optimizely.GraphSearchTools.Helpers;
using UmageAI.Optimizely.GraphSearchTools.Services;
using UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage.Models;
using UmageAI.Optimizely.GraphSearchTools.Tools.Synonyms;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.SynonymCoverage;

/// <summary>
/// Joins the saved synonym blobs against the search-log table to surface
/// pruning candidates (entries that never fired against a logged query) and
/// suggested adds (top zero-result phrases that look like missing synonyms).
///
/// Read-only — the analyzer never mutates the synonym set; clicking a row in
/// the UI navigates back to the Synonyms editor with a hint pre-applied.
/// </summary>
/// <remarks>
/// The "suggested adds" pass uses a small fuzzy match (Levenshtein ≤ 2 OR a
/// shared 4-gram) against a sample of indexed terms drawn from top-result
/// phrases over the same window. When the sample set is empty (no logs, brand
/// new tenant) the pass falls back to the safe v1 behaviour: top N
/// zero-result phrases that aren't already covered by the synonym set.
/// </remarks>
public sealed class SynonymCoverageService
{
    /// <summary>Default look-back when callers omit a window.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(30);

    /// <summary>Cap on suggested-adds rows; keeps the UI card scannable.</summary>
    private const int MaxSuggestedAdds = 50;

    /// <summary>Cap on zero-result rows pulled before similarity scoring.</summary>
    private const int ZeroResultPullSize = 100;

    /// <summary>Cap on top-result phrases used to seed the indexed-term sample.</summary>
    private const int IndexedTermSampleSize = 200;

    /// <summary>Levenshtein cut-off for "close enough to be a typo".</summary>
    private const int LevenshteinThreshold = 2;

    /// <summary>n-gram length for the secondary similarity check.</summary>
    private const int NGramLength = 4;

    private readonly SynonymsService _synonyms;
    private readonly SearchLogService _logs;
    private readonly LanguageSiteEnumerator? _languageSites;

    public SynonymCoverageService(
        SynonymsService synonyms,
        SearchLogService logs,
        LanguageSiteEnumerator? languageSites = null)
    {
        _synonyms = synonyms;
        _logs = logs;
        _languageSites = languageSites;
    }

    /// <summary>
    /// Runs the analyzer over the past <see cref="DefaultWindow"/>.
    /// </summary>
    public Task<SynonymCoverageResult> AnalyzeAsync(CancellationToken cancellationToken)
        => AnalyzeAsync(DefaultWindow, cancellationToken);

    /// <summary>
    /// Runs the analyzer over a custom rolling window.
    /// <paramref name="window"/> is clamped to <c>[1 day, 365 days]</c>.
    /// </summary>
    public async Task<SynonymCoverageResult> AnalyzeAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var clamped = ClampWindow(window);
        var now = DateTime.UtcNow;
        var since = now - clamped;

        // 1. Pull every synonym entry across all configured languages + Global.
        var entries = await CollectSynonymEntriesAsync(cancellationToken);

        // 2. Pull recent log rows so we can answer "did this rule ever fire?"
        //    SearchLogService.ListSince clamps take to [1, 50000]; we ask for
        //    that cap because the analyzer's whole point is to look at every
        //    captured query.
        var logRows = _logs.ListSince(since, take: 50000).ToList();

        // 3. Build a deduped, lower-cased set of phrase tokens we've actually
        //    observed in the window. The synonym-vs-logs join is membership-only;
        //    we don't need per-row counts.
        var loggedPhrases = logRows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.Phrase.Trim().ToLowerInvariant())
            .ToHashSet();

        // 4. Pruning candidates: synonym entries whose trigger terms are all
        //    absent from the log set.
        var unused = FindUnusedEntries(entries, loggedPhrases);

        // 5. Suggested adds: zero-result phrases not already covered, optionally
        //    enriched with a closest-indexed-term hint.
        var suggested = FindSuggestedAdds(entries, since);

        return new SynonymCoverageResult
        {
            GeneratedAt = now,
            WindowStart = since,
            LogsScanned = logRows.Count,
            UnusedEntries = unused,
            SuggestedAdds = suggested
        };
    }

    // ── Synonym ingestion ───────────────────────────────────────────────

    /// <summary>
    /// Fetch every synonym blob (one per host language plus the tenant-global
    /// blob), parse it line-by-line, and return one
    /// <see cref="ParsedSynonymEntry"/> per non-empty line. Languages whose
    /// blobs are absent from Graph yield zero entries — same null-tolerant
    /// shape the editor uses.
    /// </summary>
    private async Task<IReadOnlyList<ParsedSynonymEntry>> CollectSynonymEntriesAsync(CancellationToken cancellationToken)
    {
        var languages = new List<string?> { null }; // null = Global slot
        if (_languageSites != null)
        {
            try
            {
                foreach (var site in _languageSites.Enumerate())
                {
                    if (string.IsNullOrWhiteSpace(site.LanguageCode)) continue;
                    if (!languages.Contains(site.LanguageCode, StringComparer.OrdinalIgnoreCase))
                        languages.Add(site.LanguageCode);
                }
            }
            catch
            {
                // Enumerator depends on Optimizely runtime services; in a host-less
                // environment it'll throw. Fall back to Global-only.
            }
        }

        var collected = new List<ParsedSynonymEntry>();
        foreach (var language in languages)
        {
            string content;
            try
            {
                content = await _synonyms.GetAsync(
                    new Services.SynonymsQuery { LanguageRouting = language },
                    cancellationToken);
            }
            catch
            {
                // Missing slot or transient error — skip this language; the
                // rest of the analyzer still runs on whatever did load.
                continue;
            }

            if (string.IsNullOrWhiteSpace(content)) continue;
            var label = string.IsNullOrEmpty(language) ? "Global" : language!;
            foreach (var line in content.Split('\n'))
            {
                var rule = line.Trim();
                if (string.IsNullOrEmpty(rule)) continue;
                collected.Add(new ParsedSynonymEntry(label, rule, ExtractTriggerTerms(rule)));
            }
        }

        return collected;
    }

    /// <summary>
    /// Pull the trigger terms out of a synonym rule line.
    /// <list type="bullet">
    ///   <item><c>a => b, c</c> — trigger is the LHS only (RHS is the
    ///   replacement; only LHS would appear in user queries).</item>
    ///   <item><c>a, b, c</c> — every term triggers.</item>
    /// </list>
    /// Returns lower-cased, trimmed, deduped terms. Empty input yields an
    /// empty set.
    /// </summary>
    public static IReadOnlyList<string> ExtractTriggerTerms(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return Array.Empty<string>();

        var arrowIdx = rule.IndexOf("=>", StringComparison.Ordinal);
        var lhs = arrowIdx >= 0 ? rule[..arrowIdx] : rule;

        return lhs.Split(',')
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();
    }

    // ── Unused detection ────────────────────────────────────────────────

    private static IReadOnlyList<UnusedSynonymEntry> FindUnusedEntries(
        IReadOnlyList<ParsedSynonymEntry> entries,
        IReadOnlySet<string> loggedPhrases)
    {
        const string reason = "no logged query matched in window";
        var unused = new List<UnusedSynonymEntry>();
        foreach (var entry in entries)
        {
            if (entry.TriggerTerms.Count == 0) continue;
            // An entry is "unused" only when none of its triggers ever
            // appeared in a logged query — partial coverage still counts as
            // active to avoid pestering editors about side terms.
            var anyHit = entry.TriggerTerms.Any(t => loggedPhrases.Contains(t));
            if (!anyHit)
            {
                unused.Add(new UnusedSynonymEntry(entry.Language, entry.Rule, reason));
            }
        }
        return unused;
    }

    // ── Suggested adds ──────────────────────────────────────────────────

    private IReadOnlyList<SuggestedSynonym> FindSuggestedAdds(
        IReadOnlyList<ParsedSynonymEntry> entries,
        DateTime since)
    {
        // Already-covered triggers — case-insensitive set so we filter zero-
        // result phrases that the existing synonym set already addresses.
        var covered = entries
            .SelectMany(e => e.TriggerTerms)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var zeros = _logs.ZeroResultPhrases(since, take: ZeroResultPullSize)
            .Where(z => !string.IsNullOrWhiteSpace(z.Phrase))
            .Where(z => !covered.Contains(z.Phrase.Trim().ToLowerInvariant()))
            .ToList();

        if (zeros.Count == 0) return Array.Empty<SuggestedSynonym>();

        // Build the indexed-term sample from top phrases that returned at
        // least some results — those are the strings actually present in the
        // index. Phrases whose every session was zero-result aren't indexed
        // terms by definition; including them would let the analyzer "fuzzy
        // match" zero-result phrases against each other and produce
        // misleading hints. When the sample is empty (a brand-new tenant),
        // we degrade to the fall-back behaviour: list zero-result phrases
        // without a closest-term annotation.
        var indexedSample = _logs
            .TopPhrases(since, take: IndexedTermSampleSize)
            .Where(p => p.ZeroResultRate < 1d)
            .Select(p => p.Phrase.Trim().ToLowerInvariant())
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList();

        var rows = new List<SuggestedSynonym>(Math.Min(MaxSuggestedAdds, zeros.Count));
        foreach (var z in zeros.Take(MaxSuggestedAdds))
        {
            var (closest, distance) = FindClosestIndexedTerm(z.Phrase, indexedSample);
            rows.Add(new SuggestedSynonym(
                Phrase: z.Phrase,
                Locale: z.Locale ?? string.Empty,
                Hits: z.Hits,
                ClosestIndexedTerm: closest,
                Similarity: distance));
        }
        return rows;
    }

    /// <summary>
    /// Finds the indexed term that's most similar to <paramref name="phrase"/>.
    /// Returns <c>(null, null)</c> when no candidate clears the threshold —
    /// the row still surfaces in fall-back mode, just without a hint.
    /// </summary>
    private static (string? Term, int? Distance) FindClosestIndexedTerm(
        string phrase, IReadOnlyList<string> indexedTerms)
    {
        if (indexedTerms.Count == 0) return (null, null);
        var lower = phrase.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lower)) return (null, null);

        string? bestTerm = null;
        var bestDistance = int.MaxValue;

        var phraseGrams = NGrams(lower, NGramLength);

        foreach (var candidate in indexedTerms)
        {
            // Exact match never makes for a useful synonym suggestion.
            if (string.Equals(candidate, lower, StringComparison.Ordinal)) continue;

            var distance = LevenshteinBounded(lower, candidate, LevenshteinThreshold + 1);
            if (distance >= 1 && distance <= LevenshteinThreshold)
            {
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTerm = candidate;
                }
                continue;
            }

            // Levenshtein bailed; try the n-gram check. We only accept the
            // n-gram match when no Levenshtein candidate has been found yet —
            // a typo wins over a substring overlap.
            if (bestTerm == null && phraseGrams.Count > 0)
            {
                var candidateGrams = NGrams(candidate, NGramLength);
                if (phraseGrams.Overlaps(candidateGrams))
                {
                    bestTerm = candidate;
                    bestDistance = distance == int.MaxValue ? LevenshteinThreshold + 1 : distance;
                }
            }
        }

        return bestTerm == null ? (null, null) : (bestTerm, bestDistance);
    }

    /// <summary>
    /// Bounded Levenshtein — bails out as soon as the running distance exceeds
    /// <paramref name="ceiling"/>. Lets us scan a few hundred candidates in
    /// the background without blowing the request budget.
    /// </summary>
    private static int LevenshteinBounded(string a, string b, int ceiling)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;

        // Cheap length-prune — if the lengths differ by more than the ceiling
        // we know up front the distance will too.
        if (Math.Abs(a.Length - b.Length) >= ceiling) return ceiling;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            if (rowMin >= ceiling) return ceiling;
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    private static HashSet<string> NGrams(string s, int n)
    {
        var grams = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(s) || s.Length < n) return grams;
        for (var i = 0; i + n <= s.Length; i++)
        {
            grams.Add(s.Substring(i, n));
        }
        return grams;
    }

    private static TimeSpan ClampWindow(TimeSpan window)
    {
        if (window <= TimeSpan.Zero) return DefaultWindow;
        var min = TimeSpan.FromDays(1);
        var max = TimeSpan.FromDays(365);
        if (window < min) return min;
        if (window > max) return max;
        return window;
    }

    private sealed record ParsedSynonymEntry(string Language, string Rule, IReadOnlyList<string> TriggerTerms);
}
