# Aurora UI Refactor — Implementation Plan

A phased plan to replace the inline-editable Pinned and Synonyms grids (and the
Profile detail surface that hosts them) with the Optimizely-Aurora-style pattern:
summary rows + flyout editing + sticky live preview. Existing C# APIs stay; the
change is mostly Razor views, JS, CSS, plus one upstream pagination fix.

The visual reference is the CMS Admin SPA — e.g. `EPiServer/EPiServer.Cms.UI.Admin/default#/ContentTypes`:
Inter typography, brand-blue primary, flat grids with blue-link summary rows,
`⋯` overflow per row, label-above-value filter dropdowns.

The new edit pattern is a right-edge **flyout** (~480 px panel) — pins are
5–6 fields and synonyms 2–3, both fit comfortably. Deep-linking is by query
parameter; a future detail page can replace the flyout if entities grow.

---

## What's already in `feature/search-telemetry`

These commits land the foundation and can be reviewed independently of the
phased work below:

- **Aurora design tokens** in `graphsearchtools.css`: Inter font (Google Fonts),
  brand blue `#0042FF`, lighter neutrals, larger light-weight page titles,
  flat surfaces.
- **`?` help icon** at the top-right of every main page header (8 views).
- **`.gst-filter` label-above-value dropdown wrapper**, adopted by Profiles toolbar.
- **Foundation CSS** for `.gst-flyout`, `.gst-field`, `.gst-target-list`,
  `.gst-rowmenu` — not yet referenced outside the prototype branch.

A working prototype lives at `/EPiServer/GraphSearchtools/GraphSearchtools/Prototype`
(uncommitted on this branch — `Views/Prototype/Aurora.cshtml` + a `Prototype()`
controller action). It demonstrates all four target surfaces (Pins, Synonyms,
Profile › Pinned, Profile › Synonyms) with mock data, the flyout (single- and
multi-target pin variants), drag-reorder, cross-profile conflict warning, and
the 50/50 workspace with row-driven preview updates.

---

## Decisions baked in (from review)

1. **No feature flag.** Straight cutover; the addon isn't in production yet.
2. **Load all pins upfront** on tab open. Walk the upstream offset pagination
   into a single client-side dataset, so filter / sort / search / paging are
   all client-side with real totals.
3. **Insights first**, with the **live-preview pane defaulting to the most-
   searched phrase from the last 7 days** for the active profile.
4. **Inline editing is removed.** Flyouts are the only edit path. The old
   editor JS and CSS get deleted, not coexisted.

---

## Phases

### Phase 1 — Flyout component (1 day)

**Why first:** Phases 3–5 depend on it.

- Promote `.gst-flyout`, `.gst-target-list`, and form-field CSS from
  inline `<style>` in the prototype into `graphsearchtools.css` proper.
- Add `GST.flyout` helper in `components.js`:
  `open(key, opts)` / `close(key)`, backdrop-click + `Esc` dismiss,
  focus trap, `lastFocus` restore on close.
- Build `Views/Shared/_PinFlyout.cshtml` and `_SynonymFlyout.cshtml` as
  Razor partials so the global and profile-detail pages share the same
  markup.

**Touched:** `components.js`, `graphsearchtools.css`, two new partials.

---

### Phase 2 — Server-side pagination + bulk loader (1 day)

**Why now:** Without this, the new grid still hits ContentGraph's hard 20-item
cap when reading.

- `GraphAdminClient.GetItemsAsync(collectionId, offset, ct)` forwards
  `?offset=N` to ContentGraph (verified to work; default 20-item window,
  no other paging params honoured).
- Service helper `LoadAllItemsAsync(collectionId, ct)` walks pages until a
  short page is returned. Safety cap (~5000) so a runaway tenant can't
  exhaust memory; honest 503 if exceeded.
- New `[FromQuery] int offset` parameter on `PinnedApiController.Items`,
  plus a `PinnedApiController.AllItems` action that returns the full
  walked list with a real total.
