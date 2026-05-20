# Optimizely Graph — Research

Notes on Optimizely Graph as a site-search backend, with a focus on relevancy tuning.
Research conducted 2026-04-30 against `docs.developers.optimizely.com` and adjacent community sources.

## Documents

- [optimizely-graph-site-search.md](./optimizely-graph-site-search.md) — Capabilities reference: search types, GraphQL surface, faceting, autocomplete, semantic search.
- [relevancy-optimization.md](./relevancy-optimization.md) — Playbook for tuning result quality: boosting, decay, synonyms, pinned results, semantic weight, score thresholds.
- [graph-authentication.md](./graph-authentication.md) — How AppKey/Secret + SingleKey work together against the CMS.

## TL;DR

Optimizely Graph is "search-as-a-service" exposed over GraphQL. Out of the box it gives you BM25 keyword ranking, AI-powered semantic ranking, faceting, autocomplete, and per-field/per-clause boosting. The main relevancy levers are:

1. **Match operator with multi-field boosts** — e.g. boost `Name` over `MainBody`.
2. **`_ranking: SEMANTIC` with `_semanticWeight`** — blend keyword and vector relevance.
3. **Gaussian decay on dates** — favour recent content.
4. **`factor` modifier on numeric fields** — incorporate engagement signals (clicks, inventory).
5. **Synonyms and pinned results** — manageable via the third-party `OptiGraphExtensions` add-on for CMS 12 (no first-party editor UI for these as of writing).
6. **`_minimumScore`** — cut off low-quality long-tail matches.
