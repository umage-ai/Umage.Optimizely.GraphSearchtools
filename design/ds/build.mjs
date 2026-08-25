// Generates the Claude Design *design-system* bundle for GraphSearchtools.
//
//   node design/ds/build.mjs        → writes dist/ (styles.css, cards, screens,
//                                     readme.md, SKILL.md, _ds_manifest.json)
//
// The bundle is then pushed to the "Graph Search Tools" design-system project on
// claude.ai/design with the DesignSync tool (see README.md). Cards are plain
// HTML previews; the first line carries the `@dsCard` marker the Design System
// pane indexes. `styles.css` is graphsearchtools.css verbatim plus the mock
// CMS-shell frame used by the screen cards.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  css, SHELL_CSS, tokens, resolveToken, ICONS, icon,
  screens, shellFrame, searchInput, filter, kpis, tabs, toolCards, toolCard,
  navTable, pinTable, synonymTable, PINS, RULES, serp, HITS,
  insightLanes, insightsBar, profSwitcher,
} from '../lib.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, 'dist');
for (const d of ['', 'foundations', 'components', 'screens']) mkdirSync(join(dist, d), { recursive: true });

const files = {};
const cards = [];

// ── styles.css ─────────────────────────────────────────────────────────────
files['styles.css'] = `/* Graph Search Tools — design-system entry point.
   Section 1 is graphsearchtools.css, copied VERBATIM from the repo
   (src/GraphSearchtools/modules/_protected/GraphSearchtools/ClientResources/css/).
   Section 2 is the mock CMS-13 shell frame used only by the screen cards.
   Regenerate with: node design/ds/build.mjs */
@import url("https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&display=swap");

${css}

${SHELL_CSS}
`;

// ── Card scaffold ──────────────────────────────────────────────────────────
const DEMO_CSS = `
/* Demo scaffolding only — everything visual comes from ../styles.css. */
body { margin: 0; background: #ffffff; }
.demo { padding: 24px; min-height: 0; display: block; }
.demo-head { font-family: var(--gst-font); font-size: 11px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; color: var(--gst-color-text-muted); margin: 24px 0 10px; }
.demo-head:first-child { margin-top: 0; }
.demo-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.demo-note { font-size: 12px; color: var(--gst-color-text-secondary); max-width: 64ch; line-height: 1.5; margin: 10px 0 0; }
.demo-note code { font-family: var(--gst-font-mono); font-size: 11px; background: var(--gst-color-surface-hover); padding: 1px 5px; border-radius: 3px; }
`;

function card({ path, group, name, subtitle, viewport, body, extraCss = '', shell = true }) {
  const [w, h] = viewport.split('x');
  const marker = `<!-- @dsCard group="${group}" name="${name}"${subtitle ? ` subtitle="${subtitle}"` : ''} viewport="${viewport}" -->`;
  const depth = path.split('/').length - 1;
  const rel = depth ? '../'.repeat(depth) : './';
  files[path] = `${marker}
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>Graph Search Tools — ${name}</title>
<link rel="stylesheet" href="${rel}styles.css" />
<style>${DEMO_CSS}${extraCss}</style>
</head>
<body>
${shell ? `<div class="gst-shell demo">\n${body}\n</div>` : body}
</body>
</html>
`;
  cards.push({ path, group, viewport, ...(subtitle ? { subtitle } : {}), name });
  void w; void h;
}

// ── Foundations ────────────────────────────────────────────────────────────
const semantic = tokens.filter(t => t.name.startsWith('--gst-color-'));
const groups = [
  ['Surfaces', ['bg', 'surface', 'surface-hover', 'surface-active']],
  ['Borders', ['border', 'border-light']],
  ['Text', ['text', 'text-secondary', 'text-muted']],
  ['Brand', ['primary', 'primary-hover', 'primary-light', 'link', 'link-hover']],
  ['Status', ['success', 'success-light', 'warning', 'warning-light', 'danger', 'danger-light', 'info', 'info-light']],
];
const swatch = (t) => {
  const hex = resolveToken(t.name);
  const alias = t.value.startsWith('var(') ? t.value.slice(4, -1) : '';
  const light = /^#(f|e)/i.test(hex);
  return `<div class="sw"><div class="sw__chip" style="background:${hex}; ${light ? 'border:1px solid var(--gst-color-border);' : ''}"></div><div class="sw__name">${t.name.replace('--gst-color-', '')}</div><div class="sw__hex">${hex}${alias ? ` · ${alias.replace('--gst-', '')}` : ''}</div></div>`;
};
card({
  path: 'foundations/colors.html', group: 'Foundations', name: 'Color', viewport: '700x780',
  subtitle: 'Semantic --gst-color-* tokens and the primitives they alias',
  extraCss: `.grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 12px 14px; }
.sw__chip { height: 44px; border-radius: var(--gst-radius); }
.sw__name { font-size: 12px; font-weight: 500; margin-top: 6px; }
.sw__hex { font-family: var(--gst-font-mono); font-size: 10.5px; color: var(--gst-color-text-muted); margin-top: 2px; }`,
  body: groups.map(([g, keys]) => `<div class="demo-head">${g}</div>
<div class="grid">${keys.map(k => swatch(semantic.find(t => t.name === `--gst-color-${k}`))).join('')}</div>`).join('\n')
    + `<p class="demo-note">Component CSS reads the semantic tier only (<code>var(--gst-color-primary)</code>); primitives (<code>--gst-blue-600</code> …) exist to be aliased. Three-tier model per <code>docs/design-system.md</code>.</p>`,
});

