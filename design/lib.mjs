// Shared source for the design deliverables under design/:
//   design/canvas/build.mjs  → Claude Design canvas artboards (*.dc.html)
//   design/ds/build.mjs      → Claude Design design-system bundle (cards + styles)
//
// Everything visual is lifted from the repo at build time: graphsearchtools.css
// verbatim, the umage watermark SVG, the Lucide icon registry, and the real
// gst-* markup shapes from the Razor views / JS renderers. Only the CMS shell
// chrome (top bar, icon rail, secondary nav) is drawn here as a `mock-*` frame —
// that markup belongs to Optimizely's <platform-navigation>, not to this repo.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
export const repo = join(here, '..');
const addon = join(repo, 'src', 'GraphSearchtools');

export const css = readFileSync(
  join(addon, 'modules/_protected/GraphSearchtools/ClientResources/css/graphsearchtools.css'),
  'utf8'
);
export const watermarkSvg = readFileSync(join(addon, 'Views/Shared/_UmageWatermark.cshtml'), 'utf8')
  .replace(/@\*[\s\S]*?\*@/g, '')
  .trim();

// ── Tokens — parsed from the :root block so cards/manifest never drift ─────
export const tokens = (() => {
  const root = css.slice(css.indexOf(':root {'), css.indexOf('\n}', css.indexOf(':root {')));
  const out = [];
  for (const m of root.matchAll(/(--gst-[\w-]+):\s*([^;]+);/g)) out.push({ name: m[1], value: m[2].trim() });
  return out;
})();
const tokenMap = Object.fromEntries(tokens.map(t => [t.name, t.value]));
export function resolveToken(name, depth = 0) {
  const v = tokenMap[name];
  if (!v || depth > 6) return v ?? '';
  const m = v.match(/^var\((--[\w-]+)\)$/);
  return m ? resolveToken(m[1], depth + 1) : v;
}

// ── Icon registry — mirrors Views/Shared/_Icon.cshtml / GST.icons ─────────
export const ICONS = {
  search: '<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>',
  chevronRight: '<polyline points="9 18 15 12 9 6"/>',
  chevronDown: '<polyline points="6 9 12 15 18 9"/>',
  chevronLeft: '<polyline points="15 18 9 12 15 6"/>',
  pin: '<line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14l-1.5-2.5V8.5h.5a2 2 0 0 0 0-4h-13a2 2 0 0 0 0 4h.5V14.5L5 17z"/>',
  pinOff: '<line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14l-1.5-2.5V8.5h.5a2 2 0 0 0 0-4h-13a2 2 0 0 0 0 4h.5V14.5L5 17z"/><line x1="4" y1="4" x2="20" y2="20"/>',
  synonym: '<polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/>',
  synonymOff: '<polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/><line x1="2" y1="2" x2="22" y2="22"/>',
  channels: '<rect x="3" y="4" width="18" height="6" rx="1"/><rect x="3" y="14" width="11" height="6" rx="1"/><path d="M17 17h4"/>',
  insights: '<line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>',
  details: '<rect x="3" y="3" width="18" height="18" rx="2"/><line x1="7" y1="9" x2="17" y2="9"/><line x1="7" y1="13" x2="17" y2="13"/><line x1="7" y1="17" x2="13" y2="17"/>',
  trash: '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>',
  plus: '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>',
  x: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>',
  refresh: '<polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>',
  info: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>',
  copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
};
export const icon = (name, size = 24, cls = '') =>
  `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"${cls ? ` class="${cls}"` : ''} aria-hidden="true">${ICONS[name]}</svg>`;

// GST.sparkline output shape: an SVG stretched to its host, single polyline.
export function sparkline(series, label, max) {
  const m = max ?? Math.max(...series, 1);
  const W = 100, H = 100;
  const step = series.length > 1 ? W / (series.length - 1) : W;
  const pts = series.map((v, i) => `${(i * step).toFixed(2)},${(H - (v / m) * (H - 6) - 3).toFixed(2)}`).join(' ');
  return `<svg class="gst-sparkline" viewBox="0 0 ${W} ${H}" preserveAspectRatio="none" role="img" aria-label="${label}"><polyline class="gst-sparkline__line" points="${pts}"/></svg>`;
}

