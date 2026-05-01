# Optimizely Graph — Search Relevancy Optimization Playbook

A practical guide to tuning result quality in Optimizely Graph. Each lever is described with its mechanism, GraphQL syntax, when it helps, and when it backfires.

The relevancy levers, ordered by how broadly they apply:

1. Choose the right operator (`match` over `contains`)
2. Boost important fields per query
3. Blend semantic ranking via `_semanticWeight`
4. Apply Gaussian decay for recency
5. Use `factor` to fold in numeric engagement signals
6. Curate vocabulary with synonyms
7. Curate intent with pinned results
8. Cut tail noise with `_minimumScore`
9. Iterate using `_score` and `_fulltext` as debugging windows

---

## 1. Operator choice: `match` is the default

`match` does proper full-text matching with relevance scoring. `contains` is a substring filter and does not produce ranked output.

```graphql
# Good — relevance-ranked
where: { _fulltext: { match: $q } }

# Bad for site search — substring filter, no ranking
where: { Name: { contains: $q } }
```

Reach for `contains` only for filter-box behaviour ("show items whose SKU contains 'A1B'"), not for end-user search.

## 2. Per-field, per-clause boosting

Boost is an integer (≥ 0) on any clause inside `where`. Use it to encode editorial priority across fields:

```graphql
where: {
  _or: [
    { Name:        { match: $q, boost: 10 } }
    { Heading:     { match: $q, boost: 5 } }
    { TeaserText:  { match: $q, boost: 3 } }
    { MainBody:    { match: $q } }
    { Tags:        { match: $q, boost: 4 } }
  ]
}
```

**Rules of thumb**

- Title/H1 is almost always the strongest field; weight it 5–10× the body.
- Tags and structured taxonomy beat free-text body for short, intent-driven queries.
- Don't over-boost — once one field dominates, the engine effectively ignores the others. A 3-5× spread between top and bottom field is usually plenty.
- Boost can also be applied to `_or` branches that match a content type, to lift one type over another (e.g. Products over Articles for commerce queries).

### `BOOST_ONLY` mode

`orderBy: { _ranking: BOOST_ONLY }` scores only the boosted clauses; everything else is index order. Useful for "feature these without disturbing the rest" pages.

## 3. Semantic ranking with `_semanticWeight`

Semantic ranking uses pre-trained language models to capture intent and synonymy automatically. It is the lowest-effort way to handle vocabulary mismatch.

```graphql
orderBy: { _ranking: SEMANTIC, _semanticWeight: 0.4 }
where:   { _fulltext: { match: $q } }
```

- Default `_semanticWeight` is `0.2`. A typical safe range is `0.2 – 0.6`.
- Increase weight for **conversational** queries ("how do I return a faulty lamp") or **discovery** queries ("a sofa for a small living room").
- Decrease (or stick to RELEVANCE) for **navigational** queries (SKU, exact product name, person name) where exact-keyword wins.
- Negative values disable semantic blending.
- Test per query class — naive global increases frequently regress head-term queries.

### Tiered ranking pattern

Run RELEVANCE for short queries (≤ 2 tokens) and SEMANTIC blended for longer queries. Easy to implement client-side based on input length or detected query class.

## 4. Recency with Gaussian decay

For news, blog, and event-style content, recency is part of relevance. `decay` on a `Datetime` field applies a Gaussian curve.

```graphql
where: {
  _fulltext: { match: $q }
  PublishedDate: {
    decay: { origin: "now", scale: 30, rate: 0.5 }
  }
}
```

- `origin` — reference date (default `now()`).
- `scale` — distance in days at which score is multiplied by `rate`. Default `1000` is too soft for news; use `7–30` for press releases, `90–365` for evergreen content.
- `rate` — score at the scale boundary (default `0.5`). Lower = sharper falloff.

Combine with field boosts so that recency is a multiplier on top of textual relevance, not a replacement.

## 5. Engagement signals with `factor`

Numeric fields (clicks, sales, inventory, ratings) can be folded into the score via `factor`:

```graphql
where: {
  _fulltext: { match: $q }
  NumClicks: { gt: 0, factor: { value: 10, modifier: SQRT } }
}
```

`modifier` choices:

- `SQRT` — diminishing returns; the canonical choice for click counts.
- `LOG` — even more aggressive flattening for very long-tail distributions.
- `RECIPROCAL` — penalise high values (use rarely, e.g. for inventory levels you want to push down).
- `SQUARE` — amplify differences. Use only with bounded, normalised values (e.g. 0–1 quality score).
- `NONE` — raw multiplication.

**Watch out for feedback loops.** Boosting clicked content makes that content more likely to be clicked, baking in early traffic patterns. Counter with: a cap (`gt`/`lt`), a recency-weighted click counter, or by using popularity only as a tiebreaker.

## 6. Synonyms — solve vocabulary mismatch deterministically