const typeRows = [
  ['Page title', 'font-size: 32px; font-weight: 300; line-height: 1.15; letter-spacing: -0.01em;', 'text-3xl · 300 · Graph Search Tools'],
  ['Detail title', 'font-size: 24px; font-weight: 600; letter-spacing: -0.01em;', 'text-2xl · 600 · Alloy site search'],
  ['Card / flyout title', 'font-size: 16px; font-weight: 500; letter-spacing: -0.005em;', 'text-lg · 500 · Pinned results'],
  ['Body', 'font-size: 14px; font-weight: 400; line-height: 1.5;', 'text-base · 400 · Marketer-facing tuning for each search surface.'],
  ['Table header / tab / button', 'font-size: 12px; font-weight: 500; color: var(--gst-color-text-secondary);', 'text-sm · 500 · Activity (30d)'],
  ['Hint / badge', 'font-size: 11px; font-weight: 500; color: var(--gst-color-text-secondary);', 'text-xs · 500 · 1 days ago · Admin'],
  ['Mono (keys, paths)', 'font-family: var(--gst-font-mono); font-size: 11px; color: var(--gst-color-text-muted);', 'alloy-search · /en/alloy-plan/'],
  ['KPI value', 'font-size: 32px; font-weight: 300; letter-spacing: -0.01em; font-variant-numeric: tabular-nums; line-height: 1.1;', '19K'],
];
card({
  path: 'foundations/type.html', group: 'Foundations', name: 'Typography', viewport: '700x580',
  subtitle: 'Inter — light oversized titles, 14px body, 12px chrome',
  extraCss: `.t { display: grid; grid-template-columns: 170px 1fr; gap: 16px; align-items: baseline; padding: 10px 0; border-bottom: 1px solid var(--gst-color-border-light); }
.t:last-of-type { border-bottom: 0; } .t__k { font-size: 11px; color: var(--gst-color-text-muted); }`,
  body: typeRows.map(([k, style, sample]) => `<div class="t"><div class="t__k">${k}</div><div style="${style}">${sample}</div></div>`).join('\n')
    + `<p class="demo-note">Scale: <code>--gst-text-xs 11</code> · <code>sm 12</code> · <code>base 14</code> · <code>lg 16</code> · <code>xl 20</code> · <code>2xl 24</code> · <code>3xl 32</code> · <code>4xl 40</code>. Mirrors the Optimizely admin (Aurora) so tools feel native.</p>`,
});