// 30-day series shaped like the demo telemetry (steady with one spike).
export const SPARK_SEARCHES = [610,600,615,590,605,620,600,610,595,605,600,590,600,610,605,600,1200,980,720,610,590,540,560,700,760,720,690,640,620,610];
export const SPARK_CTR = [27,26,28,27,27,26,28,27,27,28,26,27,27,27,28,27,26,27,28,27,27,26,27,28,27,27,27,26,27,27];
export const SPARK_ZERO = [18,20,17,22,19,21,18,20,22,19,20,18,21,19,20,22,44,40,30,21,19,18,22,32,35,30,26,22,21,20];

// ── CMS 13 shell frame (approximation of <platform-navigation>) ───────────
const RAIL_ICONS = [
  '<circle cx="12" cy="12" r="9"/><path d="M12 12l3.5-3.5"/><path d="M12 12h.01"/>',
  '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>',
  '<path d="M3 3v18h18"/><path d="M7 15l4-4 3 3 5-6"/>',
  '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
  '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z"/>',
  '<path d="M19.4 13.6a2.2 2.2 0 0 0 0-3.2c-.6-.6-1.5-.7-2.2-.3V7a2 2 0 0 0-2-2h-3.1c.4-.7.3-1.6-.3-2.2a2.2 2.2 0 0 0-3.2 0c-.6.6-.7 1.5-.3 2.2H5a2 2 0 0 0-2 2v3.1c.7-.4 1.6-.3 2.2.3a2.2 2.2 0 0 1 0 3.2c-.6.6-1.5.7-2.2.3V17a2 2 0 0 0 2 2h3.1c-.4.7-.3 1.6.3 2.2a2.2 2.2 0 0 0 3.2 0c.6-.6.7-1.5.3-2.2H15a2 2 0 0 0 2-2v-3.1c.7.4 1.6.3 2.2-.3Z"/>',
];
const NAV_ITEMS = [
  ['overview', 'Overview'],
  ['channels', 'Search channels'],
  ['insights', 'Insights'],
  ['pinned', 'Pinned results'],
  ['synonyms', 'Synonyms'],
  ['about', 'About'],
];

export const SHELL_CSS = `
/* ── CMS 13 shell frame (mock-*) — approximation of Optimizely's
      <platform-navigation>; not part of the addon. ────────────────── */
html, body { margin: 0; padding: 0; }
body { background: #ffffff; }
.mock-root { font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; color: #1d1f24; background: #ffffff; -webkit-font-smoothing: antialiased; display: flex; flex-direction: column; }
.mock-topbar { height: 44px; background: #080736; color: #ffffff; display: flex; align-items: center; padding: 0 20px 0 14px; gap: 14px; }
.mock-topbar__brand { display: flex; align-items: center; gap: 10px; font-weight: 700; font-size: 15px; letter-spacing: -0.01em; }
.mock-topbar__mark { width: 26px; height: 26px; }
.mock-topbar__divider { width: 1px; height: 44px; background: rgba(255,255,255,0.18); }
.mock-topbar__product { display: flex; align-items: center; gap: 6px; font-size: 15px; font-weight: 500; }
.mock-topbar__spacer { flex: 1; }
.mock-topbar__actions { display: flex; align-items: center; gap: 22px; }
.mock-topbar__actions svg { width: 20px; height: 20px; }
.mock-body { display: flex; align-items: stretch; flex: 1; }
.mock-rail { width: 58px; flex: 0 0 58px; border-right: 1px solid #e2e4e9; display: flex; flex-direction: column; align-items: center; gap: 16px; padding: 18px 0; color: #595d65; background: #ffffff; }
.mock-rail svg { width: 20px; height: 20px; }
.mock-nav { width: 316px; flex: 0 0 316px; border-right: 1px solid #e2e4e9; background: #ffffff; }
.mock-nav__title { font-size: 20px; font-weight: 400; color: #1d1f24; padding: 42px 28px 22px; letter-spacing: -0.01em; }
.mock-nav__list { display: flex; flex-direction: column; }
.mock-nav__item { display: flex; align-items: center; height: 53px; padding: 0 28px; font-size: 15px; color: #2d5bd9; border-top: 1px solid #e2e4e9; text-decoration: none; }
.mock-nav__item:last-child { border-bottom: 1px solid #e2e4e9; }
.mock-nav__item.is-active { background: #eef2fb; }
.mock-content { flex: 1; min-width: 0; }
/* The frame is the viewport: keep the shell's sticky header put. */
.mock-content .gst-shell { min-height: 0; }
.mock-content .gst-header { position: static; }
`;