- Reuse for any other Graph admin endpoints that exhibit the same cap.

**Touched:** `GraphAdminClient.cs`, `PinnedService.cs`, `PinnedApiController.cs`,
new unit tests for the offset boundary.

---

### Phase 3 — Insights tool & Profile › Insights (3–4 days)

The biggest single phase, because Insights doesn't exist today as a discrete
surface.

#### A. Global Insights (new top-level menu entry)

- Add a `Tools/Insights/` folder with `InsightsController`, `InsightsService`,
  `InsightsApiController`, and `Views/Insights/Index.cshtml`.
- Register a menu item in `GraphSearchtoolsMenuProvider` between Profiles
  and Pinned.
- Surfaces:
  - **Top phrases** with a 7d / 30d toggle (count, last-seen).
  - **Top zero-result phrases** — suggested-pin candidates.
  - **Synonym coverage rollup** — % of queries that triggered at least one
    synonym rule.
  - **Last-edited activity strip** — recent `SearchProfileEdit` entries.
- No live-preview pane on the global view (no profile scope).

#### B. Profile › Insights sub-tab

- Same data, scoped to one profile's locales.
- **This is where the live preview lives:**
  - On tab open, the page fetches `topPhrases: [{ phrase, count, locale }]`
    for the profile over the last 7 days.
  - The preview pane auto-fills its query field with `topPhrases[0].phrase`
    and renders the SERP immediately — the marketer lands on "what people
    actually search for, and what they get back."
  - Clicking another phrase in the Insights panel re-drives the preview.
  - In Phases 4/5, the row-driven behaviour from the Pinned/Synonyms
    sub-tabs stacks on top: selecting a row drives the preview; clearing
    selection snaps it back to the 7d-top default.

#### C. Profile-detail shell refactor

- Breadcrumb header + stat row + Aurora sub-tabs:
  `Insights · Pinned · Synonyms · Activity · Settings`.
- Insights sub-tab is functional in this phase. Pinned and Synonyms sub-tabs
  **temporarily** render the existing inline editor inside the new shell
  until Phases 4/5 swap them out.

#### API additions

- `GET /InsightsApi/TopPhrases?profileKey=&days=7` →
  `[{ phrase, count, locale, zeroResults }]`. Backed by existing
  `SearchLogsService` aggregations; no new DB schema.

**Open question for Phase 3 kick-off:** what does the 7d-top preview show on a
fresh install with no logged searches yet — empty state, alphabetically-first
pinned phrase, or a hardcoded placeholder?

---

### Phase 4 — Aurora Pinned grid + flyout (3–4 days)

Replace `gst-pinedit__grid` and its inline editors with the summary grid +
PinFlyout.

- **Markup:** rewrite the "Pins" panel in `Views/Pinned/Index.cshtml` as the
  Aurora grid (phrase, profile, locale, pinned items, state, modified, ⋯).
- **JS:** new `pinned-aurora.js` that:
  - Aggregates ContentGraph rows by `(phrase, profile, locale)` so the grid
    shows one row per phrase with an "N items" count.
  - Wires row-click → `GST.flyout.open('pin', { row })`.
  - Renders the multi-target list with HTML5 drag-reorder + add + remove
    inside the flyout.
  - On save, computes the diff and emits N calls: `CreateItem` /
    `UpdateItem` / `DeleteItem` / priority renumber. Shows a single
    progress + error rollup.
- **Conflict detection:** before save, search other profiles for the same
  phrase + locale; show the warning bar inside the flyout. Scope the check
  to profiles that share at least one locale (so it scales).
- **Content picker:** wire `GST.contentPicker` so the picker button in the
  flyout opens it.