const spaces = ['xs', 'sm', 'md', 'lg', 'xl', '2xl'];
card({
  path: 'foundations/spacing.html', group: 'Foundations', name: 'Spacing, radii & shadows', viewport: '700x460',
  subtitle: '4 · 8 · 16 · 24 · 32 · 48 — flat page, shadows only on popovers',
  extraCss: `.sp { display: flex; align-items: flex-end; gap: 18px; } .sp__bar { background: var(--gst-color-primary-light); border: 1px solid var(--gst-color-primary); }
.sp__l { font-family: var(--gst-font-mono); font-size: 10.5px; color: var(--gst-color-text-muted); margin-top: 6px; text-align: center; }
.rad { display: flex; gap: 18px; } .rad__box { width: 96px; height: 56px; border: 1px solid var(--gst-color-border); background: var(--gst-color-surface); }
.sh { display: flex; gap: 22px; } .sh__box { width: 120px; height: 64px; border-radius: var(--gst-radius-lg); background: #fff; border: 1px solid var(--gst-color-border-light); }`,
  body: `<div class="demo-head">Spacing</div>
<div class="sp">${spaces.map(s => { const v = resolveToken(`--gst-space-${s}`); return `<div><div class="sp__bar" style="width:${v}; height:${v}"></div><div class="sp__l">${s} · ${v}</div></div>`; }).join('')}
<div><div class="sp__bar" style="width: 60px; height: 14px; border-style: dashed; background: transparent;"></div><div class="sp__l">row compact 14×16</div></div></div>
<div class="demo-head">Radii</div>
<div class="rad">${['sm', '', 'lg'].map(s => { const n = s ? `--gst-radius-${s}` : '--gst-radius'; const v = resolveToken(n); return `<div><div class="rad__box" style="border-radius:${v}"></div><div class="sp__l">${n.replace('--gst-', '')} · ${v}</div></div>`; }).join('')}
<div><div class="rad__box" style="border-radius:999px; width:56px"></div><div class="sp__l">pill · badges, chips</div></div></div>
<div class="demo-head">Shadows</div>
<div class="sh">${['sm', '', 'lg', 'xl'].map(s => { const n = s ? `--gst-shadow-${s}` : '--gst-shadow'; return `<div><div class="sh__box" style="box-shadow: var(${n})"></div><div class="sp__l">${n.replace('--gst-', '')}</div></div>`; }).join('')}</div>
<p class="demo-note">The page itself is flat: whitespace plus 1px hair-rules between sections. <code>shadow</code> appears on hovered tool cards, <code>shadow-xl</code> on the flyout, <code>shadow-lg</code> on popovers.</p>`,
});

card({
  path: 'foundations/icons.html', group: 'Foundations', name: 'Iconography', viewport: '700x300',
  subtitle: 'Lucide line icons · stroke 1.5 · 24×24 · currentColor',
  extraCss: `.ic { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 14px 8px; }
.ic__cell { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 10px 4px; border: 1px solid var(--gst-color-border-light); border-radius: var(--gst-radius); color: var(--gst-color-text); }
.ic__n { font-family: var(--gst-font-mono); font-size: 10.5px; color: var(--gst-color-text-muted); }`,
  body: `<div class="ic">${Object.keys(ICONS).map(n => `<div class="ic__cell">${icon(n, 20)}<span class="ic__n">${n}</span></div>`).join('')}</div>
<p class="demo-note">One icon per concept; scale via <code>width</code>/<code>height</code>, never a second path. Off-state variants (<code>pinOff</code>, <code>synonymOff</code>) are the only doubles. Registry lives in <code>_Icon.cshtml</code> + <code>GST.icons</code>.</p>`,
});

// ── Components ─────────────────────────────────────────────────────────────
card({
  path: 'components/buttons.html', group: 'Components', name: 'Buttons & badges', viewport: '700x400',
  subtitle: 'Solid brand-blue primary, outlined default, text and danger variants; 40px tall',
  body: `<div class="demo-head">Buttons</div>
<div class="demo-row">
  <button type="button" class="gst-btn gst-btn--primary">+ Add pin</button>
  <button type="button" class="gst-btn">Cancel</button>
  <button type="button" class="gst-btn gst-btn--danger">Delete</button>
  <button type="button" class="gst-btn gst-btn--text">Refresh</button>
  <button type="button" class="gst-btn gst-btn--sm">Small</button>
  <button type="button" class="gst-btn gst-btn--icon" aria-label="Close">${icon('x', 16)}</button>
  <button type="button" class="gst-btn gst-btn--primary gst-disabled" disabled aria-disabled="true" title="You need the edit permission">+ Add rule</button>
</div>
<div class="demo-head">Row actions</div>
<div class="demo-row">
  <button type="button" class="gst-rowdelete" aria-label="Delete">${icon('trash', 16)}</button>
  <button type="button" class="gst-rowaction" aria-label="Preview this phrase">${icon('search', 16)}</button>
  <button type="button" class="gst-prof-ins__refresh" aria-label="Refresh">${icon('refresh', 14)}</button>
  <span class="gst-muted" style="font-size: 12px;">rowdelete · rowaction · refresh (Insights bar)</span>
</div>
<div class="demo-head">Badges</div>
<div class="demo-row">
  <span class="gst-badge gst-badge--default">all sites</span>
  <span class="gst-badge gst-badge--primary">en</span>
  <span class="gst-badge gst-badge--success">Active</span>
  <span class="gst-badge gst-badge--warning">Expires soon</span>
  <span class="gst-badge gst-badge--danger">Orphaned</span>
  <span class="gst-tab__count">15</span>
  <span class="gst-filter-chip">2026-08-19 <button type="button" class="gst-filter-chip__clear" aria-label="Clear">×</button></span>
</div>
<p class="demo-note">One primary action per toolbar. Disabled-by-permission keeps the control visible with <code>.gst-disabled</code> + tooltip — never hide controls a user could operate with the right permission.</p>`,
});

