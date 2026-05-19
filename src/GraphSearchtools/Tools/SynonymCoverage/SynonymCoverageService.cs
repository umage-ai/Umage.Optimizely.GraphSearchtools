using UmageAI.Optimizely.GraphSearchTools.Abstractions;
using UmageAI.Optimizely.GraphSearchTools.Helpers;
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

    /// <summary>
    /// Cap on the phrase enumeration used to answer "did this synonym ever fire?".
    /// We deliberately ask for far more than the UI cards display so the unused-
    /// detection pass sees the long tail, not just the head.
    /// </summary>
    private const int LoggedPhraseEnumerationSize = 5000;

    private readonly SynonymsService _synonyms;
    private readonly ITelemetryReader _reader;
    private readonly LanguageSiteEnumerator? _languageSites;

    public SynonymCoverageService(
        SynonymsService synonyms,
        ITelemetryReader reader,
        LanguageSiteEnumerator? languageSites = null)
    {
        _synonyms = synonyms;
        _reader = reader;
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

        // 2. Enumerate the phrase set the window has actually seen. Under the
        //    aggregate-first design we ask the reader for the long-tail head —
        //    each PhraseAggregate counts as "this phrase fired N times".
        //    LoggedPhraseEnumerationSize is generous because the unused-
        //    detection pass cares about the tail, not the head.
        var phraseAggregates = await _reader.TopPhrasesAsync(
            new TelemetryQuery(since, now, LoggedPhraseEnumerationSize),
            cancellationToken);

        // 3. Build the deduped, lower-cased set used for synonym membership,
        //    plus a phrase→hits map for the per-rule activity rollup. The set
        //    powers the (legacy) unused-detection pass; the map turns "did
        //    this rule fire?" into "how many times?" without an extra reader
        //    round-trip.
        var loggedPhrases = new HashSet<string>();
        var hitsByPhrase = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var p in phraseAggregates)
        {
            if (string.IsNullOrWhiteSpace(p.Phrase)) continue;
            var key = p.Phrase.Trim().ToLowerInvariant();
            if (key.Length == 0) continue;
            loggedPhrases.Add(key);
            // Same phrase can arrive multiple times with different locale /
            // channel splits — accumulate rather than overwrite.
            hitsByPhrase.TryGetValue(key, out var prev);
            hitsByPhrase[key] = prev + p.Hits;
        }

        var totalEvents = phraseAggregates.Sum(p => p.Hits);

        // Pruning candidates: synonym entries whose trigger terms are all
        // absent from the log set. The former "suggested adds" pass (zero-
        // result phrases that look like missing synonyms) belongs in the
        // per-channel insights pipeline rather than the global synonyms tool
        // — dropped from this analyzer along with the standalone UI.
        var unused = FindUnusedEntries(entries, loggedPhrases);

        // Per-rule hit totals. For each rule, sum the hits of every trigger
        // term that appears in the window. A rule like `phone, mobile` with
        // phone=50 and mobile=30 reads as 80. Hits=0 falls out naturally for
        // rules whose triggers never showed up.
        var activity = entries
            .Select(e => new RuleActivityRow(
                Language: e.Language,
                Entry: e.Rule,
                Hits: e.TriggerTerms.Sum(t =>
                    hitsByPhrase.TryGetValue(t, out var h) ? h : 0L)))
            .ToList();

        return new SynonymCoverageResult
        {
            GeneratedAt = now,
            WindowStart = since,
            LogsScanned = totalEvents,
            TotalRules = entries.Count,
            UnusedEntries = unused,
            RuleActivity = activity
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
            // Optimizely Graph returns the synonym body as a JSON-quoted
            // string for some tenants (leading `"`, escaped `\n` between
            // rules) and as a plain newline-separated blob for others.
            // Match the JS parser's defensive normalisation — without this
            // the analyzer treats the entire blob as one rule and the unused
            // / activity counts come out as nonsense.
            content = UnwrapIfJsonString(content);
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

    /// <summary>
    /// Unwraps a synonym blob that was returned as a JSON-encoded string.
    /// Leaves plain newline-separated content untouched. Mirrors the
    /// normalisation step in synonyms-grid.js / synonyms-aurora.js so the
    /// server-side analyzer sees the same per-line view as the editor.
    /// </summary>
    private static string UnwrapIfJsonString(string content)
    {
        if (content.Length < 2) return content;
        if (content[0] != '"' || content[^1] != '"') return content;
        try
        {
            var unwrapped = System.Text.Json.JsonSerializer.Deserialize<string>(content);
            return unwrapped ?? content;
        }
        catch
        {
            return content;
        }
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