/** Full CMS-13 frame with the addon shell inside. Returns the .mock-root element. */
export function shellFrame({ active, body, height, width = 1600 }) {
  const nav = NAV_ITEMS.map(([k, label]) =>
    `<a class="mock-nav__item${k === active ? ' is-active' : ''}" href="#">${label}</a>`).join('\n          ');
  const rail = RAIL_ICONS.map(p =>
    `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${p}</svg>`).join('\n          ');
  return `<div class="mock-root" style="width: ${width}px; min-height: ${height}px;">
  <div class="mock-topbar">
    <div class="mock-topbar__brand">
      <svg class="mock-topbar__mark" viewBox="0 0 24 24" fill="none" stroke="#ffffff" stroke-width="2.2" stroke-linecap="round" aria-hidden="true"><path d="M5 18c4-1 6-5 6-9"/><path d="M11 9c0 5 3 8 8 9"/><circle cx="11" cy="9" r="2.2" fill="#ffffff" stroke="none"/></svg>
      <span>Optimizely</span>
    </div>
    <div class="mock-topbar__divider"></div>
    <div class="mock-topbar__product"><span>CMS</span>${icon('chevronDown', 14)}</div>
    <div class="mock-topbar__spacer"></div>
    <div class="mock-topbar__actions">
      ${icon('search', 20)}
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3"/><path d="M12 17h.01"/></svg>
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
    </div>
  </div>
  <div class="mock-body">
    <div class="mock-rail">
          ${rail}
    </div>
    <nav class="mock-nav">
      <div class="mock-nav__title">Graph Search Tools</div>
      <div class="mock-nav__list">
          ${nav}
      </div>
    </nav>
    <div class="mock-content">
      <div class="gst-shell">
        <header class="gst-header">
          <a href="#" class="gst-header__logo">
            ${icon('search', 20, 'gst-header__icon')}
            Graph Search Tools
          </a>
          <nav class="gst-header__nav"></nav>
          <a href="#" class="gst-header__about">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
            About
          </a>
        </header>
        <main class="gst-main">
          <a class="gst-watermark" href="#" title="Powered by umage.ai">
            ${watermarkSvg}
          </a>
${body}
        </main>
      </div>
    </div>
  </div>
</div>`;
}

// ── Shared fragments (real markup shapes from the views / JS) ─────────────
export const searchInput = (id, placeholder) =>
  `<div class="gst-search"><input type="text" id="${id}" placeholder="${placeholder}" /></div>`;

export const filter = (label, value) =>
  `<label class="gst-filter"><span class="gst-filter__label">${label}</span><select class="gst-filter__select"><option>${value}</option></select></label>`;

export const kpis = (id) => `
<div id="${id}" class="gst-kpis" aria-live="polite">
  <div class="gst-kpi" data-kpi="searches">
    <div class="gst-kpi__label">Searches <span class="gst-muted">(last 30 days)</span></div>
    <div class="gst-kpi__value">19K</div>
    <div class="gst-kpi__spark">${sparkline(SPARK_SEARCHES, 'Daily searches')}</div>
  </div>
  <div class="gst-kpi" data-kpi="ctr">
    <div class="gst-kpi__label">Click-through rate <span class="gst-muted">(last 30 days)</span></div>
    <div class="gst-kpi__value">27%</div>
    <div class="gst-kpi__spark">${sparkline(SPARK_CTR, 'Daily click-through rate', 100)}</div>
  </div>
  <div class="gst-kpi" data-kpi="zero">
    <div class="gst-kpi__label">Zero-result searches <span class="gst-muted">(last 30 days)</span></div>
    <div class="gst-kpi__value">676</div>
    <div class="gst-kpi__spark">${sparkline(SPARK_ZERO, 'Daily zero-result searches')}</div>
  </div>
</div>`;

export const tabs = (items, ariaLabel) =>
  `<nav class="gst-tabs" role="tablist" aria-label="${ariaLabel}">
${items.map(([label, active, count]) =>
  `  <button type="button" class="gst-tabs__btn${active ? ' is-active' : ''}" role="tab" aria-selected="${active ? 'true' : 'false'}">${label}${count != null ? `<span class="gst-tab__count">${count}</span>` : ''}</button>`).join('\n')}
</nav>`;

