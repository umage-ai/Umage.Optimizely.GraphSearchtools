using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UmageAI.Optimizely.GraphSearchTools.Abstractions;

namespace GraphSearchtools.SampleSiteCms13.Services;

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
    /// facet). The Alloy schema indexes <c>_metadata.types</c> as the full
    /// inheritance chain (e.g. <c>["StandardPage", "_Page", "_Content"]</c>);
    /// we list the leaf page types we care about and drop the inherited /
    /// non-page entries (<c>_Page</c>, <c>_Content</c>, <c>_Item</c>, …) when
    /// reading the facet output. LandingPage is included even though the
    /// seed catalog has zero — that's the §4 disabled-not-hidden case worth
    /// demonstrating.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownContentTypes = new[]
    {
        "ArticlePage", "NewsPage", "ProductPage", "StandardPage", "LandingPage", "ContactPage"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly IGraphCredentialsResolver _credentials;
    private readonly IGraphAdminClient _graphAdmin;

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
        // because the alloy-search channel registers locales as "en"/"sv" and
        // the pinned-collection key formula ("alloy-{locale}") is built around
        // that casing. Empty when the controller couldn't resolve a culture —
        // we then skip the language clause and let Graph return everything.
        var locale = string.IsNullOrWhiteSpace(request.Locale) ? null : request.Locale.Trim().ToLowerInvariant();

        // Resolve the pinned-results collection id for the active locale. The
        // alloy-search channel's pinned-key formula is "alloy-{locale}", so we
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

        // Facet name aligns with the CMS 13 schema (`_metadata.types`); the
        // facet parser walks `facets._metadata[facetName]`.
        var typeCounts = ParseFacetCounts(typeCountBody, "types");

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

        // CMS 13 doesn't promote `MetaKeywords` to the per-type autocomplete
        // index the way the CMS 12 stack did, and the new `_metadata` block
        // exposes only identifier-ish fields (key/locale/url/...) under
        // `autocomplete`. The pragmatic replacement is a small `_Content`
        // query that prefix-matches `displayName` and surfaces the page
        // titles as suggestions — same shape from the consumer's side, just
        // backed by a real query rather than the autocomplete sidecar.
        var queryDocument = $@"
{{
  _Content(
    where: {{ _and: [
      {{ _metadata: {{ types: {{ eq: ""_Page"" }} }} }},
      {{ _metadata: {{ displayName: {{ startsWith: {EscapeString(prefix)} }} }} }}
    ] }}
    limit: {clamped}
  ) {{
    items {{
      _metadata {{ displayName }}
    }}
  }}
}}";

        try
        {
            using var doc = await ExecuteAsync(endpoint, queryDocument, cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>();
            if (TryGetContentBlock(doc, out var content)
                && content.TryGetProperty("items", out var items)
                && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("_metadata", out var meta) || meta.ValueKind != JsonValueKind.Object) continue;
                    var displayName = GetString(meta, "displayName");
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    if (seen.Add(displayName!)) ordered.Add(displayName!);
                }
            }
            // Prefer suggestions whose first token starts with the prefix —
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The autocomplete dropdown is best-effort; never let a Graph
            // hiccup block typing in the search bar.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Representative form of the hits query — uses placeholder substitutions
    /// for the dynamic parts (<c>$phrase</c>, <c>$locale</c>,
    /// <c>$pinnedCollectionId</c>) so the registered channel's admin view
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
        // "asdfasdf" or "burp" still nearest-neighbours into the corpus and
        // returns semantic-only hits with _score ≈ 1.0–2.1 against this
        // tenant's Alloy demo corpus.
        //
        // The trick is that Graph applies a score discount to synonym-
        // replacement hits (`a => b`) — a query that synonym-expands to
        // "alloy" lands in roughly the 4.5–8 band even though a direct
        // "alloy" search lexically scores 30–888. Earlier we tried floor 10
        // to be safe and silently killed every replacement rule. Floor 2.5
        // is the empirically-tuned sweet spot: probe runs (`burp`,
        // `burp burp`, `asdfasdf` vs `floop` synonym vs direct `alloy`)
        // showed it cuts every noise variant to zero while keeping all 26
        // strong synonym hits. Drop down to ~2 if you want a softer floor;
        // bump higher only if you've sampled scores against representative
        // queries and confirmed the synonym band sits clear.
        var orderBy = string.IsNullOrEmpty(phrase)
            ? "orderBy: { StartPublish: DESC }"
            : "orderBy: { _ranking: SEMANTIC, _minimumScore: 2.5 }";
        // usePinned only makes sense when there's a phrase to match against.
        var pinned = !string.IsNullOrEmpty(phrase) && !string.IsNullOrEmpty(pinnedCollectionId)
            ? $"usePinned: {{ phrase: {EscapeString(phrase)}, collectionId: {EscapeString(pinnedCollectionId)} }}"
            : string.Empty;

        // Native highlight on _fulltext — Graph wraps every matched token
        // (lexical AND synonym-expanded) with the start/end markers so the
        // snippet logic can locate the actual matched span without rerunning
        // the match heuristics client-side. We use SOH () / STX
        // () as markers because they:
        //   • don't appear in real content (all corpora are text)
        //   • survive JSON transit and HTML stripping unchanged
        //   • render invisible if a downstream renderer forgets to convert
        //     them, instead of leaking a visible "[GHL]" sentinel.
        // Renderers replace them with <b>/<mark> at the very end.
        var fulltextField = string.IsNullOrEmpty(phrase)
            ? "_fulltext"
            : "_fulltext(highlight: { enabled: true, startToken: \"\\u0001\", endToken: \"\\u0002\" })";

        return $@"
{{
  _Content(
    where: {{ _and: [{{ _metadata: {{ types: {{ eq: ""_Page"" }} }} }}, {clauses}] }}
    limit: {limit}
    {orderBy}
    {pinned}
  ) {{
    total
    items {{
      _metadata {{
        displayName
        types
        locale
        key
        url {{ default }}
      }}
      {fulltextField}
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
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
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

        // CMS 13 nests every metadata facet under `_metadata { ... }` — the
        // legacy CMS 12 shape `facets { ContentType { name count } }` becomes
        // `facets { _metadata { types(...) { name count } } }`.
        var facetBlock = include switch
        {
            FacetField.ContentType => "_metadata { types(limit: 100, orderType: COUNT, orderBy: DESC) { name count } }",
            _ => string.Empty
        };

        // limit: 0 — facet-only query, items not needed.
        return $@"
{{
  _Content(
    where: {{ _and: [{{ _metadata: {{ types: {{ eq: ""_Page"" }} }} }}, {clauses}] }}
    limit: 0
  ) {{
    facets {{
      {facetBlock}
    }}
  }}
}}";
    }

    // Builds the WHERE clause as a `_and: [ ... ]` body — joined by the caller
    // with the always-applied `_metadata: { types: { eq: "_Page" } }` restriction.
    // Returns `{}` (empty object) when no clauses apply, which keeps the
    // composed `_and` valid GraphQL.
    //
    // CMS 13 schema notes (see /workspace/docs/research/ if you need the full
    // map): the type-system fields that CMS 12's Graph exposed at the root of
    // each content item (Name, ContentType, Language, ContentLink) now live
    // under a unified `_metadata` block (displayName, types, locale, key).
    // The where input mirrors that, so all metadata-driven filters route
    // through `_metadata { ... }`.
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
            //
            // Title boost: pages whose displayName matches the phrase get a
            // 5× score contribution on top of the base _fulltext match.
            // Wrapping both in _or keeps the membership rule the same — the
            // doc still has to match _fulltext somewhere — while raising
            // title hits above body-only hits in the ranking. Tune the boost
            // factor up (e.g. 8–10) if titles are being out-ranked by body
            // content; down toward 2–3 if exact-title matches feel sticky.
            //
            // synonyms: ONE on BOTH arms — without it, the title boost only
            // fires when the user types the literal title word, so editorial
            // synonyms (`floop => alloy`) get the _fulltext synonym discount
            // but no title elevation, and synonym hits sink below direct
            // lexical hits.
            inner.Add(
                "{ _or: ["
                + $"{{ _fulltext: {{ match: {EscapeString(phrase)}, synonyms: ONE }} }}, "
                + $"{{ _metadata: {{ displayName: {{ match: {EscapeString(phrase)}, boost: 5, synonyms: ONE }} }} }}"
                + "] }");
        }
        if (includeContentType && types.Count > 0)
        {
            // `_metadata.types` is the inheritance chain as a string list. An
            // `eq` against the leaf type (StandardPage, ProductPage…) matches
            // only documents of that exact type; the umbrella `_Page` is the
            // shared ancestor that the always-on filter restricts to.
            inner.Add($"{{ _metadata: {{ types: {{ in: [{string.Join(", ", types.Select(EscapeString))}] }} }} }}");
        }
        if (includeLocale && !string.IsNullOrEmpty(locale))
        {
            // eq: rather than in: — locale is single-valued (page route serves
            // one branch at a time). Using eq makes the placeholder in the
            // sample query (`$locale`) substitute cleanly without dragging in
            // list syntax.
            inner.Add($"{{ _metadata: {{ locale: {{ eq: {EscapeString(locale)} }} }} }}");
        }
        return inner.Count == 0 ? "{}" : $"{{ _and: [{string.Join(", ", inner)}] }}";
    }

    private async Task<JsonDocument> ExecuteAsync(string endpoint, string queryDocument, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { query = queryDocument }, SerializerOptions);
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
            var meta = item.TryGetProperty("_metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
            hits.Add(new AlloySearchHit
            {
                Title = GetString(meta, "displayName") ?? string.Empty,
                // `_metadata.url` exposes default / hierarchical / internal /
                // graph / base variants. `default` is the routable URL the
                // host would publish for the page.
                Url = GetNestedString(meta, "url", "default") ?? "#",
                Excerpt = BuildExcerpt(GetFulltextSnippet(item), phrase),
                ContentType = GetLeafContentType(meta) ?? "Content",
                Language = GetString(meta, "locale") ?? string.Empty,
                // `key` is the GUID-ish identifier the CMS uses for the content
                // item. Same role the CMS 12 `ContentLink.GuidValue` played.
                ContentLink = GetString(meta, "key") ?? string.Empty
            });
        }
        return hits;
    }

    // Google-style snippet — window around the first highlight marker
    // injected by Graph ( …match… ). Markers stay in the
    // returned string; renderers replace them with <b>/<mark> at emission.
    // Falls back to the leading text when no markers are present (e.g.
    // semantic-only hit where the corpus didn't lexically match).
    private const char HighlightStartMarker = '\u0001';
    private const char HighlightEndMarker = '\u0002';

    private static string BuildExcerpt(string? body, string? _phrase)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        const int windowChars = 220;
        const int leadChars = 60;

        var firstMarker = body.IndexOf(HighlightStartMarker);
        if (firstMarker < 0) return Trim(body, windowChars);

        var start = Math.Max(0, firstMarker - leadChars);
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
    // contributed to the index. With native highlight enabled, only the
    // entries that actually matched the query carry / markers.
    // Pick the first marker-bearing entry so the snippet is meaningful;
    // fall back to the first non-empty entry for the no-marker case.
    private static string? GetFulltextSnippet(JsonElement item)
    {
        if (!item.TryGetProperty("_fulltext", out var ft)) return null;
        string? chosen = null;
        if (ft.ValueKind == JsonValueKind.String)
        {
            chosen = ft.GetString();
        }
        else if (ft.ValueKind == JsonValueKind.Array)
        {
            string? firstNonEmpty = null;
            foreach (var entry in ft.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String) continue;
                var s = entry.GetString();
                if (string.IsNullOrWhiteSpace(s)) continue;
                firstNonEmpty ??= s;
                if (s.IndexOf(HighlightStartMarker) >= 0) { chosen = s; break; }
            }
            chosen ??= firstNonEmpty;
        }
        if (string.IsNullOrWhiteSpace(chosen)) return null;
        // Strip HTML tags (rich text fields land here as <p>…</p>) and
        // collapse runs of whitespace. Markers (/) survive both.
        var stripped = System.Text.RegularExpressions.Regex.Replace(chosen, "<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(stripped);
        var collapsed = System.Text.RegularExpressions.Regex.Replace(decoded, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(collapsed) ? null : collapsed;
    }

    private static Dictionary<string, int> ParseFacetCounts(JsonDocument doc, string facetName)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!TryGetContentBlock(doc, out var content)) return result;
        if (!content.TryGetProperty("facets", out var facets) || facets.ValueKind != JsonValueKind.Object) return result;
        // CMS 13 nests every type-system facet under `_metadata`. Walk through
        // it so callers can still ask for the facet by its conceptual name
        // (`types`) without spelling out the path.
        if (!facets.TryGetProperty("_metadata", out var metaFacets) || metaFacets.ValueKind != JsonValueKind.Object) return result;
        if (!metaFacets.TryGetProperty(facetName, out var facet) || facet.ValueKind != JsonValueKind.Array) return result;

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
        // CMS 13's content-graph schema renamed the root collection field
        // from `Content` to `_Content`. The CMS 12 sample still uses `Content`,
        // but here we read the new name.
        if (!data.TryGetProperty("_Content", out content) || content.ValueKind != JsonValueKind.Object) return false;
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
        return selection
            .Where(raw => !string.IsNullOrWhiteSpace(raw))
            .Select(raw => raw.Trim())
            .Where(t => whitelist == null || whitelist.Contains(t, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? GetString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object
           && el.TryGetProperty(name, out var p)
           && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string? GetNestedString(JsonElement el, string a, string b)
        => el.TryGetProperty(a, out var inner) && inner.ValueKind == JsonValueKind.Object ? GetString(inner, b) : null;

    // CMS 13's `_metadata.types` array carries the full inheritance chain.
    // Framework-introduced base names are underscore-prefixed (_Content, _Page,
    // _Image, _Block, _Item, _Component, _Folder, _Media). Drop those plus the
    // host's framework-level umbrellas so we surface the concrete leaf type
    // (StandardPage, ArticlePage, …) — the value users actually filter by.
    private static readonly HashSet<string> GenericContentTypes = new(StringComparer.Ordinal)
    {
        "_Content", "_Page", "_Block", "_Media", "_Image", "_Item",
        "_Component", "_Folder", "_AssetItem", "_ImageItem"
    };

    private static string? GetLeafContentType(JsonElement meta)
    {
        if (meta.ValueKind != JsonValueKind.Object) return null;
        if (!meta.TryGetProperty("types", out var ct) || ct.ValueKind != JsonValueKind.Array) return null;
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

    /// <summary>
    /// Content GUID for the hit, surfaced from <c>ContentLink.GuidValue</c>
    /// in the Graph projection. Used by the SERP click beacon to attribute
    /// click-through telemetry to a specific content item, so the Search Logs
    /// CTR rollup can compare clicks against impressions per phrase.
    /// </summary>
    public string ContentLink { get; init; } = string.Empty;
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