- Keep the existing **Audit** tab as-is (it's coverage-oriented, not editing).
- Decommission `pinned.js` (~2,000 lines of inline-editor logic).
- **Profile › Pinned** sub-tab now renders the Aurora grid scoped to the
  profile (filter and column for Profile hidden).

**Touched:** `Pinned/Index.cshtml`, new `pinned-aurora.js`, deprecate
`pinned.js`, share helpers with `synonyms-grid.js` where reasonable.

---

### Phase 5 — Aurora Synonyms grid + flyout (2–3 days)

Smaller scope than Pinned — synonyms are a simpler entity.

- **Markup:** rewrite the "Rules" panel in `Views/Synonyms/Index.cshtml` as
  the Aurora grid (rule, type, locale, coverage, state, ⋯).
- **JS:** new `synonyms-aurora.js`. Group equivalent vs. replacement rules
  by parsing the rule string. Row-click → `GST.flyout.open('synonym', { row })`.
- **Inline parse validation** inside the flyout: as the user types, parse
  and show `Equivalent: 3 terms` / `Replacement: a → b` / `Invalid: missing
  operator` below the input.
- Keep the existing **Unused** tab as-is.
- **Profile › Synonyms** sub-tab renders the Aurora grid scoped to the
  profile's locales, with the existing "synonyms are global" info banner.

**Touched:** `Synonyms/Index.cshtml`, new `synonyms-aurora.js`, deprecate
`synonyms-grid.js`.

---

### Phase 6 — Cleanup & docs (0.5 day)

Without a feature flag, the cleanup pass is chunky:

- Remove `pinned.js` (2,027 lines) and `synonyms-grid.js` (737 lines).
- Remove inline-editor CSS for `gst-pinedit__grid`, `gst-syn-page`,
  `gst-prof-switcher` (~1,500 lines in `graphsearchtools.css`).
- Remove `Views/Prototype/Aurora.cshtml` + `Prototype()` controller action.
- Remove obsolete localization keys (diff new strings against `lang/en.xml`).
- Refresh screenshots in `docs/personas.md` and the implementation-plan.

Worth doing in its own PR after Phases 3–5 land so the diff is clean.

---

## Risks & open questions

1. **Upstream pagination has no total-count** — Phase 2's bulk loader walks
   the full collection on tab open, which gives us real totals at the cost
   of N round-trips. For tenants with >500 pins per collection this could
   take a few seconds; surface a progress hint.
2. **Multi-target save is N+1 calls** — no batch API today. For a 5-target
   pin that's worst-case 5 PUTs. Acceptable for v1.
3. **Drag-to-reorder is desktop-only** — HTML5 drag doesn't fire on touch.
   Tablet/touch needs explicit ↑/↓ buttons or a pointer-events polyfill.
   Recommend: desktop drag in v1, touch fallback in a follow-up.
4. **Cross-profile conflict scope** — global scan is O(profiles × pins).
   Mitigation: filter to profiles sharing at least one locale, lazy-load
   on flyout open.
5. **Synonyms are global** — the Profile › Synonyms info banner must make
   clear that editing a rule there updates it everywhere, not just for
   this profile. Already in the prototype copy.

## Estimate

| Phase | Effort | Ships independently? |
|---|---|---|
| 1. Flyout component | 1 d | Yes (lib only) |
| 2. Pagination + bulk loader | 1 d | Yes (backend) |
| 3. Insights + Profile detail shell | 3–4 d | Yes |
| 4. Aurora Pinned | 3–4 d | Yes |
| 5. Aurora Synonyms | 2–3 d | Yes |
| 6. Cleanup | 0.5 d | After 3–5 |
| **Total** | **10–13 d** | |

---

## Out of scope for this refactor

- A standalone detail page per pin/rule — the flyout covers our entities
  today. Revisit if pins gain audit history or rich preview features that
  outgrow the 480 px panel.
- Bulk operations (multi-select disable, bulk delete) — surface in the
  toolbar via row checkboxes; not in this initial cutover.
- Touch-input drag-to-reorder (see Risk 3).
- A new batch API on ContentGraph for multi-target writes (see Risk 2).