export const toolCards = [
  ['channels', 'Search channels', 'Tune each registered search surface — pins, synonyms, and insights scoped to one channel.'],
  ['insights', 'Insights', 'What people search for across every channel — top phrases, zero-result phrases, and low-CTR phrases. Filter by channel to narrow.'],
  ['pin', 'Pinned results', 'Pin specific content to the top of the results for chosen phrases, per collection and locale.'],
  ['synonym', 'Synonyms', 'Manage replacement and equivalent synonym rules per locale. Changes take effect immediately.'],
];
export const toolCard = ([ic, t, d], disabled = false) => disabled
  ? `<div class="gst-tool-card disabled" title="No access" aria-disabled="true">
    ${icon(ic, 28, 'gst-tool-card__icon')}
    <div class="gst-tool-card__title">${t}</div>
    <div class="gst-tool-card__desc">${d}</div>
    <div class="gst-tool-card__noaccess">No access</div>
  </div>`
  : `<a class="gst-tool-card" href="#">
    ${icon(ic, 28, 'gst-tool-card__icon')}
    <div class="gst-tool-card__title">${t}</div>
    <div class="gst-tool-card__desc">${d}</div>
  </a>`;

export const channelRow = () => `<tr class="is-selectable">
          <td><div class="gst-prof-name"><div class="gst-prof-name__title">Alloy site search</div><div class="gst-prof-name__key">alloy-search</div></div></td>
          <td class="col-scope"><div class="gst-prof-scope"><span class="gst-badge gst-badge--default">all sites</span><span class="gst-badge gst-badge--primary">en</span><span class="gst-badge gst-badge--primary">sv</span></div></td>
          <td class="col-activity"><div class="gst-prof-activity"><div class="gst-prof-activity__spark">${sparkline(SPARK_SEARCHES, 'Activity (30d)')}</div><span class="gst-prof-activity__total">18K</span></div></td>
          <td><span class="gst-prof-status__line">1 days ago · Admin</span></td>
          <td>${icon('chevronRight', 24, 'gst-prof-arrow')}</td>
        </tr>`;

export const navTable = (rows = channelRow()) => `<table class="gst-table gst-nav-table">
      <colgroup><col style="width: 26%"/><col style="width: 22%"/><col style="width: 32%"/><col/><col style="width: 32px"/></colgroup>
      <thead><tr><th>Channel</th><th class="col-scope">Sites &amp; locales</th><th class="col-activity">Activity (30d)</th><th>Last edited</th><th></th></tr></thead>
      <tbody>
        ${rows}
      </tbody>
    </table>`;

export const PINS = [
  ['alloy plan', 'alloy-sv', 'sv', '2,128'], ['alloy plan', 'alloy-en', 'en', '2,128'],
  ['alloy track', 'alloy-sv', 'sv', '1,944'], ['alloy track', 'alloy-en', 'en', '1,944'],
  ['alloy meet', 'alloy-sv', 'sv', '1,769'], ['alloy meet', 'alloy-en', 'en', '1,769'],
  ['demo', 'alloy-sv', 'sv', '1,276'], ['demo', 'alloy-en', 'en', '1,276'],
  ['pricing', 'alloy-en', 'en', '1,218'],
  ['support', 'alloy-sv', 'sv', '1,096'], ['support', 'alloy-en', 'en', '1,096'],
  ['contact', 'alloy-en', 'en', '987'],
];
export const pinTable = (rows = PINS) => `<table class="gst-table gst-pin-aurora-table">
    <thead><tr><th data-sort="phrase">Phrase</th><th data-sort="collection" class="col-collection">Collection</th><th data-sort="locale">Locale</th><th data-sort="items">Pinned items</th><th data-sort="activity">Activity (30d)</th><th aria-label="Actions"></th></tr></thead>
    <tbody>
${rows.map(([p, c, l, a]) => `      <tr class="is-selectable"><td><a href="#" class="gst-table__link">${p}</a></td><td class="col-collection">${c}</td><td>${l}</td><td>1 items</td><td>${a}</td><td class="gst-table__actions"></td></tr>`).join('\n')}
    </tbody>
  </table>`;

