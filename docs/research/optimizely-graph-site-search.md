# Optimizely Graph as a Site-Search Tool — Capabilities Reference

Optimizely Graph is Optimizely's content indexing and querying service, exposed over a GraphQL API. Content from CMS, Commerce, and custom sources is pushed into the index, and any client (frontend, head, mobile, AI agent) can query it directly. Optimizely positions it as **search-as-a-service**, meaning the search engine, query language, and relevancy machinery are managed for you — there is no Elasticsearch cluster to operate.

## 1. Search types supported

| Type | Operator / setting | Use case |
| --- | --- | --- |
| **Filtering** | `where: { Field: { eq / in / gt / startsWith / ... } }` | Strict matching: status, category, tag |
| **Full-text search** | `match`, `contains` on searchable string fields, plus `_fulltext` | The default for site search input |
| **Faceted search** | `facets { ... }` sibling of `items` | Refinement UIs, filters with counts |
| **Semantic / vector search** | `orderBy: { _ranking: SEMANTIC }` | Natural-language queries, intent matching |
| **Autocomplete / type-ahead** | root-level `autocomplete` field | Search-as-you-type suggestions |

`match` is the recommended default for site-search input boxes: it returns more relevant results than `contains`, and integrates with the relevance score (`_score`).

## 2. The query surface

A site-search query typically looks like:

```graphql
query SiteSearch($q: String!) {
  Content(
    locale: en
    limit: 20
    where: {
      _or: [
        { Name:       { match: $q, boost: 5 } }
        { TeaserText: { match: $q, boost: 3 } }
        { MainBody:   { match: $q } }
      ]
    }
    orderBy: { _ranking: RELEVANCE }
  ) {
    total
    items {
      Name
      TeaserText
      RelativePath
      _score
      _fulltext
    }
    facets {
      ContentType { name count }
    }
  }
}
```

Key magic fields:

- `_fulltext` — concatenation of searchable fields; used for free-text search and returnable as a snippet.
- `_score` — relevance score for the item; useful for debugging and for cutoffs.
- `_ranking` — `RELEVANCE` (default, BM25), `SEMANTIC` (AI), `BOOST_ONLY`, or `DOC` (index order).
- `_typeName` — content-type discriminator for filtering.
- `_minimumScore` — score threshold; when applied, relevance ranking activates automatically.

## 3. Full-text search details

- **Algorithm**: BM25 / TF-IDF for `_ranking: RELEVANCE`. Fast, predictable, exact-keyword-friendly.
- **Multi-field**: combine fields with `_or` and per-field `boost` integers (non-negative).
- **Searchable vs filterable**: only fields marked searchable accept `match`; filterable fields accept `eq`, `in`, etc.
- **Field-length caveat**: do not sort on values longer than 1024 characters — make those searchable instead.

## 4. Faceted search

Facets sit alongside `items` and aggregate the result set into buckets:

```graphql
facets {
  ContentType(limit: 10, orderType: COUNT, orderBy: DESC) { name count }
  PublishedDate(unit: DAY, value: 30) { name count }      # date histogram
  Price(ranges: [{ to: 100 }, { from: 100, to: 500 }, { from: 500 }]) { name count }
}
```

- **String / Bool**: `orderType: COUNT|VALUE`, `orderBy`, `limit` (max 1000), `filters`.
- **Date**: histogram bucketing by `MINUTE | HOUR | DAY` × `value`.
- **Number**: either categorical (counts per value) or ranges with `from`/`to`.
- **Multi-select pattern**: pass selected facet values back via the per-facet `filters` parameter; original facet list is preserved with updated counts (the standard "Amazon-style" multi-select).
- **Performance**: if you only need facets, set the items `limit: 0`.

## 5. Autocomplete

Root-level `autocomplete` field, parallel to `facets`:

```graphql
{
  autocomplete(value: "opti", limit: 10) {
    Name
  }
}
```

