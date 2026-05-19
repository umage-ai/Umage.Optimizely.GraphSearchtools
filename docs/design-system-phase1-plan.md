# Design system — Phase 1 plan

Status: **proposed, not started**. Branch `feature/design-review`. This doc
captures the scope, design decisions, and deferred work so the
implementation PR can be drafted independently. Delete this doc after Phase
1 lands.

The companion reference for *why* these choices is
[`design-system-methodologies.md`](./design-system-methodologies.md);
the current catalogue is [`design-system.md`](./design-system.md).

---

## Why now

A code-level audit against the design-system catalogue found that the
doc and the code have drifted in five recurring ways. The full audit
isn't repeated here — only the items Phase 1 acts on:

1. `GST.alert(msg, level)` is documented at `design-system.md:149` but **does
   not exist**. Five files hand-roll the same `el.className = 'gst-alert
   gst-alert--…'` boilerplate. One callsite (`pinned-collections.js:132`)
   ships `gst-alert--error`, a class that doesn't exist in the stylesheet.
2. The Channels index renders a bespoke `.gst-prof-table` (built imperatively
   in `channels.js:150-187`) outside the Aurora pattern. The class name
   itself is a leftover from the Profiles→Channels rename.
3. Tab strip drift is now **four-way**: `.gst-tabs__btn` (Pinned, Synonyms,
   Changelog), `.gst-tab` (Insights), `.gst-prof-switcher__btn` (Channels
   detail), `.gst-prof-ins__pill` (Channels Insights window picker).
4. Two flyouts in `Views/Pinned/Index.cshtml` (`gst-flyout-colcreate`,
   `gst-flyout-collection`) live inline instead of in `_*Flyout.cshtml`
   partials. The second violates the documented footer-order rule.

---

## Design decisions locked

### Token tiers (already in `design-system.md`)

Three-tier model (primitive → semantic → component) committed in the doc.
The CSS migration is **Phase 2**, not part of this plan.

### Two peer grid blueprints — shared substrate, divergent task

| | **Aurora summary grid** | **Navigator grid** (new entry) |
|---|---|---|
| Task | List of *editable* entities | List of *navigable* destinations |
| Row click | `GST.flyout.open(...)` | `window.location.href` |
| Last column | `⋯` row menu | Chevron `›` |
| Row state | `.is-selectable` | `.is-selectable.is-active` (current item) |
| Density | Compact | Comfortable |
| Per-row content slots | Tabular only | Tabular + optional sparkline / status |
| CSS class | `.gst-table.gst-aurora` | `.gst-table.gst-nav-table` |
| Reference impl | Pinned, Synonyms | Channels index |

**Shared substrate — identical, not variant-specific:** `.gst-table` base
typography / sort indicators / hover / borders; `.gst-toolbar` +
`.gst-search` + `.gst-filter` (label-above); `.gst-empty`, loading row,
alert region; `<thead>` server-rendered, `<tbody>` JS-populated.

**Two density tokens:** `--gst-row-padding-compact` (Aurora),
`--gst-row-padding-comfortable` (Navigator, ≈ `--gst-space-md`). Each
blueprint references its own token; the value lives in one place.

Rule for the future: a new list of things — first ask *do users edit
these inline, or drill into them?* Pick the matching blueprint. If both,
that's the trigger to discuss before building. No third blueprint
without an explicit proposal.

### Segmented control is distinct from tabs

Tabs reveal a panel below; segmented updates the view in-place (e.g.
window picker `[7d | 30d | 90d]`). New `.gst-segmented` component.

---

## Scope — 4 commits

### Commit 1 — Ship `GST.alert(msg, level)`

- New helper in `components.js`. Single host (`#gst-alert`), four levels
  (`info` / `success` / `warning` / `danger`), optional auto-dismiss.
- Migrate five hand-rolled callsites: `synonyms.js`, `pinned-coverage.js`,
  `pinned-collections.js`, `channels.js` (×2 — two helpers in the same file).
- Fix `pinned-collections.js:132` `gst-alert--error` → `gst-alert--danger`.
- Delete dead `GST.renderJobAlert` from `graphsearchtools.js` (zero callers).
- Doc: the API is already referenced in `design-system.md`; just make it real.

### Commit 2 — Promote Navigator grid to peer blueprint

- Rename `.gst-prof-table` → `.gst-nav-table` (leftover from Channels rename).
- Move shared rules into `.gst-table` base; keep only genuine divergences as
  `.gst-nav-table` modifiers (chevron column, `is-active` state, comfortable
  density).
- Move column widths out of `channels.js` `innerHTML` strings into CSS /
  `<colgroup>`.
- Add `--gst-row-padding-compact` / `--gst-row-padding-comfortable` tokens;
  reference from `.gst-table` and `.gst-table.gst-nav-table`.
- New `design-system.md` entry: **Component: Navigator grid**, peer of
  Aurora summary grid. Both reference the same Toolbar / alert / empty
  / loading conventions.

### Commit 3 — Reconcile tabs + introduce `.gst-segmented`

- Pick `.gst-tabs__btn` + `.is-active` as the survivor (most consumers).
- Migrate:
  - `.gst-tab` + `.active` (Insights global) → `.gst-tabs__btn`.
  - `.gst-prof-switcher__btn` (Channels detail tools console) → `.gst-tabs__btn`.
  - `.gst-prof-ins__pill` (Channels Insights window picker) → **new
    `.gst-segmented` component** (semantically different — state toggle,
    not panel reveal).
- Delete three obsolete selector blocks from CSS.
- New `design-system.md` entries: **Component: Tabs**, **Component: Segmented**.

### Commit 4 — Extract two inline flyouts

- `gst-flyout-colcreate` (`Views/Pinned/Index.cshtml:112-138`) →
  `Views/Shared/_CollectionCreateFlyout.cshtml`.
- `gst-flyout-collection` (`:141-186`) → `Views/Shared/_CollectionFlyout.cshtml`.
- Fix footer-order: destructive Delete → spacer → cancel → primary
  (currently violates `design-system.md:307`).
- Restores the "every flyout is a partial" rule.

---

## Estimated shape

Roughly: ~250 lines added (helper + partials + doc entries + density tokens),
~400 deleted (alert duplication + obsolete tab classes + inline flyouts +
parallel `.gst-prof-table` styles). Net negative LOC — the right shape for
a consolidation PR.

---

## Deferred to Phase 2 (separate scoped PRs)

- **Token-tier CSS migration.** Introduce primitive layer (`--gst-blue-600`
  etc), rename existing tokens to `--gst-color-*` at the semantic tier,
  sweep all callsites. Already planned in `design-system.md`.
- **Spacing token migration.** 362 raw px declarations in
  `graphsearchtools.css` (75% of all `padding/margin/gap`) → `--gst-space-*`.
  Mechanical, large diff, wants its own review.
- **Aurora `<thead>` extraction.** Per-tool Razor partials so
  `Channels/Detail.cshtml` can't drift from the top-level Pinned/Synonyms
  headers.
- **Search-icon SVG → shared `_SearchInput.cshtml` partial.** 7 copies
  in markup today.