export const RULES = [
  ['demo, trial, free trial, evaluation', 'en', '1,276'],
  ['demo, testversion, provversion, utvärdering', 'sv', '1,276'],
  ['pricing, price, cost, plans, plan pricing', 'en', '1,218'],
  ['support, help, assistance, customer service', 'en', '1,096'],
  ['support, hjälp, kundtjänst', 'sv', '1,096'],
  ['contact, contact us, get in touch, reach out', 'en', '987'],
  ['news, press, press release, announcements', 'en', '802'],
  ['partner, reseller, channel partner, integrator', 'en', '685'],
];
export const synonymTable = (rows = RULES) => `<table class="gst-table gst-syn-aurora-table">
    <thead><tr><th data-sort="rule">Rule</th><th data-sort="locale">Locale</th><th data-sort="activity">Activity (30d)</th><th aria-label="Actions"></th></tr></thead>
    <tbody>
${rows.map(([r, l, a]) => `      <tr class="is-selectable"><td><a href="#" class="gst-table__link">${r}</a></td><td>${l}</td><td class="num">${a}</td><td class="gst-table__actions"><button type="button" class="gst-rowdelete" aria-label="Delete">${icon('trash', 16)}</button></td></tr>`).join('\n')}
    </tbody>
  </table>`;

export const HITS = [
  ['<mark class="gst-serp__mark">Alloy</mark> <mark class="gst-serp__mark">Plan</mark>', '/en/alloy-plan/', '<mark class="gst-serp__mark">Alloy Plan</mark>', 'ProductPage'],
  ['Trek Selects <mark class="gst-serp__mark">Alloy</mark> <mark class="gst-serp__mark">Plan</mark>', '/en/about-us/news-events/press-releases/trek-selects-alloy-plan/', 'Trek Selects <mark class="gst-serp__mark">Alloy Plan</mark>', 'ArticlePage'],
  ['Start', '/en/', '<mark class="gst-serp__mark">Alloy Plan</mark> … <mark class="gst-serp__mark">Alloy</mark> Track … <mark class="gst-serp__mark">Alloy</mark> Meet', 'StartPage'],
  ['<mark class="gst-serp__mark">Alloy</mark> Track', '/en/alloy-track/', '<mark class="gst-serp__mark">Alloy</mark> Track', 'ProductPage'],
  ['<mark class="gst-serp__mark">Alloy</mark> Saves Bears', '/en/about-us/news-events/press-releases/newworld-wildlife-fund-chooses-alloy/', '<mark class="gst-serp__mark">Alloy</mark> Saves Bears', 'ArticlePage'],
  ['<mark class="gst-serp__mark">Alloy</mark> Meet', '/en/alloy-meet/', '<mark class="gst-serp__mark">Alloy</mark> Meet', 'ProductPage'],
  ['About us', '/en/about-us/', '<mark class="gst-serp__mark">Alloy</mark> Saves Bears', 'StandardPage'],
];
export const serpHit = ([title, url, snippet, type], pinned = false) => `<li class="gst-serp__hit${pinned ? ' is-pinned' : ''}">
              ${pinned ? `<span class="gst-serp__pin-ribbon">${icon('pin', 14)}</span>` : ''}
              <div class="gst-serp__hit-head"><a class="gst-serp__title" href="#">${title}</a></div>
              <p class="gst-serp__url"><span class="gst-serp__url-glyph">›</span> ${url}</p>
              <p class="gst-serp__snippet">${snippet}</p>
              <div class="gst-serp__meta">${pinned ? '<span class="gst-serp__chip gst-serp__chip--pinned">Pinned</span>' : ''}<span class="gst-serp__chip">${type}</span><span class="gst-serp__lang">en</span></div>
            </li>`;
export const serp = ({ hits = HITS, pinnedFirst = false, query = 'Alloy Plan' } = {}) => `<div class="gst-serp gst-prof-preview__serp">
          <label class="gst-serp__input">
            ${icon('search', 14, 'gst-serp__glyph')}
            <input type="text" value="${query}" placeholder="Try a search phrase…" autocomplete="off" />
            <span class="gst-serp__spinner" aria-hidden="true"></span>
          </label>
          <div class="gst-serp__stats" aria-live="polite">${hits.length} hits · 843 ms</div>
          <ul class="gst-serp__results">
${hits.map((h, i) => '            ' + serpHit(h, pinnedFirst && i === 0)).join('\n')}
          </ul>
        </div>`;