card({
  path: 'components/toolbar.html', group: 'Components', name: 'Toolbar, search & filters', viewport: '900x300',
  subtitle: 'Filter-as-you-type input, label-above-value filter chips, spacer, one primary action',
  body: `<div class="demo-head">Toolbar composition</div>
<div class="gst-toolbar">
  ${searchInput('demo-search', 'Filter by phrase or content…')}
  ${filter('Collection', 'All collections')}
  ${filter('Locale', 'All')}
  <div class="gst-toolbar__spacer"></div>
  <button type="button" class="gst-btn gst-btn--primary">+ Add pin</button>
</div>
<div class="demo-head">Filter variants</div>
<div class="demo-row" style="gap: 24px;">
  ${filter('Window', 'Last 7d')}
  <div class="gst-filter gst-filter--static"><span class="gst-filter__label">Site</span><span class="gst-filter__static-value">all sites</span></div>
  <span class="gst-muted" style="font-size: 12px; align-self: center;">← static variant when there is exactly one option</span>
</div>
<p class="demo-note"><code>.gst-search</code> has no icon or external label — the placeholder is the label. Filters appear in grid-column order; the default option reads "All &lt;plural&gt;".</p>`,
});

card({
  path: 'components/tabs.html', group: 'Components', name: 'Tabs & segmented', viewport: '700x380',
  subtitle: 'Tabs swap panels; segmented swaps state; the console switcher adds icons + unwired',
  body: `<div class="demo-head">Page tabs with count chips</div>
${tabs([['Top phrases', true, 15], ['Zero-result', false, 5], ['Low-CTR', false, 7]], 'Insights')}
<div class="demo-head">Segmented (window picker)</div>
<div class="gst-segmented" role="group" aria-label="Time window">
  <button type="button" class="gst-segmented__btn">24h</button>
  <button type="button" class="gst-segmented__btn is-active">7d</button>
  <button type="button" class="gst-segmented__btn">30d</button>
</div>
<div class="demo-head">Channel-detail console switcher (one-off specialisation)</div>
<div style="border: 1px solid var(--gst-color-border); border-radius: var(--gst-radius-lg); overflow: hidden;">
  ${profSwitcher({ unwired: ['synonyms'] })}
</div>
<p class="demo-note">Exactly one <code>.is-active</code>. The amber dot marks an <code>.is-unwired</code> tab — the channel's GraphQL query doesn't opt into that feature, so the panel explains instead of editing.</p>`,
});

card({
  path: 'components/tables.html', group: 'Components', name: 'Grids — Aurora summary & Navigator', viewport: '900x800',
  subtitle: 'Two blueprints on one .gst-table substrate: edit-in-flyout rows vs. navigate-to-detail rows',
  body: `<div class="demo-head">Aurora summary grid — row opens a flyout, last column holds row actions</div>
${synonymTable(RULES.slice(0, 4))}
<div class="demo-head" style="margin-top: 28px;">Navigator grid — row navigates, chevron in the last column, comfortable density</div>
${navTable()}
<div class="demo-head" style="margin-top: 28px;">Read-only telemetry table — no row-click, sort indicator on the active column</div>
<table class="gst-table gst-ins-aurora-table">
  <thead><tr><th data-sort="phrase">Phrase</th><th data-sort="channel">Channel</th><th data-sort="locale">Locale</th><th data-sort="hits" data-sort-dir="desc" class="num">Hits</th><th data-sort="zero" class="num">Zero results</th></tr></thead>
  <tbody>
    <tr><td class="gst-ins-row__phrase">Alloy Plan</td><td><a class="gst-ins-row__channel" href="#">alloy-search</a></td><td>en</td><td class="num">638</td><td class="num gst-muted">0</td></tr>
    <tr><td class="gst-ins-row__phrase">gdpr</td><td><a class="gst-ins-row__channel" href="#">alloy-search</a></td><td>en</td><td class="num">41</td><td class="num">41</td></tr>
  </tbody>
</table>
<p class="demo-note">Ask "do users edit these inline, or drill into them?" and pick the blueprint. Numeric columns take <code>.num</code>; loading and empty states are single <code>&lt;tr&gt;</code> rows, never overlays.</p>`,
});