Synonyms are an explicit dictionary: "TV → television", "sneaker → trainer → running shoe". Compared to semantic search, synonyms are:

- **Predictable** — you control exactly which equivalences exist.
- **Auditable** — easy to explain why a result appeared.
- **Maintenance-heavy** — they require ongoing curation against search logs.

Use synonyms when:

- Domain vocabulary differs from user vocabulary (industry jargon, brand vs generic names).
- Acronyms and abbreviations are common.
- You need *guarantees*, not the probabilistic match of a language model.

Use semantic search instead when:

- The corpus is large, the vocabulary unbounded, and curation is impractical.
- Queries are conversational rather than keyword.

In practice, **use both**: synonyms for high-traffic head terms with known mismatch, semantic for the long tail.

### Sourcing the synonym list

Mine your search logs for:

- High-volume queries with low click-through.
- Zero-result queries.
- Pairs of queries from the same session that look like rephrasings.

Manage via Graph admin APIs or the third-party [`OptiGraphExtensions`](https://github.com/adayinthelifeofapro/OptiGraphExtensions) Blazor add-on (CMS 12).

## 7. Pinned results — editorial override

Pinned results lock specific content to specific queries. They are the right tool when:

- A campaign needs guaranteed top placement during a window.
- Compliance or legal pages must surface for trigger phrases ("safety", "recall", "warranty").
- A new launch needs prominence before it has accumulated relevance signals.

Pinning supports time-bounding (start/end dates) and overlap precedence rules.

**Anti-patterns** (per Optimizely OMVP guidance):

- Pinning everything — destroys signal and frustrates users when the pinned item doesn't match intent.
- Pinning without monitoring — track click-through on pinned vs organic; if pinned CTR is lower, you've made search worse.
- Using pins as a substitute for fixing relevancy — if your "best" page consistently fails to rank, the underlying signal is wrong (boosts, synonyms, or content itself), not the SERP.

Pin sparingly: a few dozen high-value queries, reviewed quarterly.

## 8. `_minimumScore` — cut the long tail

Once you've done relevancy work, set a minimum score to prevent obviously-poor matches from being shown:

```graphql
orderBy: { _minimumScore: 1.5 }
```

- Tune by sampling: log `_score` for top and bottom of result sets across realistic queries, then set the threshold below the lowest acceptable result.
- Different thresholds for landing-page (high) vs deep-results page (low) is a known pattern.
- Triggers relevance ranking automatically — don't combine with `DOC` ordering.

## 9. Debugging with `_score` and `_fulltext`

Always project `_score` (and optionally `_fulltext`) in dev environments:

```graphql
items {
  Name
  _score
  _fulltext
}
```

- Tells you whether a tweak actually moved the needle.
- Surfaces which field a query matched on (via `_fulltext` snippet).
- Lets you reason about boost ratios without guessing.

## 10. A staged tuning approach

Recommended order of operations when you start a relevancy project:

1. **Instrument first.** Log queries, zero-result rate, top-N click positions, and `_score`. Without metrics you cannot tell if changes are improvements.
2. **Fix obvious data issues.** Missing titles, empty metadata, content marked unsearchable by mistake. No boost will rescue thin content.
3. **Set field boosts.** Title > headings > tags > teaser > body, with a 3-5× spread.
4. **Add semantic blend at low weight (0.2).** Measure head vs tail behaviour separately.
5. **Layer recency decay** for time-sensitive content types (only those types).
6. **Layer engagement factors** carefully, with caps to avoid runaway feedback.
7. **Curate synonyms** for the top zero-result and low-CTR head queries.
8. **Pin results** only for the handful of queries where editorial control is genuinely needed.
9. **Apply `_minimumScore`** as a final tail-cut.
10. **Re-measure.** Then iterate.

## 11. Quick reference: a tuned site-search query

```graphql
query SiteSearch($q: String!, $skip: Int, $take: Int) {
  Content(
    locale: en
    skip: $skip
    limit: $take
    where: {
      _or: [
        { Name:       { match: $q, boost: 10 } }
        { Heading:    { match: $q, boost: 5 } }
        { Tags:       { match: $q, boost: 4 } }
        { TeaserText: { match: $q, boost: 3 } }
        { MainBody:   { match: $q } }
      ]
      PublishedDate: { decay: { scale: 365, rate: 0.5 } }
      NumClicks:     { gt: 0, factor: { value: 5, modifier: SQRT } }
    }
    orderBy: {
      _ranking: SEMANTIC
      _semanticWeight: 0.3
      _minimumScore: 1.0
    }
  ) {
    total
    items {
      Name
      RelativePath
      TeaserText
      _score
      _fulltext
    }
    facets {
      ContentType { name count }
      Tags(limit: 20) { name count }
      PublishedDate(unit: DAY, value: 30) { name count }
    }
  }
}
```

This is a starting point — every value above (`boost`, `scale`, `_semanticWeight`, `_minimumScore`) should be tuned against your own logs and click data.