export const TOP = [['Alloy Plan', 638, 100], ['Alloy Track', 607, 95], ['Alloy Meet', 549, 86], ['pricing', 436, 68], ['demo', 366, 57]];
export const ZERO = [['gdpr', 41], ['changelog', 29], ['sla', 29], ['invoice', 24], ['cancel subscription', 19]];
export const LOWCTR = [['compliance', '0%', 260], ['integration', '9%', 229], ['gdpr', '0%', 41], ['changelog', '0%', 29], ['sla', '0%', 29]];

const laneHead = (title, hint, count) => `
      <header class="gst-prof-ins-lane__head">
        <span class="gst-prof-ins-lane__rule" aria-hidden="true"></span>
        <h3 class="gst-prof-ins-lane__title">${title}</h3>
        <span class="gst-prof-ins-lane__hint">${hint}</span>
        <span class="gst-prof-ins-lane__count" data-window="7d">${count}</span>
        <a class="gst-prof-ins-lane__deeplink" href="#">More →</a>
      </header>`;
const rowActions = `<span class="gst-prof-ins-row__actions"><button type="button" class="gst-prof-ins-row__icon" aria-label="Preview this phrase">${icon('search', 14)}</button><button type="button" class="gst-prof-ins-row__icon" aria-label="Pin">${icon('pin', 14)}</button><button type="button" class="gst-prof-ins-row__icon" aria-label="Add synonym">${icon('synonym', 14)}</button></span>`;
const hitCount = (n) => `<span class="gst-prof-ins-row__count"><strong>${n}</strong><small>hits</small></span>`;

export const insightLanes = () => `<div class="gst-prof-ins__lanes">
          <section class="gst-prof-ins-lane gst-prof-ins-lane--top" data-lane="top">${laneHead('Top phrases', 'What people are searching for', 5)}
            <ol class="gst-prof-ins-lane__list">
${TOP.map(([p, n, w], i) => `              <li class="gst-prof-ins-row${i === 0 ? ' is-active' : ''}" style="--w: ${w}%"><span class="gst-prof-ins-row__phrase">${p}</span><span class="gst-prof-ins-row__bar"></span>${hitCount(n)}${rowActions}</li>`).join('\n')}
            </ol>
            <button type="button" class="gst-prof-ins-lane__more">Show more</button>
          </section>
          <section class="gst-prof-ins-lane gst-prof-ins-lane--zero" data-lane="zero">${laneHead('Zero-result phrases', 'Synonym-mining candidates', 5)}
            <ol class="gst-prof-ins-lane__list">
${ZERO.map(([p, n]) => `              <li class="gst-prof-ins-row"><span class="gst-prof-ins-row__phrase">${p}</span><span class="gst-prof-ins-row__bar"><span class="gst-prof-ins-row__miss"></span><span class="gst-prof-ins-row__miss"></span><span class="gst-prof-ins-row__miss"></span><span class="gst-prof-ins-row__miss"></span></span>${hitCount(n)}${rowActions}</li>`).join('\n')}
            </ol>
            <button type="button" class="gst-prof-ins-lane__more">Show more</button>
          </section>
          <section class="gst-prof-ins-lane gst-prof-ins-lane--lowctr" data-lane="lowctr">${laneHead('Low-CTR phrases', 'Pin-candidate phrases', 5)}
            <ol class="gst-prof-ins-lane__list">
${LOWCTR.map(([p, ctr, n]) => `              <li class="gst-prof-ins-row"><span class="gst-prof-ins-row__phrase">${p}</span><span class="gst-prof-ins-row__bar"><span class="gst-prof-ins-row__ctr"><span class="gst-prof-ins-row__ctr-num">${ctr}</span>CTR</span></span>${hitCount(n)}${rowActions}</li>`).join('\n')}
            </ol>
            <button type="button" class="gst-prof-ins-lane__more">Show more</button>
          </section>
        </div>`;

export const insightsBar = () => `<header class="gst-prof-ins__bar">
          <div class="gst-segmented" role="group" aria-label="Time window">
            <button type="button" class="gst-segmented__btn">24h</button>
            <button type="button" class="gst-segmented__btn is-active">7d</button>
            <button type="button" class="gst-segmented__btn">30d</button>
          </div>
          <span class="gst-prof-ins__bar-spacer"></span>
          <button type="button" class="gst-prof-ins__refresh" aria-label="Refresh">${icon('refresh', 14)}</button>
        </header>`;

