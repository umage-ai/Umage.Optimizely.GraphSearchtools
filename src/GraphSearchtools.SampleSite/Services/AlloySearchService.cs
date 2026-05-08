using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace UmageAI.Optimizely.GraphSearchTools.SampleSite.Services;

/// <summary>
/// Faceted site search against Optimizely Graph for the Alloy demo. Implements
/// the count-source-per-facet pattern described in
/// <c>docs/research/faceted-search-ux-guidelines.md</c> §5–§6: for each facet
/// group we run a count query that applies every active filter EXCEPT that
/// facet's own — so checkbox counts are stable across a click and the panel
/// renders disabled-not-hidden values.
///
/// One user-facing facet:
///   • <c>ContentType</c> — top-level (page types). The Alloy index also
///     contains blocks / images / media; we always filter results to the
///     <c>Page</c> branch and enumerate from a static page-type list.
///
/// Locale is no longer a user-toggleable facet — it's pinned to the active
/// language branch served by Alloy's page route (<c>PageContext.LanguageID</c>),
/// so visitors only ever see content in the language they're browsing in.
/// The same locale also drives the pinned-collection lookup
/// (<c>alloy-{locale}</c>) so admin-curated pins land on the correct branch.
/// </summary>
public sealed class AlloySearchService
{
    /// <summary>
    /// Static enumeration source for the ContentType facet (top-level type
    /// facet). The Alloy schema indexes ContentType as the full inheritance
    /// chain (e.g. <c>["ArticlePage", "StandardPage", "Page", "Content"]</c>);
    /// we list the leaf page types we care about and drop the inherited /
    /// non-page entries (Page, Block, Media, Image, …) when reading the facet
    /// output. LandingPage is included even though the seed catalog has zero
    /// — that's the §4 disabled-not-hidden case worth demonstrating.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownContentTypes = new[]
    {
        "ArticlePage", "NewsPage", "ProductPage", "StandardPage", "LandingPage", "ContactPage"
    };

    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly IGraphAdminClient _graphAdmin;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Process-wide cache mapping pinned-collection key (e.g. <c>alloy-en</c>) →
    /// Graph collection id. The id is what the consumer GraphQL query needs
    /// for the <c>usePinned</c> argument; the admin tool only ever talks in
    /// terms of the human-readable key. Populated lazily on the first hits
    /// query for a locale; missed lookups trigger a fresh list from the admin
    /// API so newly-created collections become visible without a restart.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> _pinnedCollectionIds = new(StringComparer.OrdinalIgnoreCase);

    public AlloySearchService(HttpClient http, IGraphCredentialsResolver credentials, IGraphAdminClient graphAdmin)
    {
        _http = http;
        _credentials = credentials;
        _graphAdmin = graphAdmin;
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    public async Task<AlloySearchResult> SearchAsync(AlloySearchRequest request, CancellationToken cancellationToken)
    {
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured)
        {
            return AlloySearchResult.NotConfigured();
        }

        var endpoint = $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";

        var selectedTypes = NormaliseSelection(request.SelectedContentTypes, KnownContentTypes);
        var phrase = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        var limit = Math.Clamp(request.Limit, 1, 50);
        // Active language branch served by the SearchPage route. Lower-cased
        // because the alloy-search profile registers locales as "en"/"sv" and
        // the pinned-collection key formula ("alloy-{locale}") is built around
        // that casing. Empty when the controller couldn't resolve a culture —
        // we then skip the language clause and let Graph return everything.
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? null : request.Locale.Trim().ToLowerInvariant();

        // Resolve the pinned-results collection id for the active locale. The
        // alloy-search profile's pinned-key formula is "alloy-{locale}", so we
        // can derive the lookup key directly from the request culture and
        // marketers' edits land on the matching branch automatically. No
        // active locale (e.g. an unrouted call) → no pinning.
        var pinnedCollectionId = phrase != null && locale != null
            ? await GetPinnedCollectionIdAsync($"alloy-{locale}", cancellationToken)
            : null;

        // Per §6 — every facet renders from two distinct sources:
        //   • Enumeration source (what rows exist in this section at all)
        //   • Count source (this facet's count given current filters minus its own)
        // ContentType has a static enumeration (KnownContentTypes). Language
        // is no longer a facet — the active locale is applied as a hard filter
        // on every query, so there's nothing for the user to toggle.
        var hitsTask = ExecuteAsync(endpoint, BuildHitsQuery(phrase, selectedTypes, locale, limit, pinnedCollectionId), cancellationToken);
        var contentTypeCountTask = ExecuteAsync(endpoint, BuildFacetCountQuery(phrase, omitContentType: true, selectedTypes, locale, FacetField.ContentType), cancellationToken);

        await Task.WhenAll(hitsTask, contentTypeCountTask);

        using var hitsBody = hitsTask.Result;
        using var typeCountBody = contentTypeCountTask.Result;

        var typeCounts = ParseFacetCounts(typeCountBody, "ContentType");

        var hits = ParseHits(hitsBody, phrase);
        var total = ParseTotal(hitsBody);

        var typeFacet = BuildFacetGroup(
            field: "type",
            enumeration: KnownContentTypes,
            counts: typeCounts,
            selected: selectedTypes,
            includeUnknownSelections: false);

        return new AlloySearchResult
        {
            Total = total,
            Hits = hits,
            ContentTypeFacet = typeFacet,
            ActiveLocale = locale
        };
    }

