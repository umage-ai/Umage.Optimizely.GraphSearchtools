/**
 * Insights — global cross-channel view.
 *
 * Page layout:
 *   1. 30d KPI strip    — GET InsightsApi/SearchKpis (always 30d, sparklines).
 *                         Clicking a sparkline day filters the active lane
 *                         tab to that single UTC day.
 *   2. Toolbar          — Window select (7d/30d), Channel select, Locale
 *                         select. Lives below the KPIs because these
 *                         controls only scope the tables.
 *   3. Tab strip        — one tab per lane (Top / Zero-result / Low-CTR),
 *                         each a single full-width Aurora-style table.
 *                         Endpoints:
 *                            Top    → GET InsightsApi/TopPhrases
 *                            Zero   → GET InsightsApi/ZeroResultPhrases
 *                            Low    → GET InsightsApi/LowCtrPhrases
 *
 * Toolbar inputs (window / channel / locale) apply to whichever tab is
 * currently visible; switching them marks other tabs stale so they re-fetch
 * on next select. Default sort is hits-desc; column header clicks re-sort
 * client-side without a re-fetch. Deep-link via
 *   #tab=top&channel=<key>&locale=<code>&days=30&sort=hits:desc
 */
(function () {
    var BASE = window.GST_BASE_URL || '';
    var API = BASE + '/InsightsApi';
    // Channels list lives at an absolute, well-known route — not under BASE.
    var CHANNELS_API = '/EPiServer/cms/graphsearchtools/api/channels';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.insights) || {};

    var LANE_ENDPOINT = {
        top:    '/TopPhrases',
        zero:   '/ZeroResultPhrases',
        lowctr: '/LowCtrPhrases'
    };
    // Soft cap on rows per lane. Real telemetry rarely produces more unique
    // phrases than this in a 7–30 day window; if a tenant hits the cap, the
    // tab silently shows the cap value with no overflow indicator (acceptable
    // for v1 — bump to 200+ truncated-flag UX later if anyone bumps into it).
    var MAX_ROWS = 200;
    var LANES = ['top', 'zero', 'lowctr'];

    // Columns each lane's table can actually sort on. Used to validate the
    // user's per-lane sort pick against what the lane can actually render.
    var LANE_SORT_COLS = {
        top:    ['phrase', 'channel', 'locale', 'hits', 'zero'],
        zero:   ['phrase', 'channel', 'locale', 'hits'],
        lowctr: ['phrase', 'channel', 'locale', 'hits', 'ctr']
    };
    // Per-lane default sort applied until the user explicitly clicks a header
    // on that lane. Low-CTR opens to its load-bearing signal (worst CTR first)
    // rather than the volume that brought the phrase into the list.
    var LANE_DEFAULT_SORT = {
        top:    { col: 'hits', dir: 'desc' },
        zero:   { col: 'hits', dir: 'desc' },
        lowctr: { col: 'ctr',  dir: 'asc'  }
    };

    var state = {
        tab: 'top',
        days: 7,
        channel: '',
        locale: '',
        // Free-text filter on row.phrase, applied client-side over the
        // cached rows. Intentionally not in the URL hash — scratch usage,
        // doesn't survive page reload.
        phrase: '',
        // When non-null, the active lane is scoped to this UTC day and
        // `days` is ignored. Set by a sparkline click on the KPI card;
        // cleared by re-clicking the same day, by the × on the filter chip,
        // or by picking a window pill.
        dateFilter: null,
        // Per-lane explicit sort pick. null means "use the lane default";
        // populated lazily when the user clicks a column header on that lane.
        // Per-lane (not shared) so each tab opens to its own default the first
        // time the user lands on it.
        sort: { top: null, zero: null, lowctr: null },
        // Cache of the last fetched rows per lane. Header clicks re-sort
        // out of this cache instead of round-tripping the API.
        rows:    { top: [], zero: [], lowctr: [] },
        // Each lane gets marked stale when toolbar inputs change; we only
        // re-fetch the lanes the user looks at, so switching tabs after a
        // toolbar change loads fresh data on demand.
        loaded: { top: false, zero: false, lowctr: false },
        inflight: { top: null, zero: null, lowctr: null }
    };

    document.addEventListener('DOMContentLoaded', init);

    function init() {
        readHash();
        wireTabs();
        wirePhraseFilter();
        wireChannelFilter();
        wireLocaleFilter();
        wireWindowFilter();
        wireSortHeaders();
        applySortIndicatorToActiveTab();
        reloadKpis();
        loadChannels().finally(function () {
            // Selects are populated now (or only the "All" option if the load
            // failed). Activate the visual tab state, then prefetch every
            // lane in parallel so all three count chips populate on first
            // paint (otherwise the inactive tabs would read empty until the
            // user visited them).
            showTab(state.tab);
            LANES.forEach(function (l) {
                if (l !== state.tab && !state.loaded[l]) fetchLane(l);
            });
        });
        window.addEventListener('hashchange', function () {
            readHash();
            syncToolbarToState();
            applySortIndicatorToActiveTab();
            showTab(state.tab);
        });
    }

    // ── Toolbar wiring ────────────────────────────────────────────────

    function wireTabs() {
        document.querySelectorAll('.gst-tabs__btn[data-tab]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var lane = btn.dataset.tab;
                if (!lane || lane === state.tab) return;
                state.tab = lane;
                writeHash();
                showTab(lane);
            });
        });
    }

    function wireWindowFilter() {
        var sel = document.getElementById('gst-insights-window-filter');
        if (!sel) return;
        sel.value = String(state.days);
        sel.addEventListener('change', function () {
            var d = parseInt(sel.value, 10) || 7;
            if (d === state.days) return;
            state.days = d;
            writeHash();
            clearDateFilter(/* silent: */ true);
            invalidateAll();
            fetchAllLanes();
        });
    }

    function wirePhraseFilter() {
        var input = document.getElementById('gst-insights-phrase-filter');
        if (!input) return;
        input.addEventListener('input', function () {
            state.phrase = (input.value || '').trim().toLowerCase();
            repaintActiveTabFromCache();
            // Phrase is client-side over cached rows, so every tab's count
            // can update without re-fetching anything.
            updateAllTabCounts();
        });
    }

    function wireChannelFilter() {
        var sel = document.getElementById('gst-insights-channel-filter');
        if (!sel) return;
        sel.addEventListener('change', function () {
            var k = sel.value || '';
            if (k === state.channel) return;
            state.channel = k;
            writeHash();
            invalidateAll();
            reloadKpis();
            fetchAllLanes();
        });
    }

    function wireLocaleFilter() {
        var sel = document.getElementById('gst-insights-locale-filter');
        if (!sel) return;
        sel.addEventListener('change', function () {
            var v = sel.value || '';
            if (v === state.locale) return;
            state.locale = v;
            writeHash();
            invalidateAll();
            fetchAllLanes();
        });
    }

    // Load /api/channels once, populate both the Channel and Locale dropdowns.
    // Locale options are unioned from every channel's declared locale list —
    // a channel that doesn't list a locale won't surface it as a filter even
    // if telemetry exists for it; that's a reasonable simplification given
    // those rows would be vestigial anyway.
    function loadChannels() {
        var profSel = document.getElementById('gst-insights-channel-filter');
        var locSel  = document.getElementById('gst-insights-locale-filter');
        if (!profSel && !locSel) return Promise.resolve();
        return GST.fetchJson(CHANNELS_API).then(function (rows) {
            if (!Array.isArray(rows)) return;
            var locales = {};
            rows.forEach(function (p) {
                if (profSel) {
                    var opt = document.createElement('option');
                    opt.value = p.key || '';
                    opt.textContent = p.displayName || p.key || '';
                    opt.title = p.key || '';
                    profSel.appendChild(opt);
                }
                if (Array.isArray(p.locales)) {
                    p.locales.forEach(function (l) { if (l) locales[l] = true; });
                }
            });
            if (locSel) {
                Object.keys(locales).sort().forEach(function (l) {
                    var opt = document.createElement('option');
                    opt.value = l;
                    opt.textContent = l;
                    locSel.appendChild(opt);
                });
            }
            // Reflect the state set from the hash (if any).
            syncToolbarToState();
        }).catch(function () {
            // Silently leave the toolbar with only "All" — the lane will still
            // load. The error surface for failed lane requests is enough.
        });
    }

    // ── Sort wiring ───────────────────────────────────────────────────

    function wireSortHeaders() {
        document.querySelectorAll('.gst-ins-aurora-table thead th[data-sort]').forEach(function (th) {
            var table = th.closest('.gst-ins-aurora-table');
            var lane = table && table.getAttribute('data-lane');
            if (!lane) return;
            th.addEventListener('click', function () {
                var col = th.getAttribute('data-sort');
                if (!col) return;
                var cur = effectiveSort(lane);
                var dir;
                if (cur.col === col) {
                    dir = (cur.dir === 'asc') ? 'desc' : 'asc';
                } else {
                    // Numeric columns feel right defaulting to desc (highest
                    // first); textual columns to asc (A→Z).
                    dir = isNumericCol(col) ? 'desc' : 'asc';
                }
                state.sort[lane] = { col: col, dir: dir };
                writeHash();
                applySortIndicatorToActiveTab();
                repaintActiveTabFromCache();
            });
        });
    }

    function isNumericCol(col) { return col === 'hits' || col === 'zero' || col === 'ctr'; }

    // Client-side phrase filter — case-insensitive substring on row.phrase.
    // No-op when the input is empty so the same path serves the "no filter"
    // case without a special check upstream.
    function applyPhraseFilter(rows) {
        if (!state.phrase) return rows;
        var q = state.phrase;
        return rows.filter(function (r) {
            return (r.phrase || '').toLowerCase().indexOf(q) !== -1;
        });
    }

    // The effective sort for a lane — the user's explicit pick if any,
    // else the lane's default. Picks that name a column the lane doesn't
    // carry (e.g. ctr on Top) silently fall back to the default.
    function effectiveSort(lane) {
        var def = LANE_DEFAULT_SORT[lane] || { col: 'hits', dir: 'desc' };
        var pick = state.sort[lane];
        if (!pick) return { col: def.col, dir: def.dir };
        var cols = LANE_SORT_COLS[lane] || [];
        if (cols.indexOf(pick.col) === -1) return { col: def.col, dir: def.dir };
        return { col: pick.col, dir: pick.dir };
    }

    function sortRows(lane, rows) {
        var s = effectiveSort(lane);
        var dir = s.dir === 'asc' ? 1 : -1;
        var get;
        switch (s.col) {
            case 'phrase':  get = function (r) { return (r.phrase || '').toLowerCase(); }; break;
            case 'channel': get = function (r) { return (r.channelKey || '').toLowerCase(); }; break;
            case 'locale':  get = function (r) { return (r.locale || '').toLowerCase(); }; break;
            case 'zero':    get = function (r) { return r.zeroResults || 0; }; break;
            case 'ctr':     get = function (r) { return r.ctr || 0; }; break;
            case 'hits':
            default:        get = function (r) { return r.count || r.hits || 0; }; break;
        }
        return rows.slice().sort(function (a, b) {
            var av = get(a), bv = get(b);
            if (av < bv) return -1 * dir;
            if (av > bv) return  1 * dir;
            return 0;
        });
    }

    function applySortIndicatorToActiveTab() {
        var s = effectiveSort(state.tab);
        var table = document.querySelector('.gst-ins-aurora-table[data-lane="' + state.tab + '"]');
        if (!table) return;
        table.querySelectorAll('thead th[data-sort]').forEach(function (th) {
            if (th.getAttribute('data-sort') === s.col) th.setAttribute('data-sort-dir', s.dir);
            else th.removeAttribute('data-sort-dir');
        });
    }

    function repaintActiveTabFromCache() {
        var lane = state.tab;
        var tbody = document.getElementById('gst-ins-' + lane);
        var countEl = document.getElementById('gst-ins-' + lane + '-count');
        if (!tbody) return;
        if (!state.loaded[lane]) { fetchLane(lane); return; }
        paintLane(lane, tbody, countEl, state.rows[lane]);
    }

    // Refresh a single tab's count chip from cached rows + the active phrase
    // filter. Used by `updateAllTabCounts()` when filters change in ways that
    // can be computed without a re-fetch (today: phrase only).
    function updateTabCountFromCache(lane) {
        var el = document.getElementById('gst-ins-' + lane + '-count');
        if (!el) return;
        if (!state.loaded[lane]) { el.hidden = true; el.textContent = ''; return; }
        var filtered = applyPhraseFilter(state.rows[lane]);
        el.textContent = String(filtered.length);
        el.hidden = false;
    }

    function updateAllTabCounts() { LANES.forEach(updateTabCountFromCache); }

    // Fetch every lane in parallel. Counts on every tab can then reflect the
    // current server-side filter state (channel/locale/window/date) — not just
    // the active tab's. Inactive tabs paint their hidden tbodies as a side
    // effect, which makes subsequent tab switches instant.
    function fetchAllLanes() { LANES.forEach(fetchLane); }

    // ── State sync ────────────────────────────────────────────────────

    function readHash() {
        var h = (window.location.hash || '').replace(/^#/, '');
        if (!h) return;
        h.split('&').forEach(function (pair) {
            var bits = pair.split('=');
            var k = decodeURIComponent(bits[0] || '');
            var v = decodeURIComponent(bits[1] || '');
            if (k === 'tab' && LANES.indexOf(v) !== -1) state.tab = v;
            else if (k === 'channel') state.channel = v;
            else if (k === 'locale') state.locale = v;
            else if (k === 'days') {
                var d = parseInt(v, 10);
                if (d === 7 || d === 30) state.days = d;
            }
            else if (k === 'sort') {
                // sort=<lane>:<col>:<dir> — encodes the active lane's pick.
                // Reload only restores that lane; other lanes keep their
                // default until the user clicks one of their headers.
                var sb = v.split(':');
                if (sb.length === 3 && LANES.indexOf(sb[0]) !== -1 &&
                    (sb[2] === 'asc' || sb[2] === 'desc')) {
                    state.sort[sb[0]] = { col: sb[1], dir: sb[2] };
                }
            }
        });
    }

    function writeHash() {
        var bits = [];
        bits.push('tab=' + encodeURIComponent(state.tab));
        if (state.channel) bits.push('channel=' + encodeURIComponent(state.channel));
        if (state.locale)  bits.push('locale=' + encodeURIComponent(state.locale));
        if (state.days !== 7) bits.push('days=' + state.days);
        // Persist the active lane's explicit sort pick, if any — and only
        // when it differs from the lane's default. Per-lane picks for
        // non-active lanes aren't worth round-tripping through the hash;
        // they re-default on reload.
        var pick = state.sort[state.tab];
        var def = LANE_DEFAULT_SORT[state.tab] || { col: 'hits', dir: 'desc' };
        if (pick && (pick.col !== def.col || pick.dir !== def.dir)) {
            bits.push('sort=' + state.tab + ':' + pick.col + ':' + pick.dir);
        }
        var next = '#' + bits.join('&');
        if (next !== window.location.hash) {
            // replaceState so back/forward isn't polluted by toolbar tweaks.
            history.replaceState(null, '', window.location.pathname + window.location.search + next);
        }
    }

    function syncToolbarToState() {
        var winSel = document.getElementById('gst-insights-window-filter');
        if (winSel) winSel.value = String(state.days);
        var profSel = document.getElementById('gst-insights-channel-filter');
        if (profSel) profSel.value = state.channel || '';
        var locSel = document.getElementById('gst-insights-locale-filter');
        if (locSel) locSel.value = state.locale || '';
        document.querySelectorAll('.gst-tabs__btn[data-tab]').forEach(function (b) {
            var on = b.dataset.tab === state.tab;
            b.classList.toggle('is-active', on);
            b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
    }

    function invalidateAll() {
        LANES.forEach(function (l) { state.loaded[l] = false; });
    }

    // ── KPI strip ─────────────────────────────────────────────────────

    function reloadKpis() {
        renderKpisLoading();
        var url = API + '/SearchKpis';
        if (state.channel) url += '?channelKey=' + encodeURIComponent(state.channel);
        GST.fetchJson(url).then(renderKpis).catch(renderKpisError);
    }

    function renderKpis(k) {
        GST.renderKpiCard('#gst-insights-kpis', k, {
            onDateSelect: setDateFilter
        });
    }

    function renderKpisLoading() { GST.renderKpiCardLoading('#gst-insights-kpis'); }
    function renderKpisError()   { GST.renderKpiCardError('#gst-insights-kpis'); }

    // ── Date-filter chip (set by sparkline click) ─────────────────────

    function setDateFilter(d) {
        if (!d) { clearDateFilter(); return; }
        if (state.dateFilter && state.dateFilter.getTime() === d.getTime()) {
            clearDateFilter();
            return;
        }
        state.dateFilter = d;
        renderFilterChip();
        invalidateAll();
        fetchLane(state.tab);
    }

    function clearDateFilter(silent) {
        if (!state.dateFilter && !silent) return;
        state.dateFilter = null;
        renderFilterChip();
        if (window.GST && typeof GST.clearKpiCardSelection === 'function') {
            GST.clearKpiCardSelection('#gst-insights-kpis');
        }
        if (!silent) {
            invalidateAll();
            fetchAllLanes();
        }
    }

    function renderFilterChip() {
        var bar = document.querySelector('.gst-sl-toolbar');
        if (!bar) return;
        var existing = bar.querySelector('.gst-filter-chip');
        if (!state.dateFilter) {
            if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
            return;
        }
        var label = formatDateLabel(state.dateFilter);
        if (existing) {
            existing.querySelector('.gst-filter-chip__text').textContent = label;
            return;
        }
        var chip = document.createElement('span');
        chip.className = 'gst-filter-chip';
        chip.innerHTML = '<span class="gst-filter-chip__text"></span>' +
            '<button type="button" class="gst-filter-chip__clear" aria-label="Clear filter">×</button>';
        chip.querySelector('.gst-filter-chip__text').textContent = label;
        chip.querySelector('.gst-filter-chip__clear').addEventListener('click', function () { clearDateFilter(); });
        var spacer = bar.querySelector('.gst-toolbar__spacer');
        if (spacer) bar.insertBefore(chip, spacer);
        else bar.appendChild(chip);
    }

    function isoDayString(d) {
        var pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        return d.getUTCFullYear() + '-' + pad(d.getUTCMonth() + 1) + '-' + pad(d.getUTCDate());
    }
    function formatDateLabel(d) {
        try { return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' }); }
        catch (e) { return isoDayString(d); }
    }

    // ── Tab + lane render ─────────────────────────────────────────────

    function showTab(lane) {
        LANES.forEach(function (l) {
            var panel = document.getElementById('gst-ins-panel-' + l);
            if (panel) panel.classList.toggle('is-active', l === lane);
        });
        document.querySelectorAll('.gst-tabs__btn[data-tab]').forEach(function (b) {
            var on = b.dataset.tab === lane;
            b.classList.toggle('is-active', on);
            b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        applySortIndicatorToActiveTab();
        if (!state.loaded[lane]) {
            fetchLane(lane);
        } else {
            // Re-sort cached rows in case the user's sort pick was clamped to
            // the lane's default (e.g. they came from a tab with a unique col).
            repaintActiveTabFromCache();
        }
    }

    // Column count per lane — keeps colspan in sync with the per-tab <thead>
    // in Views/Insights/Index.cshtml. Top has the extra "Zero results" column;
    // Low-CTR has the extra "CTR" column.
    var LANE_COLS = { top: 5, zero: 4, lowctr: 5 };

    function fetchLane(lane) {
        var tbody = document.getElementById('gst-ins-' + lane);
        var countEl = document.getElementById('gst-ins-' + lane + '-count');
        if (!tbody) return;
        paintLoading(lane, tbody);
        if (countEl) {
            countEl.textContent = '';
            countEl.hidden = true;
        }
        var url = API + LANE_ENDPOINT[lane]
            + '?days=' + state.days
            + '&take=' + MAX_ROWS;
        if (state.channel) url += '&channelKey=' + encodeURIComponent(state.channel);
        if (state.locale)  url += '&locale=' + encodeURIComponent(state.locale);
        if (state.dateFilter) url += '&date=' + isoDayString(state.dateFilter);

        var stamp = state.inflight[lane] = {};
        GST.fetchJson(url).then(function (rows) {
            if (state.inflight[lane] !== stamp) return;
            state.loaded[lane] = true;
            state.rows[lane] = Array.isArray(rows) ? rows : [];
            paintLane(lane, tbody, countEl, state.rows[lane]);
        }).catch(function () {
            if (state.inflight[lane] !== stamp) return;
            paintLaneError(lane, tbody);
        });
    }

    function paintLoading(lane, tbody) {
        tbody.innerHTML = '<tr><td colspan="' + LANE_COLS[lane] + '" class="gst-muted">'
            + '<span class="gst-spinner" aria-hidden="true"></span></td></tr>';
    }

    function paintLaneError(lane, tbody) {
        tbody.innerHTML = '<tr><td colspan="' + LANE_COLS[lane] + '" class="gst-empty"><p>'
            + escHtml(STRINGS.load_failed || 'Could not load insights.') + '</p></td></tr>';
    }

    function paintLane(lane, tbody, countEl, rows) {
        rows = Array.isArray(rows) ? rows : [];
        var filtered = applyPhraseFilter(rows);
        if (countEl) {
            countEl.textContent = String(filtered.length);
            countEl.hidden = false;
        }
        if (filtered.length === 0) {
            var msg = (state.phrase && rows.length > 0)
                ? (STRINGS.phrase_filter_no_matches || 'No phrases match your filter.')
                : emptyMessage(lane);
            tbody.innerHTML = '<tr><td colspan="' + LANE_COLS[lane] + '" class="gst-empty"><p>'
                + escHtml(msg) + '</p></td></tr>';
            return;
        }
        var sorted = sortRows(lane, filtered);
        tbody.innerHTML = '';
        var frag = document.createDocumentFragment();
        sorted.forEach(function (r) { frag.appendChild(buildRow(lane, r)); });
        tbody.appendChild(frag);
    }

    // Shared cell helpers — phrase, channel link, locale, hits are common to
    // every lane; CTR and Zero-result are lane-specific tails.

    function cell(text, opts) {
        var td = document.createElement('td');
        if (opts && opts.cls) td.className = opts.cls;
        if (opts && opts.title) td.title = opts.title;
        td.textContent = text == null ? '' : String(text);
        return td;
    }

    function channelCell(channelKey) {
        var td = document.createElement('td');
        if (!channelKey) {
            td.className = 'gst-muted';
            td.textContent = '—';
            return td;
        }
        var a = document.createElement('a');
        a.className = 'gst-ins-row__channel';
        // ChannelsController is rooted at /EPiServer/cms/graphsearchtools/channels
        // (not under the module resource base), and the detail page reads
        // ?key=<id> from the index action — matches how Index.cshtml and
        // channels.js build the same link.
        a.href = '/EPiServer/cms/graphsearchtools/channels?key=' + encodeURIComponent(channelKey);
        a.textContent = channelKey;
        a.title = STRINGS.open_channel || 'Open this channel';
        td.appendChild(a);
        return td;
    }

    function buildRow(lane, row) {
        var hits = row.count || row.hits || 0;
        var tr = document.createElement('tr');

        // 1: phrase — first column reads as the row's name (strong, no link).
        var phraseTd = document.createElement('td');
        phraseTd.className = 'gst-ins-row__phrase';
        phraseTd.title = row.phrase || '';
        phraseTd.textContent = row.phrase || '';
        tr.appendChild(phraseTd);

        // 2: channel (link), 3: locale, 4: hits (numeric, right-aligned).
        tr.appendChild(channelCell(row.channelKey));
        tr.appendChild(cell(row.locale || '—', { cls: row.locale ? '' : 'gst-muted' }));
        tr.appendChild(cell(hits.toLocaleString(), { cls: 'num' }));

        // 5: lane-specific tail.
        if (lane === 'top') {
            var zero = row.zeroResults || 0;
            tr.appendChild(cell(zero ? zero.toLocaleString() : '0', {
                cls: zero ? 'num' : 'num gst-muted'
            }));
        } else if (lane === 'lowctr') {
            var pct = Math.round((row.ctr || 0) * 100);
            tr.appendChild(cell(pct + '%', { cls: 'num' }));
        }
        return tr;
    }

    function emptyMessage(lane) {
        if (lane === 'zero')   return STRINGS.empty_zero    || 'No zero-result phrases — every search found something.';
        if (lane === 'lowctr') return STRINGS.empty_lowctr  || 'Not enough sessions to score CTR yet.';
        return STRINGS.empty_phrases || 'No phrases logged in this window yet.';
    }

    function escHtml(s) {
        if (s == null) return '';
        var d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }
})();