export const profSwitcher = ({ unwired = [] } = {}) => `<nav class="gst-prof-switcher" role="tablist" aria-label="Tool">
      <button type="button" class="gst-tabs__btn is-active" role="tab" aria-selected="true"><span class="gst-prof-switcher__icon">${icon('insights', 14)}</span><span>Insights</span></button>
      <button type="button" class="gst-tabs__btn${unwired.includes('pinned') ? ' is-unwired' : ''}" role="tab" aria-selected="false"><span class="gst-prof-switcher__icon">${icon('pin', 14)}</span><span>Pinned</span></button>
      <button type="button" class="gst-tabs__btn${unwired.includes('synonyms') ? ' is-unwired' : ''}" role="tab" aria-selected="false"><span class="gst-prof-switcher__icon">${icon('synonym', 14)}</span><span>Synonyms</span></button>
      <button type="button" class="gst-tabs__btn" role="tab" aria-selected="false"><span class="gst-prof-switcher__icon">${icon('details', 14)}</span><span>Settings</span></button>
    </nav>`;

// ── The six screens ────────────────────────────────────────────────────────
export const INS_ROWS = [
  ['Alloy Plan', 638, 0], ['Alloy Track', 607, 0], ['Alloy Meet', 549, 0], ['pricing', 436, 0], ['demo', 366, 0],
  ['contact', 337, 0], ['compliance', 260, 0], ['integration', 229, 0], ['roadmap', 178, 0], ['support', 159, 0],
  ['gdpr', 41, 41], ['changelog', 29, 29], ['sla', 29, 29], ['invoice', 24, 24], ['cancel subscription', 19, 19],
];