card({
  path: 'components/kpi.html', group: 'Components', name: 'KPI tiles with sparkline', viewport: '900x400',
  subtitle: '3–5 cluster-summed numbers sharing one window; compact numerals, 30-day line',
  body: `${kpis('demo-kpis')}
<div class="demo-head">Loading · no access</div>
<div class="gst-kpis" style="margin-bottom: 0;">
  <div class="gst-kpi"><div class="gst-kpi__label">Searches <span class="gst-muted">(last 30 days)</span></div><div class="gst-kpi__value gst-muted">—</div><div class="gst-kpi__spark gst-sparkline--empty"></div></div>
  <div class="gst-kpi gst-kpi--noaccess" style="flex: 2 1 0;"><div class="gst-kpi__label">Search insights aren't available to you</div><div class="gst-muted" style="font-size: 12px;">Ask an administrator for the Insights permission.</div></div>
</div>
<p class="demo-note">The label always names metric <em>and</em> window. Fix <code>max</code> for ratio metrics so a quiet day doesn't read as 100%. All-zero series paint a dotted baseline.</p>`,
});

card({
  path: 'components/page-header.html', group: 'Components', name: 'Page headers', viewport: '700x300',
  subtitle: 'Bare tool header (32px light title + lede) and the channel-detail header with crumb + key',
  body: `<div class="gst-page-header">
  <h1>Pinned results</h1>
  <p>Pin specific content to the top of the results for chosen phrases, per collection and locale.</p>
</div>
<div class="gst-page-header gst-prof-detail-header" style="margin-bottom: 0;">
  <div class="gst-page-header__title">
    <div class="gst-prof-detail-header__crumb">
      <a href="#" class="gst-prof-detail-header__back">${icon('chevronLeft', 14)}Back to all channels</a>
      <span class="gst-prof-detail-header__sep" aria-hidden="true">·</span>
      <code class="gst-prof-detail-header__key">alloy-search</code>
    </div>
    <h1>Alloy site search</h1>
    <p>Header search across the Alloy demo content.</p>
  </div>
</div>`,
});

card({
  path: 'components/tool-card.html', group: 'Components', name: 'Overview tool cards', viewport: '900x300',
  subtitle: 'Flat tile with brand-blue icon; lifts on hover; desaturated + "No access" chip when the view permission is missing',
  body: `<div class="gst-tool-grid" style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px;">
  ${toolCard(toolCards[0])}
  ${toolCard(toolCards[2])}
  ${toolCard(toolCards[1], true)}
</div>`,
});

card({
  path: 'components/flyout.html', group: 'Components', name: 'Flyout edit panel', viewport: '560x760',
  subtitle: '480px right-edge panel for 2–8 fields; footer: destructive → spacer → cancel → primary',
  extraCss: `.demo .gst-flyout { position: relative; top: auto; right: auto; bottom: auto; height: 700px; animation: none; border: 1px solid var(--gst-color-border-light); border-radius: var(--gst-radius-lg); }
.demo .gst-target-list { display: flex; flex-direction: column; gap: 6px; }
.demo .gst-target { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid var(--gst-color-border); border-radius: var(--gst-radius); font-size: 13px; }
.demo .gst-target__type { margin-left: auto; }`,
  body: `<aside class="gst-flyout" role="dialog" aria-modal="true">
  <header class="gst-flyout__header">
    <h2 class="gst-flyout__title">Edit pin</h2>
    <button type="button" class="gst-flyout__close" aria-label="Close">×</button>
  </header>
  <div class="gst-flyout__body">
    <div class="gst-field">
      <label class="gst-field__label" for="fly-phrase">Search phrase</label>
      <textarea id="fly-phrase" rows="2">alloy plan</textarea>
      <span class="gst-field__hint">Matched case-insensitively against the whole query.</span>
    </div>
    <div class="gst-field">
      <label class="gst-field__label">Pinned content <span class="gst-field__count gst-muted">(1)</span></label>
      <div class="gst-target-list">
        <div class="gst-target">${icon('pin', 14)}<span>Alloy Plan</span><span class="gst-badge gst-badge--default gst-target__type">ProductPage</span></div>
      </div>
      <button type="button" class="gst-btn gst-btn--text" style="align-self: flex-start;">+ Add content</button>
      <span class="gst-field__hint">Drag to reorder — the first item ranks highest.</span>
    </div>
    <div class="gst-field-row">
      <div class="gst-field">
        <label class="gst-field__label" for="fly-locale">Locale</label>
        <select id="fly-locale"><option>en</option></select>
      </div>
      <div class="gst-field">
        <label class="gst-field__label" for="fly-eff">Effective until</label>
        <input id="fly-eff" type="date" />
        <span class="gst-field__hint">Leave empty for no end date.</span>
      </div>
    </div>
    <div class="gst-field"><button type="button" class="gst-btn">Deactivate</button></div>
    <div class="gst-flyout__notice"><span><strong>Conflict:</strong> "alloy plan" is already pinned in alloy-en for sv.</span></div>
  </div>
  <footer class="gst-flyout__footer">
    <button type="button" class="gst-btn gst-btn--danger">Delete</button>
    <div class="gst-flyout__footer__spacer"></div>
    <button type="button" class="gst-btn">Cancel</button>
    <button type="button" class="gst-btn gst-btn--primary">Save</button>
  </footer>
</aside>`,
});

