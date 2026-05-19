# GraphSearchtools Design System

A living catalogue of the visual patterns and reusable components that make up
the GraphSearchtools admin UI. The goal is consistency across tools and fewer
prompts to get a new feature to a release candidate.

The current reference implementation is the **Pinned** tool
(`Tools/Pinned/`, `Views/Pinned/Index.cshtml`). When you build a new tool page
or extend an existing one, start by skimming this doc and lifting the patterns
below — don't invent a new way to do something that already has an entry.

---

## Working contract

This catalogue is small, living, and **the source of truth for shared UI**.
Two rules:

1. **No new shared component without an entry here.**
   If you find yourself reaching for a new partial, JS helper, or `.gst-*`
   class that other tools would plausibly want, stop and add an entry first.
   Local one-off styles inside a single tool are fine — but if it looks
   reusable, codify it.

2. **No entry change without user confirm.**
   Extending an existing component (new variant, new prop, new state) is
   easy to do casually and expensive to undo once two tools depend on it.
   Surface the proposed change in chat, wait for sign-off, then update both
   the doc and the implementation in the same commit.

A useful test for "is this shared?": would a future tool page want it? If
yes, add it here. If you're not sure, ask.

---

## Design tokens

Tokens are organized in **three tiers** following the
[W3C Design Tokens](https://www.designtokens.org/) aliasing model: each
tier references the one below, component CSS reads from the highest
applicable tier, and themes swap by re-pointing aliases — not by
touching component code.

See [`design-system-methodologies.md`](./design-system-methodologies.md)
for the broader rationale and how this model compares to alternatives.

> **Current state.** The tier model is committed in this doc but the CSS
> file (`graphsearchtools.css`) is still flat — every token sits at what
> will become the semantic tier. A follow-up PR will introduce primitives
> underneath and rename the current names to `--gst-color-*`. **For new
> code today, keep using the names in the table at the bottom of this
> section.** Don't pre-emptively invent `--gst-color-*` names — the
> migration rewrites callsites in bulk, and a third spelling in the
> codebase would defeat the point.

### Tier 1 — primitive

Raw palette values. Brand-agnostic, intent-free. Named by colour family
+ shade or by scale step.

**Never reference a primitive from component CSS.** Primitives exist to
be aliased by semantic tokens; reading them directly bypasses the
abstraction that lets themes swap.

```css
--gst-blue-50:   #e6ecff;
--gst-blue-600:  #0042ff;
--gst-blue-700:  #0032c4;
--gst-gray-0:    #ffffff;
--gst-gray-900:  #1d1f24;
--gst-green-600: #1b873f;
```

### Tier 2 — semantic

Role / intent. This is the layer most code and conversation should
operate at. Named by purpose, not appearance.

Changing a brand colour means re-pointing one alias here — no component
touched, no grep for hex codes.

```css
--gst-color-primary:        var(--gst-blue-600);
--gst-color-primary-hover:  var(--gst-blue-700);
--gst-color-text:           var(--gst-gray-900);
--gst-color-success:        var(--gst-green-600);
```

### Tier 3 — component

Per-component overrides. Only introduce one when a component needs to
drift from the semantic default, or when "the button's background"
deserves a stable name independent of "the primary brand colour."

Most components can read semantic tokens directly and skip this tier.
When in doubt, don't add a component token — promote later if a real
divergence appears.

```css
--gst-button-bg:        var(--gst-color-primary);
--gst-button-bg-hover:  var(--gst-color-primary-hover);
--gst-input-border:     var(--gst-color-border);
```

### Token groups (today)

The names below are what's in the CSS right now. After the migration
they'll live at the semantic tier with a `--gst-color-*` prefix on the
colour groups; spacing / type / radii / shadows are already
semantic-by-scale and won't be renamed.

| Group        | Examples |
|--------------|----------|
| Surfaces     | `--gst-bg`, `--gst-surface`, `--gst-surface-hover`, `--gst-surface-active` |
| Borders      | `--gst-border`, `--gst-border-light` |
| Text         | `--gst-text`, `--gst-text-secondary`, `--gst-text-muted` |
| Brand        | `--gst-primary`, `--gst-primary-hover`, `--gst-primary-light`, `--gst-link` |
| Status       | `--gst-success(-light)`, `--gst-warning(-light)`, `--gst-danger(-light)`, `--gst-info(-light)` |
| Spacing      | `--gst-space-xs` (4) / `-sm` (8) / `-md` (16) / `-lg` (24) / `-xl` (32) |
| Type         | `--gst-font` (Inter), `--gst-text-xs` … `--gst-text-xl` |
| Radii        | `--gst-radius-sm` (3), `--gst-radius` (4), `--gst-radius-lg` (6) |
| Shadows      | `--gst-shadow-sm` … `--gst-shadow-xl` |

**Always reference the token, never the literal.** CSS prefix is
`gst-`; JS namespace is `GST.*`; localized strings are
`window.GST_STRINGS.{section}.{key}` (see `CLAUDE.md → Localization`).

---

## The tool page

A tool page has three regions, in order, inside `_SearchtoolsLayout.cshtml`:

```html
<div class="gst-page-header">
    <h1>@Loc.GetString("…/title")</h1>
    <p>@Loc.GetString("…/description")</p>
</div>

<div id="gst-alert" class="gst-alert" hidden></div>

@* …toolbar + grid + flyout… *@
```

Reference: `Views/Pinned/Index.cshtml`, `Views/Synonyms/Index.cshtml`.

- The header is always present. One `<h1>` per page, one `<p>` of lede copy.
- The alert region is always present (`id="gst-alert"`). JS toggles it via
  `GST.alert(msg, level)` — never inject ad-hoc banners elsewhere.
- The body region is tool-specific, but if it's a list of editable entities,
  the next two components — **Toolbar** and **Aurora summary grid** — are
  almost always what you want.

---

## Component: Toolbar

A horizontal strip above a grid: free-text search on the left, filter
dropdowns next to it, then a flexible spacer, then the primary action on the
right.

**Use when** a grid has more than one filter, or any combination of search +
primary CTA. **Don't use** for a single button — just place it.

### Skeleton

```html
<div class="gst-toolbar">
    <div class="gst-search">
        <svg class="gst-search__icon">…</svg>
        <input type="text" id="gst-{tool}-search" placeholder="@Loc.GetString(...)" />
    </div>

    <label class="gst-filter">
        <span class="gst-filter__label">@Loc.GetString(...)</span>
        <select class="gst-filter__select" id="gst-{tool}-{facet}-filter">…</select>
    </label>

    <div class="gst-toolbar__spacer"></div>

    <button type="button" class="gst-btn gst-btn--primary" id="gst-{tool}-create">
        + @Loc.GetString(".../add")
    </button>
</div>
```

### Rules

- `.gst-filter` is label-above-value (Aurora style), not a bare `<select>`.
  The label is the column name, singularized: "Site", "Locale", "Status".
- The default `<option>` reads "All <plural>" — e.g. "All sites".
- Filters appear in the same order as the grid columns they filter, left to
  right.
- The spacer (`<div class="gst-toolbar__spacer">`) pushes the primary action
  to the right edge.
- Only one primary action per toolbar. If you need a second action, it goes
  in the row's `⋯` menu, not the toolbar.

Reference: `Views/Pinned/Index.cshtml` lines 17–45.

---

## Component: Aurora summary grid

A flat table where each row is a summary of an entity, the row is clickable
(opens a flyout), and a `⋯` button in the last column reveals row actions.
This is the canonical list-of-things surface across the addon.

**Use when** showing 5+ editable entities of one kind. **Don't use** for
read-only telemetry rows or activity logs — those use the same `.gst-table`
class but no row-click / `⋯`.

### Skeleton

```html
<table class="gst-table gst-{tool}-aurora-table">
    <thead>
        <tr>
            <th data-sort="phrase">@Loc.GetString(".../col_phrase")</th>
            <th data-sort="site">@Loc.GetString(".../col_site")</th>
            <th data-sort="activity">@Loc.GetString(".../col_activity")</th>
            <th aria-label="Actions"></th>
        </tr>
    </thead>
    <tbody id="gst-{tool}-aurora-rows"></tbody>
</table>
```

### Rules

- `<thead>` is server-rendered; `<tbody>` is JS-populated. Headers carry
  `data-sort="{key}"`; the JS toggles `data-sort-dir="asc|desc"` to drive
  the arrow indicators.
- Rows that open an editor must add `class="is-selectable"` and a `click`
  handler that calls `GST.flyout.open(...)`. The row should also be focusable
  (`tabindex="0"`) so keyboard users can press Enter.
- The last column is reserved for the `⋯` button. Use `GST.rowMenu(anchor,
  items)` to render the popover — see **Row menu** below.
- Numeric columns get the `.num` class (right-aligned, tabular numerals).
- Loading state: render one `<tr><td colspan="N"><span class="gst-spinner"/>
  …</td></tr>` row, not a separate overlay.
- Empty state: render one `<tr><td colspan="N" class="gst-empty">…</td></tr>`
  with a single CTA. Never a blank `<tbody>`.

Reference: `Views/Pinned/Index.cshtml` lines 48–60 + `pinned-aurora.js`.

---

## Component: Row menu (`GST.rowMenu`)

The popover anchored to a row's `⋯` button. Single open at a time; closes on
outside-click or Esc.

### Use

```js
const btn = row.querySelector('.gst-rowmenu');
btn.addEventListener('click', (e) => {
    e.stopPropagation();
    GST.rowMenu(btn, [
        { label: GST.s('pinned.menu_edit', 'Edit'),   onSelect: () => openFlyout(row) },
        { label: GST.s('pinned.menu_delete', 'Delete'), onSelect: () => del(row), danger: true }
    ]);
});
```

### Rules

- Max 5 items per menu. If you need more, the row probably needs a flyout
  instead.
- `danger: true` for destructive items; they render red and sit at the
  bottom.
- Don't put the primary edit action *only* here — row-click also opens the
  flyout, so the menu's "Edit" is a redundant affordance for discoverability.

Reference: `components.js` lines 18–71.

---

## Component: Flyout edit panel

A right-edge panel (~480 px) for editing one entity. Replaces inline-edit
rows. Backdrop + focus trap + Esc/backdrop dismiss are handled by
`GST.flyout`.

**Use when** an entity has 2–8 fields. **Don't use** for entities that need
audit history, large rich-text fields, or multi-section forms — those grow a
detail page.

### Markup (Razor partial)

Every flyout lives in `Views/Shared/_{Entity}Flyout.cshtml` and is included
once on the page hosting it:

```cshtml
@await Html.PartialAsync("/Views/Shared/_PinFlyout.cshtml")
```

Conventional IDs (the JS depends on these):

- `id="gst-flyout-{key}"` — the `<aside class="gst-flyout">`
- `id="gst-flyout-{key}-backdrop"` — the backdrop `<div>`
- `data-flyout-close="{key}"` on every cancel/× button
- `data-flyout-save="{key}"` on the primary Save button

Footer ordering, left-to-right: **destructive secondary** (Delete, hidden in
create mode) → spacer → **cancel** → **primary**.

### Field markup

```html
<div class="gst-field">
    <label class="gst-field__label" for="gst-{key}fly-{name}">…</label>
    <textarea id="gst-{key}fly-{name}" rows="2"></textarea>
    <span class="gst-field__hint">…</span>
</div>

<div class="gst-field-row">
    <div class="gst-field">…</div>
    <div class="gst-field">…</div>
</div>
```

Use `.gst-field-row` for two fields side-by-side (e.g. Locale + Effective).
For lists-inside-a-field (multi-target pins, synonym terms), use
`.gst-target-list` — see `_PinFlyout.cshtml`.

### JS API

```js
GST.flyout.open('pin', {
    onOpen:  ({ panel }) => { /* populate fields from row */ },
    onClose: ({ panel }) => { /* optional cleanup */ },
    focus:   '#gst-pinfly-phrase'   // optional, defaults to first input
});

GST.flyout.close('pin');
GST.flyout.isOpen('pin');           // → boolean
```

`onOpen` is called every time `open(key)` is invoked — including
re-entrantly while the panel is already open. Use it to refresh fields when
the user clicks a different row without closing the panel.

Reference: `Views/Shared/_PinFlyout.cshtml`, `components.js` lines 73–212,
`pinned-aurora.js` (flyout wire-up).

---

## Component: KPI tile with sparkline

A horizontal row of headline metric tiles. Each tile stacks a small label,
a large value, and an inline 30-day sparkline (a single SVG line, not
bars). Used for "here's the number, here's the trend" surfaces — the
Insights tool's search-activity card and the Channel detail header are
the reference implementations.

**Use when** showing 3–5 cluster-summed numbers that share the same window
and benefit from a trend hint. **Don't use** for single ratios (use a plain
text line), for >5 metrics (becomes a dashboard, not a header), or for
non-numeric signals (status badges live in row chrome instead).