    /// <summary>
    /// Type-ahead suggestions for the search bar. Uses Optimizely Graph's
    /// per-type <c>autocomplete</c> field over <c>MetaKeywords</c> — the only
    /// text autocomplete source Alloy exposes by default. Each known page
    /// type contributes its own pool; results are merged + deduped + capped.
    /// </summary>
    public async Task<IReadOnlyList<string>> SuggestAsync(string prefix, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return Array.Empty<string>();
        var creds = _credentials.Resolve();
        if (!creds.IsQueryConfigured) return Array.Empty<string>();

        prefix = prefix.Trim();
        var clamped = Math.Clamp(limit, 1, 25);
        var endpoint = $"{creds.GatewayAddress.TrimEnd('/')}/content/v2?auth={creds.SingleKey}";

        // One round-trip across every known page type. MetaKeywords is the
        // only autocomplete-indexed text field on Alloy's stock content types
        // (Name isn't marked Searchable). Aliasing each branch lets us
        // deserialize the merged response without name collisions.
        var aliases = KnownContentTypes
            .Select(t => $"{t.ToLowerInvariant()}: {t}(limit: 0) {{ autocomplete {{ MetaKeywords(value: {EscapeString(prefix)}, limit: {clamped}) }} }}")
            .ToList();
        var queryDocument = "{\n" + string.Join("\n", aliases) + "\n}";

        try
        {
            using var doc = await ExecuteAsync(endpoint, queryDocument, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var typeBranch in data.EnumerateObject())
                {
                    if (typeBranch.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!typeBranch.Value.TryGetProperty("autocomplete", out var ac) || ac.ValueKind != JsonValueKind.Object) continue;
                    if (!ac.TryGetProperty("MetaKeywords", out var mk) || mk.ValueKind != JsonValueKind.Array) continue;
                    foreach (var entry in mk.EnumerateArray())
                    {
                        var s = entry.GetString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        if (seen.Add(s)) ordered.Add(s);
                    }
                }
            }
            // Prefer suggestions whose first matching token is at the start —
            // gives a more "this is what I was typing" feel before falling
            // back to mid-string matches.
            ordered.Sort((a, b) =>
            {
                var ai = a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                var bi = b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                if (ai != bi) return ai - bi;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return ordered.Take(clamped).ToList();
        }
        catch
        {
            // The autocomplete dropdown is best-effort; never let a Graph
            // hiccup block typing in the search bar.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Representative form of the hits query — uses placeholder substitutions
    /// for the dynamic parts (<c>$phrase</c>, <c>$locale</c>,
    /// <c>$pinnedCollectionId</c>) so the registered profile's admin view
    /// reflects every code path the storefront actually runs, including the
    /// <c>usePinned</c> directive that applies pinned-results edits to the
    /// SERP and the per-locale language filter that scopes the SERP to the
    /// branch currently being browsed.
    /// </summary>
    public static string SampleHitsQueryDocument
        => BuildHitsQuery(phrase: "$phrase", Array.Empty<string>(), locale: "$locale", limit: 20, pinnedCollectionId: "$pinnedCollectionId");

    // Builds the items query: filter by phrase + active locale + ContentType
    // facet selections. Always restricts to pages so blocks/images don't show
    // up as hits. When pinnedCollectionId is provided alongside a phrase,
    // emits Graph's `usePinned` argument so editor-curated pins surface above
    // organic results.
    internal static string BuildHitsQuery(
        string? phrase,
        IReadOnlyList<string> types,
        string? locale,
        int limit,
        string? pinnedCollectionId = null)
    {
        var clauses = BuildClauses(phrase, types, locale, includeContentType: true, includeLocale: true);
        // SEMANTIC ranking has no real "no match" floor — gibberish like
        // "asdfasdf" still nearest-neighbours into the corpus and returns
        // semantic-only hits with _score ≈ 1.0–1.2. Empirically (probe runs
        // against this tenant's Alloy demo corpus) lexical hits land at
        // _score ≥ 38, so _minimumScore: 2 cleanly cuts the noise band
        // without losing any real match. Tune higher (5–20) for noisier
        // corpora; lower if you want semantic-only matches to surface.
        var orderBy = string.IsNullOrEmpty(phrase)
            ? "orderBy: { StartPublish: DESC }"
            : "orderBy: { _ranking: SEMANTIC, _minimumScore: 2 }";
        // usePinned only makes sense when there's a phrase to match against.
        var pinned = !string.IsNullOrEmpty(phrase) && !string.IsNullOrEmpty(pinnedCollectionId)
            ? $"usePinned: {{ phrase: {EscapeString(phrase)}, collectionId: {EscapeString(pinnedCollectionId)} }}"
            : string.Empty;

        return $@"
{{
  Content(
    where: {{ _and: [{{ ContentType: {{ eq: ""Page"" }} }}, {clauses}] }}
    limit: {limit}
    {orderBy}
    {pinned}
  ) {{
    total
    items {{
      Name
      ContentType
      RelativePath
      Language {{ Name }}
      ContentLink {{ GuidValue }}
      _fulltext
    }}
  }}
}}";
    }

    /// <summary>
    /// Looks up the Graph collection id for a pinned-collection key, caching
    /// the result. Cache misses trigger a fresh list from the admin API so
    /// collections created via the addon's UI become available to the
    /// storefront on the next search without restarting the host.
    /// </summary>
    private async Task<string?> GetPinnedCollectionIdAsync(string pinnedKey, CancellationToken cancellationToken)
    {
        if (_pinnedCollectionIds.TryGetValue(pinnedKey, out var cached)) return cached;

        try
        {
            var collections = await _graphAdmin.GetCollectionsAsync(cancellationToken);
            foreach (var col in collections)
            {
                if (!string.IsNullOrEmpty(col.Key) && !string.IsNullOrEmpty(col.Id))
                {
                    _pinnedCollectionIds[col.Key] = col.Id;
                }
            }
        }
        catch
        {
            // Pinning is a best-effort enhancement; never let a failed admin
            // call break the search itself.
            return null;
        }
        return _pinnedCollectionIds.TryGetValue(pinnedKey, out var resolved) ? resolved : null;
    }

    // Per-facet count query (§5/§6). We pass `omitContentType` to remove that
    // facet's selections from the where clause while keeping the locale clause
    // applied — the count is "what the user would see if they clicked this
    // value", stable across a toggle of the same facet.
    private static string BuildFacetCountQuery(
        string? phrase,
        bool omitContentType,
        IReadOnlyList<string> types,
        string? locale,
        FacetField include)
    {
        var clauses = BuildClauses(
            phrase,
            types,
            locale,
            includeContentType: !omitContentType,
            includeLocale: true);

        var facetBlock = include switch
        {
            FacetField.ContentType => "ContentType(limit: 100, orderType: COUNT, orderBy: DESC) { name count }",
            _ => string.Empty
        };

        // limit: 0 — facet-only query, items not needed.
        return $@"
{{
  Content(
    where: {{ _and: [{{ ContentType: {{ eq: ""Page"" }} }}, {clauses}] }}
    limit: 0
  ) {{
    facets {{
      {facetBlock}
    }}
  }}
}}";
    }

    // Builds the WHERE clause as a `_and: [ ... ]` body — joined by the caller
    // with the always-applied `ContentType: { eq: "Page" }` restriction.
    // Returns `{}` (empty object) when no clauses apply, which keeps the
    // composed `_and` valid GraphQL.
    private static string BuildClauses(
        string? phrase,
        IReadOnlyList<string> types,
        string? locale,
        bool includeContentType,
        bool includeLocale)
    {
        var inner = new List<string>();
        if (!string.IsNullOrEmpty(phrase))
        {
            // synonyms: ONE is required for Graph to apply the synonym pool
            // saved under synonym_slot=one (the addon's Synonyms editor writes
            // there). Without this argument, _fulltext silently skips the
            // synonym index — so editor-side rules like "sdfgsdfg => alloy"
            // wouldn't fire at the storefront. ONE matches the slot the
            // addon's UI defaults to; switch to TWO if you maintain a
            // staging slot and activate it via the Graph admin API.
            inner.Add($"{{ _fulltext: {{ match: {EscapeString(phrase)}, synonyms: ONE }} }}");
        }
        if (includeContentType && types.Count > 0)
        {
            inner.Add($"{{ ContentType: {{ in: [{string.Join(", ", types.Select(EscapeString))}] }} }}");
        }
        if (includeLocale && !string.IsNullOrEmpty(locale))
        {
            // eq: rather than in: — locale is single-valued (page route serves
            // one branch at a time). Using eq makes the placeholder in the
            // sample query (`$locale`) substitute cleanly without dragging in
            // list syntax.
            inner.Add($"{{ Language: {{ Name: {{ eq: {EscapeString(locale)} }} }} }}");
        }
        return inner.Count == 0 ? "{}" : $"{{ _and: [{string.Join(", ", inner)}] }}";
    }

    private async Task<JsonDocument> ExecuteAsync(string endpoint, string queryDocument, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { query = queryDocument }, _serializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Optimizely Graph returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
        var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
        {
            // Surface the first GraphQL error verbatim — easier to debug than
            // a generic 200-with-errors-payload response.
            var first = errors[0].TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                ? msg.GetString()
                : errors.GetRawText();
            doc.Dispose();
            throw new InvalidOperationException($"Optimizely Graph error: {first}");
        }
        return doc;
    }

    private static int ParseTotal(JsonDocument doc)
    {
        if (!TryGetContentBlock(doc, out var content)) return 0;
        if (content.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var n)) return n;
        return 0;
    }

    private static List<AlloySearchHit> ParseHits(JsonDocument doc, string? phrase)
    {
        var hits = new List<AlloySearchHit>();
        if (!TryGetContentBlock(doc, out var content)) return hits;
        if (!content.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return hits;

        foreach (var item in items.EnumerateArray())
        {
            hits.Add(new AlloySearchHit
            {
                Title = GetString(item, "Name") ?? string.Empty,
                Url = GetString(item, "RelativePath") ?? "#",
                Excerpt = BuildExcerpt(GetFulltextSnippet(item), phrase),
                ContentType = GetLeafContentType(item) ?? "Content",
                Language = GetNestedString(item, "Language", "Name") ?? string.Empty
            });
        }
        return hits;
    }

    // Google-style snippet — window around the first matching token. Falls
    // back to the leading snippet when nothing matches (e.g. semantic search
    // surfaced a result by meaning, not literal substring).
    private static string BuildExcerpt(string? body, string? phrase)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        const int windowChars = 220;
        const int leadChars = 60;

        if (string.IsNullOrWhiteSpace(phrase))
        {
            return Trim(body, windowChars);
        }

        var tokens = phrase
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToArray();

        var firstMatch = -1;
        foreach (var token in tokens)
        {
            var idx = body.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (firstMatch < 0 || idx < firstMatch))
            {
                firstMatch = idx;
            }
        }

        if (firstMatch < 0) return Trim(body, windowChars);

        var start = Math.Max(0, firstMatch - leadChars);
        // Walk forward to a word boundary so we don't slice mid-word.
        while (start > 0 && start < body.Length && !char.IsWhiteSpace(body[start - 1])) start++;
        var snippet = body[start..];
        snippet = Trim(snippet, windowChars);
        return start > 0 ? "… " + snippet : snippet;

        static string Trim(string s, int max)
        {
            s = s.Trim();
            if (s.Length <= max) return s;
            // Cut on a word boundary.
            var cut = s.LastIndexOf(' ', max);
            if (cut < max / 2) cut = max;
            return s[..cut].TrimEnd(',', '.', ';', ':') + "…";
        }
    }

    // _fulltext is a string[] — every searchable text fragment that
    // contributed to the index. Concatenate, strip HTML markup so the snippet
    // renders cleanly, and let the caller trim to a Google-sized excerpt.
    private static string? GetFulltextSnippet(JsonElement item)
    {
        if (!item.TryGetProperty("_fulltext", out var ft)) return null;
        var raw = ft.ValueKind switch
        {
            JsonValueKind.String => ft.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(" ",
                ft.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // Strip HTML tags (rich text fields land here as <p>…</p>) and
        // collapse runs of whitespace.
        var stripped = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(stripped);
        var collapsed = System.Text.RegularExpressions.Regex.Replace(decoded, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(collapsed) ? null : collapsed;
    }

    private static Dictionary<string, int> ParseFacetCounts(JsonDocument doc, string facetName)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!TryGetContentBlock(doc, out var content)) return result;
        if (!content.TryGetProperty("facets", out var facets) || facets.ValueKind != JsonValueKind.Object) return result;
        if (!facets.TryGetProperty(facetName, out var facet) || facet.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in facet.EnumerateArray())
        {
            var name = GetString(entry, "name");
            if (string.IsNullOrEmpty(name)) continue;
            var count = entry.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var n) ? n : 0;
            result[name!] = count;
        }
        return result;
    }

    private static FacetGroup BuildFacetGroup(string field, IReadOnlyList<string> enumeration, IReadOnlyDictionary<string, int> counts, IReadOnlyList<string> selected, bool includeUnknownSelections)
    {
        var selectedSet = new HashSet<string>(selected, StringComparer.Ordinal);
        var values = new List<FacetValue>(enumeration.Count + selected.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in enumeration)
        {
            if (!seen.Add(name)) continue;
            counts.TryGetValue(name, out var count);
            values.Add(new FacetValue
            {
                Name = name,
                Count = count,
                Selected = selectedSet.Contains(name)
            });
        }

        // §4: surface user-selected values that aren't in the enumeration so
        // the user can always un-click. Only applies to facets whose
        // enumeration source is itself filtered (e.g. Language from the
        // catalog). For the static ContentType list this branch is a no-op.
        if (includeUnknownSelections)
        {
            foreach (var pick in selected)
            {
                if (seen.Add(pick))
                {
                    values.Add(new FacetValue { Name = pick, Count = 0, Selected = true });
                }
            }
        }

        return new FacetGroup
        {
            Field = field,
            Values = values,
            SelectedCount = selectedSet.Count
        };
    }

    private static bool TryGetContentBlock(JsonDocument doc, out JsonElement content)
    {
        content = default;
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return false;
        if (!data.TryGetProperty("Content", out content) || content.ValueKind != JsonValueKind.Object) return false;
        return true;
    }

    private static string EscapeString(string value)
    {
        var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    private static IReadOnlyList<string> NormaliseSelection(IEnumerable<string>? selection, IReadOnlyList<string>? whitelist = null)
    {
        if (selection == null) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (var raw in selection)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var trimmed = raw.Trim();
            if (whitelist != null && !whitelist.Contains(trimmed, StringComparer.Ordinal)) continue;
            if (seen.Add(trimmed)) ordered.Add(trimmed);
        }
        return ordered;
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string? GetNestedString(JsonElement el, string a, string b)
        => el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object ? GetString(inner, b) : null;

    private static readonly HashSet<string> GenericContentTypes = new(StringComparer.Ordinal)
    {
        "Content", "Page", "Block", "Media", "Image", "Video"
    };

    private static string? GetLeafContentType(JsonElement item)
    {
        if (!item.TryGetProperty("ContentType", out var ct) || ct.ValueKind != JsonValueKind.Array) return null;
        // Graph indexes ContentType as the full inheritance chain plus a few
        // generic interface names (Page / Content / Block). Walk the array
        // and return the first entry that ISN'T one of the generics — that's
        // the concrete leaf type the user actually cares about.
        string? fallback = null;
        foreach (var t in ct.EnumerateArray())
        {
            var s = t.GetString();
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (!GenericContentTypes.Contains(s)) return s;
            fallback ??= s;
        }
        return fallback;
    }

    private enum FacetField { ContentType }
}

public sealed class AlloySearchRequest
{
    public string? Query { get; init; }
    public IReadOnlyList<string> SelectedContentTypes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Active language branch served by the SearchPage route — Optimizely
    /// passes this through <c>PageContext.LanguageID</c>. The service uses it
    /// to filter results to the matching branch and to resolve the pinned
    /// collection (<c>alloy-{locale}</c>). Null/empty falls back to no
    /// language clause and no pinning.
    /// </summary>
    public string? Locale { get; init; }
    public int Limit { get; init; } = 20;
}

public sealed class AlloySearchResult
{
    public int Total { get; init; }
    public IReadOnlyList<AlloySearchHit> Hits { get; init; } = Array.Empty<AlloySearchHit>();
    public FacetGroup ContentTypeFacet { get; init; } = new();

    /// <summary>
    /// Locale that scoped this query — surfaces in the view so the SERP can
    /// show a small "results in <em>en</em>" label without re-deriving it.
    /// </summary>
    public string? ActiveLocale { get; init; }

    public bool Configured { get; init; } = true;

    public static AlloySearchResult NotConfigured() => new() { Configured = false };
}

public sealed class AlloySearchHit
{
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = "#";
    public string Excerpt { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
}

public sealed class FacetGroup
{
    public string Field { get; init; } = string.Empty;
    public IReadOnlyList<FacetValue> Values { get; init; } = Array.Empty<FacetValue>();
    public int SelectedCount { get; init; }
}

public sealed class FacetValue
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool Selected { get; init; }
}