card({
  path: 'components/feedback.html', group: 'Components', name: 'Alerts, banners & empty states', viewport: '700x800',
  subtitle: 'Alert region levels, read-only banner, empty state, unwired callout, spinner',
  body: `<div class="demo-head">Alert region (#gst-alert via GST.alert)</div>
<div class="gst-alert gst-alert--info">Synonyms in Optimizely Graph are tenant-global; this list is narrowed to the channel's locales.</div>
<div class="gst-alert gst-alert--success">Pin saved. It's live in search results now.</div>
<div class="gst-alert gst-alert--warning">This channel is site-shared — pins apply to every site that uses the collection.</div>
<div class="gst-alert gst-alert--danger">Couldn't reach Optimizely Graph. Try again in a moment.</div>
<div class="demo-head">Read-only banner (view without edit permission)</div>
<div class="gst-readonly-banner"><span class="gst-readonly-banner__icon"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg></span><span class="gst-readonly-banner__text"><strong>Read-only.</strong> You can view synonym rules but not change them.</span></div>
<div class="demo-head">Empty state · loading</div>
<div class="demo-row" style="align-items: stretch;">
  <div class="gst-empty" style="flex: 1; border: 1px dashed var(--gst-color-border); border-radius: var(--gst-radius);">${icon('info', 40)}<h3 style="font-size: 14px; font-weight: 500; color: var(--gst-color-text);">No pins yet</h3><p>Pin a page to the top of results for a phrase your visitors search.</p></div>
  <div class="gst-loading" style="flex: 0 0 160px; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; font-size: 12px;"><span class="gst-spinner"></span>Loading…</div>
</div>
<div class="demo-head">Unwired callout (feature not opted into by the channel's query)</div>
<div class="gst-prof-unwired" role="status" style="padding: 20px 16px; border: 1px solid var(--gst-color-border-light); border-radius: var(--gst-radius);">
  <div class="gst-prof-unwired__glyph">${icon('pinOff', 24)}</div>
  <h3 class="gst-prof-unwired__title">This channel doesn't use pinned results</h3>
  <p class="gst-prof-unwired__body">The registered GraphQL query has no <code>usePinned</code> argument, so pins saved here would never reach the storefront.</p>
</div>`,
});

card({
  path: 'components/serp.html', group: 'Components', name: 'Live preview (SERP)', viewport: '640x640',
  subtitle: 'Channel-detail try-it pane: pill input, mono stats line, serif titles, pinned card treatment',
  body: `<div class="gst-prof-preview" style="border: 1px solid var(--gst-color-border-light); border-radius: var(--gst-radius-lg);">
  <div class="gst-prof-preview__inner">
    <header class="gst-prof-preview__head">
      <div class="gst-prof-preview__heading"><span class="gst-prof-preview__eyebrow">Try-it</span><h2 class="gst-prof-preview__title">Live preview</h2></div>
      <div class="gst-prof-preview__pickers"><div class="gst-filter gst-prof-preview__filter gst-filter--static"><span class="gst-filter__label">Site</span><span class="gst-filter__static-value">all sites</span></div><label class="gst-filter gst-prof-preview__filter"><span class="gst-filter__label">Locale</span><select class="gst-filter__select"><option>en</option></select></label></div>
    </header>
    <div class="gst-prof-preview__body">
      ${serp({ hits: HITS.slice(0, 3), pinnedFirst: true })}
    </div>
  </div>
</div>
<p class="demo-note">A pinned hit gets the left accent rail, soft primary tint and ribbon. Specific to Channels today — not yet a shared component (see "What's NOT in the system yet").</p>`,
});

card({
  path: 'components/insights-lanes.html', group: 'Components', name: 'Insight lanes', viewport: '640x800',
  subtitle: 'Channel-scoped Top / Zero-result / Low-CTR lanes with window pills; hover reveals row actions',
  body: `<div class="gst-prof-ins">
  ${insightsBar()}
  ${insightLanes()}
</div>`,
});