### Skeleton

```html
<div class="gst-kpis" aria-live="polite">
    <div class="gst-kpi" data-kpi="{name}">
        <div class="gst-kpi__label">@Loc.GetString("…") <span class="gst-muted">(last 30 days)</span></div>
        <div class="gst-kpi__value">12,345</div>
        <div class="gst-kpi__spark"></div>   <!-- GST.sparkline fills this -->
    </div>
    …two more…
</div>
```

### JS API

```js
GST.sparkline(hostElementOrSelector, [12, 45, 0, 56, ...], {
    label: 'Daily searches',          // aria-label on the SVG
    max: 100,                          // optional fixed scale (CTR uses 100)
    formatTooltip: (v, i) => `…`       // optional per-bar <title>
});
```

- Series can be any length; the SVG stretches to fill its container.
- Default max = `Math.max(...series, 1)`. Fix the max for ratio metrics
  (CTR, success rate) so a quiet day doesn't look like a 100% day.
- When every value is zero, the helper paints a muted dotted baseline
  instead of invisible bars.

### Rules

- One row per page, max 5 tiles. If you need more, you're building a
  dashboard — split into multiple `.gst-kpis` rows or a separate card.
- Window applies to the *whole row*; mixing windows across tiles in the
  same row is confusing.
- The label always names the metric *and* the window
  ("Searches (last 30 days)"). Don't rely on a card-level subtitle to
  carry the window — a screenshot of just the tile must be legible.