export const screens = {
  overview: {
    key: 'overview', file: 'Main', title: 'Overview', active: 'overview', height: 1000,
    body: `
<div class="gst-page-header">
  <h1>Graph Search Tools</h1>
  <p>Dashboard of all Graph Search Tools.</p>
</div>
<div class="gst-tool-grid" style="display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px;">
${toolCards.map(c => '  ' + toolCard(c)).join('\n')}
</div>`,
  },
  channels: {
    key: 'channels', file: 'Channels', title: 'Search channels', active: 'channels', height: 1000,
    body: `
<div class="gst-page-header">
  <h1>Search Channels</h1>
  <p>Marketer-facing tuning for each search surface.</p>
</div>
<div class="gst-card">
  <div class="gst-toolbar">
    ${searchInput('gst-prof-search', 'Filter channels…')}
    ${filter('Site', 'All sites')}
    ${filter('Locale', 'Global')}
    <div class="gst-toolbar__spacer"></div>
    <span id="gst-prof-count" class="gst-muted">1 channel</span>
  </div>
  <div id="gst-prof-table-host">
    ${navTable()}
  </div>
</div>`,
  },
  channelDetail: {
    key: 'channelDetail', file: 'ChannelDetail', title: 'Channel detail', active: 'channels', height: 1560,
    body: `
<div class="gst-page-header gst-prof-detail-header">
  <div class="gst-page-header__title">
    <div class="gst-prof-detail-header__crumb">
      <a href="#" class="gst-prof-detail-header__back">${icon('chevronLeft', 14)}Back to all channels</a>
      <span class="gst-prof-detail-header__sep" aria-hidden="true">·</span>
      <code class="gst-prof-detail-header__key">alloy-search</code>
    </div>
    <h1>Alloy site search</h1>
    <p>Header search across the Alloy demo content.</p>
  </div>
</div>
${kpis('gst-prof-kpis')}
<div class="gst-prof-workspace" data-channel-key="alloy-search">
  <section class="gst-prof-preview" aria-label="Live preview">
    <div class="gst-prof-preview__inner">
      <header class="gst-prof-preview__head">
        <div class="gst-prof-preview__heading">
          <span class="gst-prof-preview__eyebrow">Try-it</span>
          <h2 class="gst-prof-preview__title">Live preview</h2>
        </div>
        <div class="gst-prof-preview__pickers" role="group" aria-label="Scope">
          <div class="gst-filter gst-prof-preview__filter gst-filter--static">
            <span class="gst-filter__label">Site</span>
            <span class="gst-filter__static-value">all sites</span>
          </div>
          <label class="gst-filter gst-prof-preview__filter">
            <span class="gst-filter__label">Locale</span>
            <select class="gst-filter__select"><option>en</option><option>sv</option></select>
          </label>
        </div>
      </header>
      <div class="gst-prof-preview__body">
        ${serp()}
      </div>
    </div>
  </section>
  <section class="gst-prof-console" aria-label="Tools">
    ${profSwitcher()}
    <article class="gst-prof-panel is-active" data-panel="insights" role="tabpanel">
      <div class="gst-prof-ins">
        ${insightsBar()}
        ${insightLanes()}
        <p class="gst-prof-ins__footnote gst-muted" style="font-size: 11px; margin: 0;">Telemetry comes from the storefront. Phrases are scoped to this channel and grouped case-insensitively.</p>
      </div>
    </article>
  </section>
</div>`,
  },
  insights: {
    key: 'insights', file: 'Insights', title: 'Insights', active: 'insights', height: 1440,
    body: `
<div class="gst-page-header">
  <h1>Insights</h1>
  <p>What people search for across every channel — top phrases, zero-result phrases, and low-CTR phrases. Filter by channel to narrow.</p>
</div>
<div class="gst-card gst-sl-card gst-sl-card--wide" data-card="kpis">
${kpis('gst-insights-kpis')}
</div>
<div class="gst-card">
  ${tabs([['Top phrases', true, 15], ['Zero-result', false, 5], ['Low-CTR', false, 7]], 'Insights')}
  <div class="gst-toolbar gst-sl-toolbar">
    ${searchInput('gst-insights-phrase-filter', 'Filter phrases…')}
    ${filter('Channel', 'All channels')}
    ${filter('Locale', 'All locales')}
    ${filter('Window', 'Last 7d')}
    <div class="gst-toolbar__spacer"></div>
  </div>
  <div class="gst-ins-panels">
    <section class="gst-ins-tabpanel is-active" data-lane="top" role="tabpanel">
      <table class="gst-table gst-ins-aurora-table" data-lane="top">
        <thead><tr><th data-sort="phrase">Phrase</th><th data-sort="channel">Channel</th><th data-sort="locale">Locale</th><th data-sort="hits" data-sort-dir="desc" class="num">Hits</th><th data-sort="zero" class="num">Zero results</th></tr></thead>
        <tbody>
${INS_ROWS.map(([p, h, z]) => `          <tr><td class="gst-ins-row__phrase">${p}</td><td><a class="gst-ins-row__channel" href="#">alloy-search</a></td><td>en</td><td class="num">${h}</td><td class="num${z === 0 ? ' gst-muted' : ''}">${z}</td></tr>`).join('\n')}
        </tbody>
      </table>
    </section>
  </div>
</div>`,
  },
  pinned: {
    key: 'pinned', file: 'Pinned', title: 'Pinned results', active: 'pinned', height: 1200,
    body: `
<div class="gst-page-header">
  <h1>Pinned results</h1>
  <p>Pin specific content to the top of the results for chosen phrases, per collection and locale.</p>
</div>
${tabs([['Pins', true], ['Collections', false], ['Changelog', false]], 'Pinned views')}
<section class="gst-tabpanel" data-tab="pins" role="tabpanel">
  <div class="gst-toolbar">
    ${searchInput('gst-pin-search', 'Filter by phrase or content…')}
    ${filter('Collection', 'All collections')}
    ${filter('Locale', 'All')}
    <div class="gst-toolbar__spacer"></div>
    <button type="button" id="gst-pin-create" class="gst-btn gst-btn--primary">+ Add pin</button>
  </div>
  ${pinTable()}
</section>`,
  },
  synonyms: {
    key: 'synonyms', file: 'Synonyms', title: 'Synonyms', active: 'synonyms', height: 1120,
    body: `
<div class="gst-page-header">
  <h1>Synonyms</h1>
  <p>Manage replacement and equivalent synonym rules per locale. Changes take effect immediately.</p>
</div>
${tabs([['Rules', true], ['Changelog', false]], 'Synonym views')}
<section class="gst-tabpanel" data-tab="rules" role="tabpanel">
  <div class="gst-toolbar">
    ${searchInput('gst-syn-search', 'Filter rules…')}
    ${filter('Locale', 'All')}
    <div class="gst-toolbar__spacer"></div>
    <button type="button" id="gst-syn-create" class="gst-btn gst-btn--primary">+ Add rule</button>
  </div>
  ${synonymTable()}
</section>`,
  },
};