- Works on `StringFilterInput` fields (including nested, e.g. `ContentLink.GuidValue`).
- Each typed word capped at 10 characters; longer words return no suggestions.
- Punctuation and HTML tags are ignored during matching.
- Can be combined with `where` to restrict suggestions to a result set, and with `facets` to drive faceted suggestions.

## 6. Semantic / vector search

Enabled by setting `orderBy: { _ranking: SEMANTIC }`. Solves the **vocabulary mismatch problem** — e.g. a query of "non-alcoholic cold beverage" can return content tagged as "cola".

- Uses pre-trained language models; multilingual support across 24+ languages including English, Spanish, French, German, Chinese, Japanese, Korean, Arabic, with explicit CJK handling.
- Combines with keyword scoring; the blend is controlled by `_semanticWeight` (default `0.2`, positive float; negative disables semantic).
- Suitable for natural-language questions, RAG pipelines for chatbots, and conceptual queries.
- Can be mixed with traditional sort (e.g. sort by date asc, semantic as tiebreaker).

```graphql
orderBy: { _ranking: SEMANTIC, _semanticWeight: 0.5 }
```

## 7. Boosting & relevance customisation

Three boost mechanisms (covered in depth in `relevancy-optimization.md`):

1. **Static `boost: <int>`** on any clause inside `where`.
2. **Gaussian `decay`** on `Datetime` fields with `origin`, `scale` (days, default 1000), `rate` (default 0.5).
3. **`factor`** on numeric fields with `value` and `modifier` (`NONE | SQUARE | SQRT | LOG | RECIPROCAL`).

## 8. orderBy beyond ranking

```graphql
orderBy: { _ranking: RELEVANCE, PublishedDate: DESC }
```

- Multiple criteria are tiered tiebreakers, evaluated in declaration order.
- `BOOST_ONLY` mode scores only by boosted clauses, leaving the rest in index order — useful for "promote these, leave everything else untouched" use cases.
- `DOC` is recommended with cursor-style pagination because it's stable.
- `_minimumScore` lets you build "top results vs. all results" patterns (high-bar landing page, lower-bar "show more" page).

## 9. Editorial controls: synonyms & pinned results

Optimizely Graph supports synonyms and pinned results at the platform level, but **as of 2026-04-30 there is no first-party editor UI** to manage them. Two practical options:

1. **REST/Graph admin APIs** — manage synonyms and pinned results programmatically (CI/CD, scripts).
2. **`OptiGraphExtensions` community add-on** ([github.com/adayinthelifeofapro/OptiGraphExtensions](https://github.com/adayinthelifeofapro/OptiGraphExtensions)) — Blazor-based admin UI for Optimizely CMS 12 that adds editor-facing management of:
   - Synonyms (with multiple groups / languages / slots)
   - Pinned results (collections mapped to phrases, with content autocomplete)
   - Webhooks, saved queries, request logs, custom data sources
   - Requires .NET 8, CMS 12, SQL Server; access gated to `CmsAdmins`/`Administrator`/`WebAdmins`.
   - Limitation: webhook edits require recreate (Graph API constraint).

## 10. CMS SaaS specifics

For CMS SaaS, search is implemented via Optimizely Graph by default (no separate Search & Navigation product). Optimizely provides a [migration guide](https://docs.developers.optimizely.com/platform-optimizely/docs/feature-migration) for teams moving from classic Search & Navigation, mapping the old feature set to Graph equivalents (e.g. boosted fields, synonyms, faceting). A new **Optimizely CMS 13 Graph SDK** was announced in March 2026 that streamlines client-side query construction and content-type binding.

## 11. What Graph does *not* do (or does opaquely)

- Custom analyzers / tokenizers — you cannot swap in your own Lucene analyzer; tokenisation is service-managed.
- Per-field language overrides — semantic models pick language automatically; explicit per-field language tagging is limited.
- Re-ranking with custom ML models — boosting is the supported extension point; you cannot inject a learned-to-rank model into the engine itself. (You can re-rank client-side after retrieving top-K.)
- Detailed visibility into the synonym graph or stop-word list — these are managed by the platform.