- Don't put interactive controls (filters, toggles) inside a tile. The
  tile is read-only; refresh + window pick live on the surrounding card
  header.
- Numbers use the compact form: `GST.formatCompactInt` rounds to nearest
  integer with K/M suffixes (`12K`, `1M`); `GST.formatCompactPct` rounds
  to nearest integer with `%`. KPI tiles are scanning surfaces, not
  forensic readouts — precision belongs in the tables below.
- The sparkline is decorative-but-informative — provide a `formatTooltip`
  callback so hovering over a day surfaces the value.

### Card-level render helper

Both the global Insights tool and the Channel detail page mount this
component, so the rendering is centralised:

```js
GST.renderKpiCard(hostElementOrSelector, kpisPayload);
GST.renderKpiCardLoading(host);   // 3 blank tiles while fetching
GST.renderKpiCardError(host);     // single-tile error state
```

The helper reads localized labels from `window.GST_STRINGS.insights` by
default. If a future surface needs different labels, pass
`{ strings: customMap }`.

Reference: `Views/Insights/Index.cshtml` (the `data-card="kpis"` block,
global aggregate), `Views/Channels/Detail.cshtml` (the `#gst-prof-kpis`
host, channel-scoped), `GST.renderKpiCard` + `GST.sparkline` in
`components.js`.

