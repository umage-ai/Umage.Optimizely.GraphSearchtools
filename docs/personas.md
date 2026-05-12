# Personas

Who we're designing GraphSearchtools for. Update this when reality contradicts the
sketch — these are guides, not rules.

When building UI, design for the **primary persona** by default. Mention the secondary
personas only when the feature is clearly aimed at them.

---

## Primary: The Marketer

**Role.** Works in marketing — campaign manager, merchandiser, content strategist, or
similar. Not a developer. Owns the commercial outcome of what shoppers/readers see
when they search the site.

**Measured on.** Marketing KPIs: conversion rate, engagement, click-through, revenue
per session, campaign performance. Search quality matters to them only insofar as it
moves those numbers.

**Where they live day-to-day.**
- Optimizely CMS / Commerce editing UI (pages, products, campaigns, promotions).
- Analytics tools (GA, Optimizely Data Platform, internal BI dashboards).
- Planning tools (calendar/roadmap apps, Jira/Asana, spreadsheets).
- Email/Slack — coordinating with merchandising, content, and SEO peers.

**Why they open GraphSearchtools.**
- "Black Friday is next week — make sure the Air Max line shows up first when shoppers
  search 'sneakers'." → Pinned results.
- "Customers keep searching 'jumper' but we call them 'sweaters' — fix the zero results."
  → Synonyms.
- "Our top search term converts terribly. Why?" → Profile Insights / telemetry.
- "Marketing director is asking why search feels slow today." → Health (glance, then
  forward to IT if there's a problem).

**What they care about in our UI.**
- **Speak business, not Graph.** "Pinned result for query 'sneakers'", not "boost
  document by ID in semantic profile". GraphQL, scoring functions, vector embeddings,
  and index internals should be hidden or behind a clear "advanced" affordance.
- **Fast confirmation that a change took effect.** They want to see the search result
  page reorder, not parse a JSON response.
- **Safe defaults.** They're nervous about breaking search for the whole site. Changes
  should feel scoped, reversible, and previewable.
- **Connection to the work they were already doing.** Pinning a product they're
  launching, fixing a term that came up in a meeting, responding to a dashboard.

**What they explicitly do NOT care about.**
- How Optimizely Graph works internally.
- Query syntax, GraphQL playgrounds, raw response payloads.
- Index lifecycle, replication, sync schedules, infrastructure health beyond
  "is it up?".
- Editing config files, environment variables, or anything that smells like ops.

**Availability & performance.** They notice it, they'll complain about it, but they're
not on the hook to fix it. Surface it at a glance (a status pill, a "search is healthy"
indicator). Don't make them interpret latency histograms — that's a developer job.

**Vocabulary they expect.** Search term, result, pin, synonym, campaign, category,
product, page, conversion. Not: document, edge, hit, shard, profile, boost, weight,
semantic vector.

**Implications for design.**
- Default landing surface should answer "what's happening with my search today?" in
  marketing terms, not engineering terms.
- Per-tool views should lead with the *task* (pin a result, fix a zero-hit query),
  not the *object* (a pin record, a synonym row).
- Advanced/technical affordances should exist (we have developer users too) but
  should not be the primary call to action.
- Errors and empty states should suggest the next marketing-meaningful action,
  not display a stack trace or Graph error code.

---

## Secondary personas

Stubs — flesh these out when we have a real feature aimed at them.

- **The Developer / Solution Architect.** Sets up the integration, configures
  profiles, debugs why pin X isn't ranking, owns relevancy tuning. Comfortable with
  GraphQL and the Graph schema. Cares about correctness and observability over
  marketing language.
- **The Site Owner / Ops.** Watches Health, gets paged when Graph is down or slow.
  Doesn't edit pins or synonyms. Wants latency, error rate, and quota at a glance,
  and a clear escalation path.