// ── Screens ────────────────────────────────────────────────────────────────
for (const s of Object.values(screens)) {
  card({
    path: `screens/${s.key}.html`, group: 'Screens', name: s.title, viewport: `1600x${s.height}`,
    subtitle: 'Current UI inside the CMS 13 shell — sample data from the Alloy demo site',
    shell: false,
    body: shellFrame({ active: s.active, body: s.body, height: s.height }),
  });
}

// ── readme.md / SKILL.md ───────────────────────────────────────────────────
files['readme.md'] = `# Graph Search Tools — Design System

The admin UI of **UmageAI.Optimizely.GraphSearchTools**, an Optimizely CMS 12/13 add-on
by umage.ai that gives marketers control over Optimizely Graph site search: pinned
results, synonyms, per-channel insights and a live search preview. It lives inside the
Optimizely CMS shell as a set of tool pages, and is styled to feel native to the CMS
admin (Aurora): Inter, oversized light titles, generous whitespace, flat grids with
hair-rules instead of card chrome.

This system is generated from the codebase — \`styles.css\` here **is** the addon's
stylesheet, and every card uses the real \`gst-*\` class names — so a design made with
it is already speaking the vocabulary the code implements.

---

## 1. Product context

- **Repo:** github.com/umage-ai/Umage.Optimizely.GraphSearchtools · package
  \`UmageAI.Optimizely.GraphSearchTools\` on the Optimizely NuGet feed.
- **Surfaces:** Overview (tool dashboard) · Search channels (list + channel detail with
  live preview and a tools console) · Insights (top / zero-result / low-CTR phrases) ·
  Pinned results (pins, collections, changelog) · Synonyms (rules, changelog) · About.
- **Host:** rendered inside Optimizely CMS 13's \`<platform-navigation>\` shell (top bar,
  icon rail, secondary nav). That chrome is drawn in the screen cards as a \`mock-*\`
  frame for context only — it is not ours to restyle.
- **Design docs in the repo:** \`docs/design-system.md\` (the component catalogue and its
  working contract), \`docs/personas.md\`, \`docs/public-api.md\`.

## 2. Who it's for — write and design for the marketer

Primary persona: **the Marketer** — owns the commercial outcome of what visitors see
when they search, measured on conversion and click-through, not a developer.

- **Speak business, not Graph.** "Pinned result for 'sneakers'", never "boost document
  by ID in the semantic channel". GraphQL, scoring and index internals stay behind an
  explicit advanced affordance (the channel Settings tab).
- **Fast confirmation.** Changes should be visible in the live preview immediately.
- **Safe and scoped.** Everything is scoped to a channel / collection / locale, previewable
  and reversible; the changelog records who did what.
- **Vocabulary:** search phrase, result, pin, synonym, channel, collection, locale,
  click-through. Not: document, hit, shard, weight, vector.
- **Tone of copy:** short, sentence case, plain. Empty states and errors suggest the next
  marketing-meaningful action. No emoji anywhere.

Secondary: the developer / solution architect (sets up channels, reads GraphQL docs) and
site ops (health at a glance). Advanced affordances exist for them but are never the
primary call to action.

## 3. Visual foundations

- **Colour** — white page, no tinted backgrounds. Brand blue \`#0042ff\` for the primary
  action and active indicators; link blue \`#0070ee\` for in-text links; \`#e6ecff\` as the
  light brand wash. Neutral greys from \`#f5f6f8\` to \`#1d1f24\`. Status greens/ambers/
  reds each have a light companion for fills. Always reference semantic tokens
  (\`--gst-color-primary\`), never primitives (\`--gst-blue-600\`).
- **Type** — Inter throughout. Page titles are 32px **light (300)**, tight tracking; body
  14px; table headers, tabs and buttons 12px medium; hints and keys 11px. Mono
  (\`--gst-font-mono\`) for channel keys, paths, stats lines.
- **Spacing** — 4 · 8 · 16 · 24 · 32 · 48. Main content pads 32×48. Rows are 14×16
  (compact, edit-in-flyout grids) or 16 (comfortable, navigate grids).
- **Corners & borders** — 4px default radius, 3px small, 6px on cards/KPI strip; 999px
  pills for badges and chips. Structure comes from 1px hair-rules (\`--gst-color-border\`
  and \`-light\`), not boxes.
- **Elevation** — the page is flat. \`--gst-shadow\` lifts a hovered tool card,
  \`--gst-shadow-lg\` popovers, \`--gst-shadow-xl\` the flyout.
- **Motion** — 120–200ms eases; row hover tint, chevron nudge (2px), panel fade-in,
  bar-fill on first paint. Nothing bouncy.

## 4. Iconography

Lucide line icons at stroke 1.5, 24×24 viewBox, \`currentColor\`; scale via width/height.
One icon per concept (registry: search, pin/pinOff, synonym/synonymOff, channels,
insights, details, chevrons, trash, plus, x, refresh, info, copy). Decorative icons carry
\`aria-hidden\`; icon-only buttons carry \`aria-label\`. No emoji, no filled variants.

## 5. Components (see the Components group)

- **Page header** — one h1 + one lede per page; the alert region follows it.
- **Toolbar** — search (placeholder is the label) → filters in column order → spacer →
  one primary action.
- **Two grid blueprints** — *Aurora summary grid* (row opens a flyout, row actions in
  the last column, compact) and *Navigator grid* (row navigates, chevron, comfortable).
  Read-only telemetry tables share the substrate without row-click.
- **Tabs / Segmented** — tabs swap panels (with optional count chips); segmented swaps
  state in place. Never both in one toolbar.
- **KPI tiles with sparkline** — 3–5 tiles, one shared window named in every label.
- **Flyout** — 480px right-edge editor for 2–8 fields; footer order destructive →
  spacer → cancel → primary.
- **Feedback** — alert levels, amber read-only banner, single-CTA empty state, distinct
  no-access placeholders (never a scary error for a permission gap).
- Channel-detail specialisations (console switcher, live preview SERP, insight lanes)
  are one-offs today — promote to shared only with a catalogue entry.

## 6. Working rules that carry over from the repo

- No new shared component or \`gst-*\` class without an entry in \`docs/design-system.md\`.
- Controls a user *could* operate with more permission are disabled with a tooltip, not
  hidden; hiding is reserved for whole surfaces with no view access.
- Everything user-facing is localised (11 languages) — keep labels short and literal.

## 7. Index

- \`styles.css\` — graphsearchtools.css verbatim + the mock shell frame.
- \`foundations/\` — colors, type, spacing, icons.
- \`components/\` — buttons, toolbar, tabs, tables, kpi, page-header, tool-card, flyout,
  feedback, serp, insights-lanes.
- \`screens/\` — the six current screens at 1600px inside the CMS 13 shell.
- Source of this bundle: \`design/ds/build.mjs\` in the repo (\`node design/ds/build.mjs\`).
`;