---

## Component: Content picker (`GST.contentPicker`)

A modal dialog with a content-tree browser and a search box. Used inside a
flyout (or elsewhere) to pick a CMS content item as a pin target.

### Use

```js
const result = await GST.contentPicker({
    rootId: 0,                                   // CMS root content link (default 0)
    title:  GST.s('pinned.picker_title', 'Select page')  // optional
});
if (result) {
    // { id, contentLink, contentGuid, name, typeName }
}
```

Returns `null` on cancel. The picker handles its own loading / empty / error
states; the caller only deals with the result.

### Rules

- Always pass a localized `title` — the default "Select Content" is
  English-only fallback copy.
- Don't open two pickers concurrently. The dialog is modal, but stacked
  modals confuse keyboard focus restoration.
- If you need a content-type selector instead of a content selector, use
  `GST.contentTypePicker(opts)` — same shape, returns
  `{ id, name, displayName, base }`.

Reference: `components.js` lines 214–477,
`Components/ComponentsApiController.cs`.

---

## What's NOT in the system yet

These show up in the codebase but haven't been promoted to shared components
— either because they appear in only one place, or because the abstraction
isn't stable. Don't copy them into a second tool without a doc entry first.

- **Live preview pane** (Channels detail) — the right-hand 50/50 SERP
  preview. Specific to Channels today.
- **Tab strip** (Channels detail sub-tabs, Pinned Audit tab) — there are
  two slightly different implementations. Needs reconciliation before
  promotion.
- **Stat cards** (Insights, Channels header strip) — currently two flavours.
  Pick one, then add an entry.
- **Drag-reorder list** (multi-target pins in `_PinFlyout`) — works but
  desktop-mouse only. Promote when touch support lands.

If you want to use one of these in a new tool, that's the trigger for a doc
entry — flag it and we'll formalize together.

---

## How to add or extend a component

1. **Sketch in chat** before writing code: what does the user task look
   like, what's the proposed component, what existing component would it
   replace or extend?
2. **Wait for sign-off.** This is the second rule of the working contract
   and exists so a casual extension doesn't accidentally fork the design
   across two tools.
3. **Implement once:** one Razor partial / JS helper / CSS block, used by
   both tools that motivated it. If only one tool needs it today, it's
   probably premature.
4. **Update this doc** in the same commit. An entry should answer: purpose,
   when to use, when not to use, skeleton, rules, reference files.
5. **Migrate consumers in follow-ups** if the new component subsumes
   existing ad-hoc markup — don't leave two patterns in the codebase
   indefinitely.
