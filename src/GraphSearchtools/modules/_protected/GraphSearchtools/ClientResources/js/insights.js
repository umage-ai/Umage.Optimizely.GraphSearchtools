/**
 * Insights — global cross-profile view.
 *
 * Page layout:
 *   1. Toolbar          — 7d/30d pill, profile filter pill, refresh.
 *   2. 30d KPI strip    — GET InsightsApi/SearchKpis (always 30d, sparklines).
 *                         Clicking a sparkline day filters the active lane
 *                         tab to that single UTC day.
 *   3. Tab strip        — one tab per lane (Top / Zero-result / Low-CTR),
 *                         each a single full-width lane. Endpoints:
 *                            Top    → GET InsightsApi/TopPhrases
 *                            Zero   → GET InsightsApi/ZeroResultPhrases
 *                            Low    → GET InsightsApi/LowCtrPhrases
 *
 * The 7d / 30d pill and Profile filter pill apply to whichever tab is
 * currently visible; switching the toolbar marks other tabs stale so they
 * re-fetch on next select. Deep-link via #tab=top&profile=<key>.
 */
(function () {
    var BASE = window.GST_BASE_URL || '';
    var API = BASE + '/InsightsApi';
    // Profiles list lives at an absolute, well-known route — not under BASE.
    var PROFILES_API = '/EPiServer/cms/graphsearchtools/api/profiles';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.insights) || {};
    var SHARED = (window.GST_STRINGS && window.GST_STRINGS.shared) || {};

    var LANE_ENDPOINT = {
        top:    '/TopPhrases',
        zero:   '/ZeroResultPhrases',
        lowctr: '/LowCtrPhrases'
    };
    var INITIAL_TAKE = 15;
    var SHOW_MORE_STEP = 25;
    var LANES = ['top', 'zero', 'lowctr'];

    var state = {
        tab: 'top',
        days: 7,
        profile: '',
        // When non-null, the active lane is scoped to this UTC day and
        // `days` is ignored. Set by a sparkline click on the KPI card;
        // cleared by re-clicking the same day, by the × on the filter chip,
        // or by picking a window pill.
        dateFilter: null,
        takes: { top: INITIAL_TAKE, zero: INITIAL_TAKE, lowctr: INITIAL_TAKE },
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
        wireWindowPills();
        wireRefresh();
        reloadKpis();
        loadProfiles().finally(function () {
            // Profile filter buttons exist now (or only the "All" one if the
            // load failed). Either way activate the current tab.
            showTab(state.tab);
        });
        window.addEventListener('hashchange', function () {
            readHash();
            syncToolbarToState();
            showTab(state.tab);
        });
    }

    // ── Toolbar wiring ────────────────────────────────────────────────

    function wireTabs() {
        document.querySelectorAll('.gst-tab[data-tab]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var lane = btn.dataset.tab;
                if (!lane || lane === state.tab) return;
                state.tab = lane;
                writeHash();
                showTab(lane);
            });
        });
    }

    function wireWindowPills() {
        document.querySelectorAll('.gst-sl-pill[data-days]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var d = parseInt(btn.getAttribute('data-days'), 10) || 7;
                if (d === state.days) return;
                document.querySelectorAll('.gst-sl-pill[data-days]').forEach(function (b) {
                    b.classList.remove('gst-sl-pill--active');
                });
                btn.classList.add('gst-sl-pill--active');
                state.days = d;
                clearDateFilter(/* silent: */ true);
                invalidateAll();
                fetchLane(state.tab);
            });
        });
    }

    function wireRefresh() {
        var btn = document.getElementById('gst-insights-refresh');
        if (!btn) return;
        btn.addEventListener('click', function () {
            resetTakes();
            invalidateAll();
            reloadKpis();
            fetchLane(state.tab);
        });
    }

    // Load /api/profiles once, render one pill per profile next to the "All"
    // pill that's already in the markup. Sets the active pill from state.
    function loadProfiles() {
        var host = document.getElementById('gst-insights-profilefilter');
        if (!host) return Promise.resolve();
        return GST.fetchJson(PROFILES_API).then(function (rows) {
            if (!Array.isArray(rows)) return;
            rows.forEach(function (p) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'gst-sl-pill';
                btn.dataset.profile = p.key || '';
                btn.textContent = p.displayName || p.key || '';
                btn.title = p.key || '';
                host.appendChild(btn);
            });
            host.querySelectorAll('[data-profile]').forEach(function (b) {
                b.addEventListener('click', function () {
                    var k = b.dataset.profile || '';
                    if (k === state.profile) return;
                    host.querySelectorAll('[data-profile]').forEach(function (x) {
                        x.classList.remove('gst-sl-pill--active');
                    });
                    b.classList.add('gst-sl-pill--active');
                    state.profile = k;
                    writeHash();
                    invalidateAll();
                    reloadKpis();
                    fetchLane(state.tab);
                });
            });
            // Reflect the state set from the hash (if any).
            syncToolbarToState();
        }).catch(function () {
            // Silently leave the toolbar with only "All" — the lane will still
            // load. The error surface for failed lane requests is enough.
        });
    }

    // ── State sync ────────────────────────────────────────────────────

    function readHash() {
        var h = (window.location.hash || '').replace(/^#/, '');
        if (!h) return;
        h.split('&').forEach(function (pair) {
            var bits = pair.split('=');
            var k = decodeURIComponent(bits[0] || '');
            var v = decodeURIComponent(bits[1] || '');
            if (k === 'tab' && LANES.indexOf(v) !== -1) state.tab = v;
            else if (k === 'profile') state.profile = v;
            else if (k === 'days') {
                var d = parseInt(v, 10);
                if (d === 7 || d === 30) state.days = d;
            }
        });
    }

    function writeHash() {
        var bits = [];
        bits.push('tab=' + encodeURIComponent(state.tab));
        if (state.profile) bits.push('profile=' + encodeURIComponent(state.profile));
        if (state.days !== 7) bits.push('days=' + state.days);
        var next = '#' + bits.join('&');
        if (next !== window.location.hash) {
            // replaceState so back/forward isn't polluted by toolbar tweaks.
            history.replaceState(null, '', window.location.pathname + window.location.search + next);
        }
    }

    function syncToolbarToState() {
        document.querySelectorAll('.gst-sl-pill[data-days]').forEach(function (b) {
            var d = parseInt(b.getAttribute('data-days'), 10);
            b.classList.toggle('gst-sl-pill--active', d === state.days);
        });
        document.querySelectorAll('#gst-insights-profilefilter [data-profile]').forEach(function (b) {
            b.classList.toggle('gst-sl-pill--active', (b.dataset.profile || '') === state.profile);
        });
        document.querySelectorAll('.gst-tab[data-tab]').forEach(function (b) {
            var on = b.dataset.tab === state.tab;
            b.classList.toggle('active', on);
            b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
    }

    function resetTakes() {
        LANES.forEach(function (l) { state.takes[l] = INITIAL_TAKE; });
    }

    function invalidateAll() {
        LANES.forEach(function (l) { state.loaded[l] = false; });
        resetTakes();
    }

    // ── KPI strip ─────────────────────────────────────────────────────

    function reloadKpis() {
        renderKpisLoading();
        var url = API + '/SearchKpis';
        if (state.profile) url += '?profileKey=' + encodeURIComponent(state.profile);
        GST.fetchJson(url).then(renderKpis).catch(renderKpisError);
    }

    function renderKpis(k) {
        GST.renderKpiCard('gst-insights-kpis', k, {
            onDateSelect: setDateFilter
        });
    }

    function renderKpisLoading() { GST.renderKpiCardLoading('gst-insights-kpis'); }
    function renderKpisError()   { GST.renderKpiCardError('gst-insights-kpis'); }

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
            fetchLane(state.tab);
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
        document.querySelectorAll('.gst-tab[data-tab]').forEach(function (b) {
            var on = b.dataset.tab === lane;
            b.classList.toggle('active', on);
            b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        if (!state.loaded[lane]) {
            fetchLane(lane);
        }
    }

    function fetchLane(lane) {
        var listEl = document.getElementById('gst-ins-' + lane);
        var countEl = document.getElementById('gst-ins-' + lane + '-count');
        if (!listEl) return;
        paintLoading(listEl);
        if (countEl) {
            countEl.textContent = '—';
            countEl.removeAttribute('data-window');
        }
        var url = API + LANE_ENDPOINT[lane]
            + '?days=' + state.days
            + '&take=' + state.takes[lane];
        if (state.profile) url += '&profileKey=' + encodeURIComponent(state.profile);
        if (state.dateFilter) url += '&date=' + isoDayString(state.dateFilter);

        var stamp = state.inflight[lane] = {};
        GST.fetchJson(url).then(function (rows) {
            if (state.inflight[lane] !== stamp) return;
            state.loaded[lane] = true;
            paintLane(lane, listEl, countEl, rows);
        }).catch(function () {
            if (state.inflight[lane] !== stamp) return;
            paintLaneError(lane, listEl);
        });
    }

    function paintLoading(listEl) {
        listEl.innerHTML = '<li class="gst-prof-ins-lane__loading"><span></span></li>';
        removeShowMore(listEl);
    }

    function paintLaneError(lane, listEl) {
        removeShowMore(listEl);
        listEl.innerHTML = '<li class="gst-prof-ins-lane__error">'
            + escHtml(STRINGS.load_failed || 'Could not load insights.') + '</li>';
    }

    function paintLane(lane, listEl, countEl, rows) {
        rows = Array.isArray(rows) ? rows : [];
        removeShowMore(listEl);
        if (countEl) {
            countEl.textContent = String(rows.length);
            countEl.setAttribute('data-window', state.days + 'd');
        }
        if (rows.length === 0) {
            listEl.innerHTML = '<li class="gst-prof-ins-lane__empty">'
                + escHtml(emptyMessage(lane)) + '</li>';
            return;
        }
        // Bar widths normalised against the lane's max — top row is full
        // width, others scale linearly for second-glance reading.
        var maxHits = rows.reduce(function (m, r) {
            var h = r.count || r.hits || 0;
            return h > m ? h : m;
        }, 0) || 1;
        listEl.innerHTML = '';
        var frag = document.createDocumentFragment();
        rows.forEach(function (r) {
            frag.appendChild(buildRow(lane, r, maxHits));
        });
        listEl.appendChild(frag);
        if (rows.length >= state.takes[lane]) {
            appendShowMore(lane, listEl);
        }
    }

    function buildRow(lane, row, maxHits) {
        var hits = row.count || row.hits || 0;
        var li = document.createElement('li');
        li.className = 'gst-prof-ins-row';

        // 1: phrase
        var phraseEl = document.createElement('span');
        phraseEl.className = 'gst-prof-ins-row__phrase';
        phraseEl.textContent = row.phrase || '';
        phraseEl.title = row.phrase || '';
        li.appendChild(phraseEl);

        // 2: bar / miss-dots / CTR chip (lane-specific)
        var barEl = document.createElement('span');
        barEl.className = 'gst-prof-ins-row__bar';
        if (lane === 'zero') {
            for (var i = 0; i < 4; i++) {
                var dot = document.createElement('span');
                dot.className = 'gst-prof-ins-row__miss';
                barEl.appendChild(dot);
            }
        } else if (lane === 'lowctr') {
            var ctrEl = document.createElement('span');
            ctrEl.className = 'gst-prof-ins-row__ctr';
            var pct = Math.round((row.ctr || 0) * 100);
            ctrEl.innerHTML = '<span class="gst-prof-ins-row__ctr-num">' + pct + '%</span> '
                + escHtml(STRINGS.ctr_label || 'CTR');
            barEl.appendChild(ctrEl);
        } else {
            var w = Math.max(4, Math.round((hits / maxHits) * 100));
            barEl.style.setProperty('--w', w + '%');
        }
        li.appendChild(barEl);

        // 3: count (or zero-results, which the Top endpoint also returns)
        var countEl = document.createElement('span');
        countEl.className = 'gst-prof-ins-row__count';
        countEl.innerHTML = '<strong>' + escHtml(String(hits)) + '</strong>'
            + '<small>' + escHtml(STRINGS.hits_label || 'hits') + '</small>';
        li.appendChild(countEl);

        // 4: profile + locale badges (global view; profile-scoped omitted)
        var metaEl = document.createElement('span');
        metaEl.className = 'gst-prof-ins-row__meta';
        if (row.profileKey) {
            var a = document.createElement('a');
            a.className = 'gst-badge gst-badge--default';
            a.href = BASE + '/Profiles/Detail?key=' + encodeURIComponent(row.profileKey);
            a.textContent = row.profileKey;
            a.title = STRINGS.open_profile || 'Open this profile';
            metaEl.appendChild(a);
        }
        if (row.locale) {
            var lb = document.createElement('span');
            lb.className = 'gst-badge gst-badge--primary';
            lb.textContent = row.locale;
            metaEl.appendChild(lb);
        }
        li.appendChild(metaEl);

        return li;
    }

    function appendShowMore(lane, listEl) {
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'gst-prof-ins-lane__more';
        btn.id = 'gst-ins-' + lane + '-more';
        btn.textContent = STRINGS.show_more || SHARED.showMore || 'Show more';
        btn.addEventListener('click', function () {
            state.takes[lane] += SHOW_MORE_STEP;
            fetchLane(lane);
        });
        listEl.parentNode.appendChild(btn);
    }

    function removeShowMore(listEl) {
        var existing = listEl.parentNode.querySelector('.gst-prof-ins-lane__more');
        if (existing) existing.remove();
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