files['SKILL.md'] = `---
name: graph-search-tools-design
description: Design new features and screens for Graph Search Tools, the umage.ai Optimizely CMS add-on for Optimizely Graph site search (pinned results, synonyms, insights, live preview). Contains the real stylesheet, tokens, component cards and current screens so designs match the codebase.
user-invocable: true
---

Read \`readme.md\` first — it carries the product context, the marketer persona the UI is
written for, and the visual rules. Then browse \`components/\` and \`screens/\` for the
real markup shapes.

When designing a new feature:
- Link \`styles.css\` and build with the existing \`gst-*\` classes (page header → alert
  region → toolbar → grid / tabs / flyout). New UI extends this vocabulary; don't invent
  a parallel one.
- Start from the closest current screen in \`screens/\` and change only what the feature
  needs. Keep the CMS shell frame as-is.
- Copy for marketers: plain, sentence case, business vocabulary, no emoji.
- Prefer the two grid blueprints and the flyout over new patterns; if a genuinely new
  shared component is needed, call it out so it can get a catalogue entry in the repo.
`;

// ── Manifest ───────────────────────────────────────────────────────────────
const kindOf = (n) => n.includes('shadow') ? 'shadow' : n.includes('radius') ? 'radius'
  : /space|row-padding/.test(n) ? 'spacing' : /font|text-/.test(n) ? 'font' : 'color';
files['_ds_manifest.json'] = JSON.stringify({
  namespace: 'GraphSearchTools_2c4a5d',
  components: [],
  startingPoints: [],
  cards,
  templates: [],
  globalCssPaths: ['styles.css'],
  tokens: tokens.map(t => ({ name: t.name, value: t.value, kind: kindOf(t.name), definedIn: 'styles.css' })),
  themes: [],
  fonts: [],
  brandFonts: [{ family: 'Inter', status: 'ok', tokens: ['--gst-font'], path: 'styles.css' }],
  source: 'spa',
}, null, 2) + '\n';

for (const [p, content] of Object.entries(files)) writeFileSync(join(dist, p), content, 'utf8');
console.log(`wrote ${Object.keys(files).length} files (${cards.length} cards) to ${dist}`);
