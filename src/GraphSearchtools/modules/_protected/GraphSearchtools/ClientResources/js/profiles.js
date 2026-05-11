/**
 * Graph Search Tools — Search Profiles UI (Phase 2.5)
 *
 * Two entry points:
 *   GST.profiles.index({ detailUrlBase })   — wires up the index table.
 *   GST.profiles.detail({ profileKey })     — wires up the detail page tabs +
 *                                              fetches the audit log.
 *
 * Read-only against the JSON API at /EPiServer/cms/graphsearchtools/api/profiles.
 */
(function() {
    'use strict';

    var API_BASE = '/EPiServer/cms/graphsearchtools/api/profiles';

    function s(path, fallback) {
        return GST.s(path, fallback);
    }

    function escHtml(v) {
        return GST.escHtml(v);
    }

    /** "2 hrs ago", "—", etc. */
    function relativeTime(iso) {
        if (!iso) return '—';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return '—';
        var diff = (Date.now() - d.getTime()) / 1000;
        if (diff < 60)        return s('profiles.time.justNow', 'just now');
        if (diff < 3600)      return Math.floor(diff / 60) + ' ' + s('profiles.time.minutesAgo', 'min ago');
        if (diff < 86400)     return Math.floor(diff / 3600) + ' ' + s('profiles.time.hoursAgo', 'hrs ago');
        return Math.floor(diff / 86400) + ' ' + s('profiles.time.daysAgo', 'days ago');
    }

    function statusBadge(status) {
        var key, klass;
        switch (status) {
            case 'Tuned':       key = 'profiles.status.tuned';       klass = 'gst-badge--success'; break;
            case 'NeedsReview': key = 'profiles.status.needsReview'; klass = 'gst-badge--warning'; break;
            case 'DocMissing':  key = 'profiles.status.docMissing';  klass = 'gst-badge--danger';  break;
            case 'FreeForm':    key = 'profiles.status.freeForm';    klass = 'gst-badge--default'; break;
            case 'Cold':        key = 'profiles.status.cold';        klass = 'gst-badge--default'; break;
            default:            key = 'profiles.status.cold';        klass = 'gst-badge--default';
        }
        // Server may also return integer enum values from JSON serializer config —
        // map those defensively.
        return '<span class="gst-badge ' + klass + '"><span class="gst-badge__dot"></span>'
            + escHtml(s(key, status)) + '</span>';
    }

    /** Render Sites & locales cell. */
    function scopeCell(p) {
        var sitesHtml = (p.sites && p.sites.length)
            ? p.sites.map(function(x) { return '<span class="gst-badge gst-badge--default">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('profiles.detail.meta.allSites', 'all sites')) + '</span>';
        var localesHtml = (p.locales && p.locales.length)
            ? p.locales.map(function(x) { return '<span class="gst-badge gst-badge--primary">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('profiles.detail.meta.allLocales', 'all locales')) + '</span>';
        return '<div class="gst-prof-scope">' + sitesHtml + '</div>'
             + '<div class="gst-prof-scope" style="margin-top: 4px">' + localesHtml + '</div>';
    }

    /** Render the three-bar tuning column. */
    function tuningCell(p) {
        // v1: actual pin / synonym counts require live Graph calls we haven't
        // wired yet. Bars show the semantic-blend weight only; pins/syns rows
        // collapse to "—" until the counts arrive.
        var sw = (typeof p.semanticWeight === 'number') ? p.semanticWeight : 0;
        var swPct = Math.max(0, Math.min(100, Math.abs(sw) * 100));
        var swDisplay = (sw === 0) ? '—' : sw.toFixed(2);
        return '<div class="gst-prof-bars">'
            +     '<span class="gst-prof-bars__name">pins</span>'
            +     '<span class="gst-prof-bars__bar" style="--w: 0%"></span>'
            +     '<span class="gst-prof-bars__num">—</span>'
            +     '<span class="gst-prof-bars__name">syns</span>'
            +     '<span class="gst-prof-bars__bar muted" style="--w: 0%"></span>'
            +     '<span class="gst-prof-bars__num">—</span>'
            +     '<span class="gst-prof-bars__name">sem.</span>'
            +     '<span class="gst-prof-bars__bar" style="--w: ' + swPct + '%"></span>'
            +     '<span class="gst-prof-bars__num">' + swDisplay + '</span>'
            +  '</div>';
    }

    function profileCell(p) {
        var subPath = p.graphQLDocPath
            ? p.key + ' · ' + p.graphQLDocPath
            : p.key;
        return '<div class="gst-prof-name">'
            +     '<div class="gst-prof-name__title">' + escHtml(p.displayName || p.key) + '</div>'
            +     '<div class="gst-prof-name__key">' + escHtml(subPath) + '</div>'
            +  '</div>';
    }

    function lastEditedCell(p) {
        if (!p.lastEditedAt) {
            return '<span class="gst-prof-status__line">—</span>';
        }
        var byPart = p.lastEditedBy ? ' · ' + escHtml(p.lastEditedBy) : '';
        return '<span class="gst-prof-status__line">' + escHtml(relativeTime(p.lastEditedAt)) + byPart + '</span>';
    }

    function chevronCell() {
        return '<svg class="gst-prof-arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            + 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>';
    }

    /** ---------------- INDEX ---------------- */
    function index(opts) {
        opts = opts || {};
        // Detail URL is the index URL with a `?key=...` query so the CMS
        // shell maps both surfaces to the same registered menu item.
        var detailUrlBase = opts.detailUrlBase || '/EPiServer/cms/graphsearchtools/profiles?key=';

        var tableHost  = document.getElementById('gst-prof-table-host');
        var emptyEl    = document.getElementById('gst-prof-empty');
        var alertEl    = document.getElementById('gst-prof-alert');
        var searchEl   = document.getElementById('gst-prof-search');
        var siteEl     = document.getElementById('gst-prof-site-filter');
        var localeEl   = document.getElementById('gst-prof-locale-filter');
        var countEl    = document.getElementById('gst-prof-count');

        if (!tableHost) return;

        GST.showLoading(tableHost);

        GST.fetchJson(API_BASE).then(function(profiles) {
            if (!profiles || profiles.length === 0) {
                tableHost.innerHTML = '';
                if (emptyEl) emptyEl.hidden = false;
                renderStats([]);
                if (countEl) countEl.textContent = '';
                return;
            }
            renderStats(profiles);
            populateFilters(profiles);
            renderTable(profiles);
        }).catch(function(err) {
            tableHost.innerHTML = '';
            showAlert(s('profiles.requestFailed', 'Failed to load profiles.'), 'danger');
            console.error('Profiles list failed', err);
        });

        function showAlert(msg, kind) {
            if (!alertEl) return;
            alertEl.className = 'gst-alert gst-alert--' + (kind || 'warning');
            alertEl.textContent = msg;
            alertEl.hidden = false;
        }

        function renderStats(profiles) {
            var sites = new Set();
            var locales = new Set();
            profiles.forEach(function(p) {
                (p.sites || []).forEach(function(x) { sites.add(x); });
                (p.locales || []).forEach(function(x) { locales.add(x); });
            });

            setText('gst-prof-stat-count', profiles.length);
            var subParts = [];
            if (sites.size)   subParts.push(sites.size + ' ' + s('profiles.stats.sites', 'sites'));
            if (locales.size) subParts.push(locales.size + ' ' + s('profiles.stats.locales', 'locales'));
            setText('gst-prof-stat-count-sub', subParts.join(' · '));

            // Pinned + synonym counts require Graph calls — scaffolded with em
            // dashes until the counts arrive (see ProfilesService note).
            setText('gst-prof-stat-pinned', '—');
            setText('gst-prof-stat-synonyms', '—');
        }

        function setText(id, value) {
            var el = document.getElementById(id);
            if (el) el.textContent = value == null ? '' : String(value);
        }

        function populateFilters(profiles) {
            var sites = new Set();
            var locales = new Set();
            profiles.forEach(function(p) {
                (p.sites || []).forEach(function(x) { sites.add(x); });
                (p.locales || []).forEach(function(x) { locales.add(x); });
            });
            fillSelect(siteEl, sites);
            fillSelect(localeEl, locales);
        }

        function fillSelect(el, values) {
            if (!el) return;
            // Preserve the first "All …" option, drop and rebuild the rest.
            var first = el.querySelector('option');
            el.innerHTML = '';
            if (first) el.appendChild(first);
            Array.from(values).sort().forEach(function(v) {
                var o = document.createElement('option');
                o.value = v;
                o.textContent = v;
                el.appendChild(o);
            });
        }

        function renderTable(profiles) {
            var table = document.createElement('table');
            table.className = 'gst-table gst-prof-table';
            table.innerHTML =
                '<thead><tr>'
                + '<th style="width: 28%">' + escHtml(s('profiles.cols.profile', 'Profile')) + '</th>'
                + '<th style="width: 18%" class="col-scope">' + escHtml(s('profiles.cols.scope', 'Sites & locales')) + '</th>'
                + '<th style="width: 22%" class="col-tuning">' + escHtml(s('profiles.cols.tuning', 'Tuning')) + '</th>'
                + '<th style="width: 18%">' + escHtml(s('profiles.cols.status', 'Status')) + '</th>'
                + '<th>' + escHtml(s('profiles.cols.lastEdited', 'Last edited')) + '</th>'
                + '<th style="width: 32px"></th>'
                + '</tr></thead><tbody></tbody>';
            tableHost.innerHTML = '';
            tableHost.appendChild(table);

            var tbody = table.querySelector('tbody');
            profiles.forEach(function(p) {
                var tr = document.createElement('tr');
                tr.dataset.key = p.key || '';
                tr.dataset.search = ((p.displayName || '') + ' ' + (p.key || '') + ' ' + (p.descriptionResolved || '')).toLowerCase();
                tr.dataset.sites = (p.sites || []).join('|');
                tr.dataset.locales = (p.locales || []).join('|');

                tr.innerHTML =
                    '<td>' + profileCell(p) + '</td>'
                    + '<td class="col-scope">' + scopeCell(p) + '</td>'
                    + '<td class="col-tuning">' + tuningCell(p) + '</td>'
                    + '<td>' + statusBadge(typeof p.status === 'number' ? statusFromInt(p.status) : p.status) + '</td>'
                    + '<td>' + lastEditedCell(p) + '</td>'
                    + '<td>' + chevronCell() + '</td>';

                tr.addEventListener('click', function() {
                    if (!p.key) return;
                    window.location.href = detailUrlBase + encodeURIComponent(p.key);
                });
                tbody.appendChild(tr);
            });

            applyFilters();
        }

        function statusFromInt(i) {
            return ['Tuned', 'NeedsReview', 'DocMissing', 'FreeForm', 'Cold'][i] || 'Cold';
        }

        function applyFilters() {
            if (!tableHost) return;
            var q = (searchEl && searchEl.value || '').toLowerCase().trim();
            var site = siteEl && siteEl.value || '';
            var locale = localeEl && localeEl.value || '';
            var rows = tableHost.querySelectorAll('tbody tr');
            var visible = 0;
            rows.forEach(function(tr) {
                var matchQ = !q || tr.dataset.search.indexOf(q) >= 0;
                var matchS = !site || tr.dataset.sites.split('|').indexOf(site) >= 0 || tr.dataset.sites === '';
                var matchL = !locale || tr.dataset.locales.split('|').indexOf(locale) >= 0 || tr.dataset.locales === '';
                var show = matchQ && matchS && matchL;
                tr.hidden = !show;
                if (show) visible++;
            });
            if (countEl) {
                countEl.textContent = visible + ' ' + s('profiles.cols.profile', 'profiles').toLowerCase();
            }
        }

        if (searchEl) searchEl.addEventListener('input', applyFilters);
        if (siteEl)   siteEl.addEventListener('change', applyFilters);
        if (localeEl) localeEl.addEventListener('change', applyFilters);
    }

    /** ---------------- DETAIL ---------------- */
    /*
     * The detail page is a 50/50 workspace: the live preview lives on the
     * left and persists across right-side panel switches; the right side has
     * a three-way segmented switcher (Pinned / Synonyms / Details). The
     * pinned editor mounts up-front so the SERP preview's pin overlay reflects
     * the same data the user is editing without having to flip panels.
     */
    function detail(opts) {
        opts = opts || {};
        var key = opts.profileKey || '';
        var pinnedMounted = false;
        var synonymsMounted = false;
        var insightsMounted = false;
        var auditLoaded = false;

        // Editor handles surfaced from each panel — the Insights tab uses
        // these to seed draft pin / synonym rows from a phrase signal.
        var editors = { pinned: null, synonyms: null };

        // Mount the pinned editor immediately — it owns the site/locale state
        // shared with the preview and we want the preview's pinned-row
        // intersection to be live from first paint. The Insights tab is the
        // default-visible panel and also needs to mount eagerly so its lanes
        // populate on first paint without waiting for a user click.
        mountPinned();
        mountInsights();

        // Inject copy buttons into any code blocks marked [data-gst-copy].
        // The Razor markup wraps the GraphQL doc <pre> in such a block; this
        // keeps the wireup co-located with the panel that owns the code so
        // we don't have to reach back into Razor for the button DOM.
        document.querySelectorAll('[data-gst-copy]').forEach(function(block) {
            if (block.querySelector('.gst-copybtn')) return;
            if (!window.GST || typeof window.GST.copyButton !== 'function') return;
            block.appendChild(window.GST.copyButton({
                getValue: function() {
                    var pre = block.querySelector('pre, code, textarea');
                    return pre ? pre.textContent : '';
                },
                className: 'gst-copybtn--overlay'
            }));
        });

        // Panel switching.
        var switcherBtns = document.querySelectorAll('.gst-prof-switcher__btn');
        switcherBtns.forEach(function(btn) {
            btn.addEventListener('click', function() {
                var target = btn.dataset.panel;
                switcherBtns.forEach(function(x) {
                    var on = x === btn;
                    x.classList.toggle('is-active', on);
                    x.setAttribute('aria-selected', on ? 'true' : 'false');
                });
                document.querySelectorAll('.gst-prof-panel').forEach(function(p) {
                    p.hidden = p.dataset.panel !== target;
                    p.classList.toggle('is-active', p.dataset.panel === target);
                });
                if (target === 'synonyms') mountSynonyms();
                if (target === 'insights') mountInsights();
                if (target === 'details')  loadAudit(key);
            });
        });

        function activateTab(name) {
            var btn = document.getElementById('gst-prof-tab-' + name);
            if (btn) btn.click();
        }

        function mountPinned() {
            if (pinnedMounted) return;
            if (!window.GST || !window.GST.pinned || typeof window.GST.pinned.editor !== 'function') return;
            pinnedMounted = true;
            editors.pinned = window.GST.pinned.editor({
                profileKey: key,
                sites: opts.sites || [],
                locales: opts.locales || [],
                isGeneric: !!opts.isGeneric,
                isSiteShared: !!opts.isSiteShared,
                hasGraphQLDoc: !!opts.hasGraphQLDoc
            });
        }

        function mountSynonyms() {
            if (synonymsMounted) return;
            synonymsMounted = true;
            editors.synonyms = mountSynonymsPanel({
                locales: opts.locales || []
            });
        }

        function mountInsights() {
            if (insightsMounted) return;
            insightsMounted = true;
            mountInsightsPanel({
                profileKey: key,
                hasGraphQLDoc: !!opts.hasGraphQLDoc,
                queryAppliesPinned: typeof opts.queryAppliesPinned === 'boolean' ? opts.queryAppliesPinned : !!opts.hasGraphQLDoc,
                queryAppliesSynonyms: typeof opts.queryAppliesSynonyms === 'boolean' ? opts.queryAppliesSynonyms : true,
                getEditors: function () { return editors; },
                ensureSynonymsMounted: mountSynonyms,
                activateTab: activateTab
            });
        }
    }

    /** ---------------- SYNONYMS PANEL ---------------- */
    /*
     * Inline synonyms editor for the profile detail page. The grid widget
     * itself lives in synonyms-grid.js (shared with the standalone Synonyms
     * tool); this wrapper supplies the profile-specific opts:
     *
     *  • the locale picker is the live preview's `gst-pin-locale` chip — one
     *    "active" locale on the detail page, so duplicating the picker
     *    invited the question "are these in sync?";
     *  • mergeWithGlobal=true so the active locale view shows lang AND
     *    global rules (read-only) in one merged list — both apply at query
     *    time for the locale.
     */
    function mountSynonymsPanel(opts) {
        opts = opts || {};
        var langSel = document.getElementById('gst-pin-locale');
        return GST.synonymsGrid.mount({
            slot: 'one',
            mergeWithGlobal: true,
            getLang: function () { return langSel ? langSel.value : ''; },
            onLangChange: function (handler) {
                if (!langSel) return;
                langSel.addEventListener('change', function () { handler(langSel.value); });
            },
            dom: {
                rowsHost: '#gst-prof-syn-rows',
                emptyEl: '#gst-prof-syn-empty',
                addBtn: '#gst-prof-syn-add',
                addEmpty: '#gst-prof-syn-empty-add',
                alertEl: '#gst-prof-syn-alert',
                saveBtn: '#gst-prof-syn-save',
                discardBtn: '#gst-prof-syn-discard',
                filterInput: '#gst-prof-syn-filter',
                countEl: '#gst-prof-syn-count',
                pagerEl: '#gst-prof-syn-pager',
                pagerStatusEl: '#gst-prof-syn-pager-status',
                prevBtn: '#gst-prof-syn-prev',
                nextBtn: '#gst-prof-syn-next',
                drawerEl: '#gst-prof-syn-drawer',
                drawerCount: '#gst-prof-syn-dirty-count',
                sortBtns: document.querySelectorAll('#gst-prof-syn .gst-pinedit__sortbtn')
            }
        });
    }

    /** ---------------- INSIGHTS PANEL ---------------- */
    /*
     * Three lanes of phrase-level signal scoped to the active profile:
     * top phrases, zero-result phrases, low-CTR phrases. Backed by the
     * Search Logs API with a profileKey filter (the controller adds the
     * filter when the param is present, so global Search Logs UI is
     * unaffected).
     *
     * Interactions:
     *  - Click a phrase → drop it into the live preview's input (the
     *    existing input listener handles the debounce + fetch).
     *  - Click "Preview" → same as clicking the row.
     *  - Click "Draft pin" → switch to Pinned tab and seed a new draft
     *    row with the phrase. A small toast confirms the seed so the
     *    marketer doesn't have to flip tabs to confirm.
     *  - Click "Draft synonym" → switch to Synonyms tab and seed a new
     *    draft row with `phrase => ` typed in.
     */
    function mountInsightsPanel(opts) {
        opts = opts || {};
        var BASE = window.GST_BASE_URL || '';
        var SEARCHLOGS_API = BASE + '/SearchLogsApi';
        var profileKey = opts.profileKey || '';
        if (!profileKey) return;

        var root = document.getElementById('gst-prof-ins');
        var alertEl = document.getElementById('gst-prof-ins-alert');
        var refreshBtn = document.getElementById('gst-prof-ins-refresh');
        var pillEls = root ? root.querySelectorAll('.gst-prof-ins__pill') : [];
        if (!root) return;

        // Time-window pills map to a since-millis offset. The ISO string is
        // recomputed at fetch time so the window is always anchored to "now"
        // rather than going stale across long-lived sessions.
        var WINDOWS = { '1h': 3600e3, '24h': 86400e3, '7d': 7 * 86400e3, '30d': 30 * 86400e3 };

        // Lane-local state. Each lane starts at INITIAL_TAKE rows and grows
        // by SHOW_MORE_STEP per "show more" click. Resets back to INITIAL_TAKE
        // whenever the window or locale changes — a fresh slice is a fresh
        // surface, no point preserving an expanded view across a context flip.
        var INITIAL_TAKE = 5;
        var SHOW_MORE_STEP = 10;
        var LANES = ['top', 'zero', 'lowctr'];
        var LANE_API = { top: 'Top', zero: 'ZeroResults', lowctr: 'LowCtr' };
        var LANE_DOM = {
            top:    { listId: 'gst-prof-ins-top',    countId: 'gst-prof-ins-top-count' },
            zero:   { listId: 'gst-prof-ins-zero',   countId: 'gst-prof-ins-zero-count' },
            lowctr: { listId: 'gst-prof-ins-lowctr', countId: 'gst-prof-ins-lowctr-count' }
        };

        var state = {
            window: '24h',
            inflight: null,
            takes: { top: INITIAL_TAKE, zero: INITIAL_TAKE, lowctr: INITIAL_TAKE }
        };

        function resetTakes() {
            LANES.forEach(function (l) { state.takes[l] = INITIAL_TAKE; });
        }

        // The Pinned editor's locale chip (`#gst-pin-locale`) is the page's
        // single source of truth for which language branch the editor is
        // looking at. Mirroring it here means a marketer who narrows the
        // preview to "sv" sees only Swedish search activity in the lanes,
        // and switching back to "en" reflects English-only data without a
        // separate pill on the Insights surface.
        var localeSel = document.getElementById('gst-pin-locale');
        function activeLocale() {
            return (localeSel && !localeSel.disabled) ? (localeSel.value || '') : '';
        }

        function activeWindowMs() {
            return WINDOWS[state.window] || WINDOWS['24h'];
        }

        function setAlert(msg) {
            if (!alertEl) return;
            if (!msg) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = msg;
            alertEl.classList.add('gst-alert--danger');
        }

        // Window pill click → state change → refetch. Reset per-lane takes
        // so a fresh window opens compact rather than carrying over a
        // previously-expanded row count.
        pillEls.forEach(function (pill) {
            pill.addEventListener('click', function () {
                if (pill.classList.contains('is-active')) return;
                pillEls.forEach(function (p) { p.classList.remove('is-active'); });
                pill.classList.add('is-active');
                state.window = pill.dataset.window || '24h';
                resetTakes();
                fetchAll();
            });
        });

        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                resetTakes();
                fetchAll();
            });
        }

        // Locale switch on the Pinned editor → re-fetch insights for the new
        // branch. Pinned listens to the same event to reload its rows; both
        // mutations land on the page in lockstep so the preview, the pinned
        // table, and the analytics lanes always agree on which locale is
        // being inspected.
        if (localeSel) {
            localeSel.addEventListener('change', function () {
                resetTakes();
                fetchAll();
            });
        }

        function fetchLane(lane) {
            var since = new Date(Date.now() - activeWindowMs()).toISOString();
            var url = SEARCHLOGS_API + '/' + LANE_API[lane]
                + '?since=' + encodeURIComponent(since)
                + '&take=' + state.takes[lane]
                + '&profileKey=' + encodeURIComponent(profileKey);
            var loc = activeLocale();
            if (loc) url += '&locale=' + encodeURIComponent(loc);
            return GST.fetchJson(url);
        }

        // Show-more bumps just one lane's take and re-renders that lane.
        // The reader caches the underlying aggregate per (window, profile,
        // locale) for 30s, so the bigger take re-runs only the in-memory
        // sort-and-take — no DB roundtrip on the hot path.
        function showMore(lane) {
            state.takes[lane] += SHOW_MORE_STEP;
            paintLoading(lane);
            var stamp = state.inflight = {};
            fetchLane(lane).then(function (rows) {
                if (state.inflight !== stamp) return;
                paintLane(lane, rows);
            }).catch(function (err) {
                if (state.inflight !== stamp) return;
                paintLane(lane, { _err: err });
            });
        }

        function fetchAll() {
            // Cancel-isolation: stamp this run so a slower in-flight request
            // can't paint over a fresher one (window pill switching is fast).
            var stamp = state.inflight = {};
            setAlert(null);
            if (refreshBtn) refreshBtn.classList.add('is-spinning');

            paintLoading('top');
            paintLoading('zero');
            paintLoading('lowctr');

            Promise.all([
                fetchLane('top').catch(function (e) { return { _err: e }; }),
                fetchLane('zero').catch(function (e) { return { _err: e }; }),
                fetchLane('lowctr').catch(function (e) { return { _err: e }; })
            ]).then(function (results) {
                if (state.inflight !== stamp) return;
                if (refreshBtn) refreshBtn.classList.remove('is-spinning');
                paintLane('top',    results[0]);
                paintLane('zero',   results[1]);
                paintLane('lowctr', results[2]);
            });
        }

        function paintLoading(lane) {
            var listEl = document.getElementById(LANE_DOM[lane].listId);
            if (!listEl) return;
            removeShowMore(lane);
            // Skeleton lives inside an <li> so the <ol> stays valid.
            listEl.innerHTML = '<li class="gst-prof-ins-lane__loading"><span></span></li>';
        }

        function removeShowMore(lane) {
            var btn = document.getElementById('gst-prof-ins-' + lane + '-more');
            if (btn) btn.remove();
        }

        function paintLane(lane, payload) {
            var dom = LANE_DOM[lane];
            var listEl = document.getElementById(dom.listId);
            var countEl = document.getElementById(dom.countId);
            if (!listEl) return;
            removeShowMore(lane);

            if (payload && payload._err) {
                listEl.innerHTML = '<li class="gst-prof-ins-lane__error">'
                    + escHtml(s('profiles.detail.insights.loadFailed', 'Failed to load insights.'))
                    + '</li>';
                if (countEl) {
                    countEl.textContent = '—';
                    countEl.removeAttribute('data-window');
                }
                return;
            }

            var rows = Array.isArray(payload) ? payload : [];
            if (countEl) {
                countEl.textContent = String(rows.length);
                countEl.setAttribute('data-window', state.window);
            }

            if (rows.length === 0) {
                var emptyKey = lane === 'zero' ? 'profiles.detail.insights.emptyZero'
                    : lane === 'lowctr' ? 'profiles.detail.insights.emptyLowCtr'
                    : 'profiles.detail.insights.empty';
                var defaultEmpty = lane === 'zero' ? 'No zero-result phrases — every search found something.'
                    : lane === 'lowctr' ? 'Not enough sessions to score CTR yet.'
                    : 'No traffic in this window yet.';
                listEl.innerHTML = '<li class="gst-prof-ins-lane__empty">'
                    + escHtml(s(emptyKey, defaultEmpty)) + '</li>';
                return;
            }

            // Bar widths are normalized against the lane's maximum hits — the
            // top row is always 100% wide; everything else scales linearly so
            // the second-glance read maps to row position.
            var maxHits = rows.reduce(function (m, r) { return r.hits > m ? r.hits : m; }, 0) || 1;

            listEl.innerHTML = '';
            var frag = document.createDocumentFragment();
            rows.forEach(function (row) {
                frag.appendChild(buildRow(lane, row, maxHits));
            });
            listEl.appendChild(frag);

            // "Show more" only when the lane returned exactly its requested
            // take — that's the signal there might be additional rows. When
            // the server returns fewer than asked for, we've reached the end
            // of the available data and the button stays hidden.
            if (rows.length >= state.takes[lane]) {
                appendShowMore(lane, listEl);
            }
        }

        function appendShowMore(lane, listEl) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.id = 'gst-prof-ins-' + lane + '-more';
            btn.className = 'gst-prof-ins-lane__more';
            btn.textContent = s('profiles.detail.insights.showMore', 'Show more');
            btn.addEventListener('click', function () {
                showMore(lane);
            });
            // Drop the button after the <ol>; it sits in the lane's flow but
            // outside the list so screen readers don't announce it as an item.
            listEl.parentNode.appendChild(btn);
        }

        function buildRow(lane, row, maxHits) {
            var li = document.createElement('li');
            li.className = 'gst-prof-ins-row';
            li.dataset.phrase = row.phrase || '';
            li.tabIndex = 0;
            li.setAttribute('role', 'button');

            // 1: phrase
            var phraseEl = document.createElement('span');
            phraseEl.className = 'gst-prof-ins-row__phrase';
            phraseEl.textContent = row.phrase || '';
            phraseEl.title = row.phrase || '';
            li.appendChild(phraseEl);

            // 2: bar / miss-dots / CTR chip
            var barEl = document.createElement('span');
            barEl.className = 'gst-prof-ins-row__bar';
            if (lane === 'zero') {
                // Four little severity ticks — eye-readable, no axis math.
                for (var i = 0; i < 4; i++) {
                    var dot = document.createElement('span');
                    dot.className = 'gst-prof-ins-row__miss';
                    barEl.appendChild(dot);
                }
            } else if (lane === 'lowctr') {
                var ctrEl = document.createElement('span');
                ctrEl.className = 'gst-prof-ins-row__ctr';
                var pct = Math.round((row.ctr || 0) * 100);
                ctrEl.innerHTML = '<span class="gst-prof-ins-row__ctr-num">' + pct + '%</span>'
                    + ' ' + escHtml(s('profiles.detail.insights.ctrLabel', 'CTR'));
                barEl.appendChild(ctrEl);
            } else {
                var w = Math.max(4, Math.round(((row.hits || 0) / maxHits) * 100));
                barEl.style.setProperty('--w', w + '%');
            }
            li.appendChild(barEl);

            // 3: count
            var countEl = document.createElement('span');
            countEl.className = 'gst-prof-ins-row__count';
            countEl.innerHTML = '<strong>' + escHtml(String(row.hits || 0)) + '</strong>'
                + '<small>' + escHtml(s('profiles.detail.insights.hitsLabel', 'hits')) + '</small>';
            li.appendChild(countEl);

            // 4: actions
            // (Locale is implied by the page-level locale chip — when it's
            //  empty we show all locales, but the chip says so. Per-row
            //  badges duplicated that information, so they're omitted.)
            var actEl = document.createElement('span');
            actEl.className = 'gst-prof-ins-row__actions';
            // Preview button is always offered.
            actEl.appendChild(makeCta(s('profiles.detail.insights.ctaPreview', 'Preview'), false, function (ev) {
                ev.stopPropagation();
                applyToPreview(row.phrase);
            }));
            // The "draft" CTA depends on lane type:
            //  - top / low-CTR → draft pin (these phrases get traffic; pin
            //    candidate is the right next step)
            //  - zero-result → draft synonym (the goal is to map them into
            //    something Graph already finds)
            if (lane === 'zero') {
                if (opts.queryAppliesSynonyms) {
                    var synBtn = makeCta(s('profiles.detail.insights.ctaSynonym', 'Draft synonym'), true, function (ev) {
                        ev.stopPropagation();
                        if (draftSynonymFor(row.phrase)) {
                            markCtaDrafted(synBtn, s('profiles.detail.insights.ctaDrafted', '✓ Drafted'));
                        }
                    });
                    actEl.appendChild(synBtn);
                }
            } else {
                if (opts.queryAppliesPinned) {
                    var pinBtn = makeCta(s('profiles.detail.insights.ctaPin', 'Draft pin'), true, function (ev) {
                        ev.stopPropagation();
                        if (draftPinFor(row.phrase)) {
                            markCtaDrafted(pinBtn, s('profiles.detail.insights.ctaDrafted', '✓ Drafted'));
                        }
                    });
                    actEl.appendChild(pinBtn);
                }
            }
            li.appendChild(actEl);

            // Whole row is the click target → loads into preview.
            li.addEventListener('click', function () {
                applyToPreview(row.phrase);
            });
            li.addEventListener('keydown', function (ev) {
                if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    applyToPreview(row.phrase);
                }
            });

            return li;
        }

        function makeCta(label, isPrimary, onClick) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'gst-prof-ins-row__cta'
                + (isPrimary ? ' gst-prof-ins-row__cta--primary' : '');
            btn.textContent = label;
            btn.addEventListener('click', onClick);
            return btn;
        }

        // Swap a draft CTA into a non-clickable confirmation chip so the user
        // can see the action took. The button stays in the DOM (so layout
        // doesn't jitter) but disables itself and visually demotes — the
        // toast handles the "what next" guidance.
        function markCtaDrafted(btn, label) {
            if (!btn) return;
            btn.textContent = label;
            btn.disabled = true;
            btn.classList.remove('gst-prof-ins-row__cta--primary');
            btn.classList.add('gst-prof-ins-row__cta--drafted');
        }

        function applyToPreview(phrase) {
            if (!phrase) return;
            // Mark this row as active so the user can see which phrase the
            // preview is showing, even after they scroll.
            root.querySelectorAll('.gst-prof-ins-row.is-active').forEach(function (r) {
                r.classList.remove('is-active');
            });
            var match = root.querySelector('.gst-prof-ins-row[data-phrase="' + cssEscape(phrase) + '"]');
            if (match) match.classList.add('is-active');

            var input = document.getElementById('gst-pin-tryit-q');
            if (!input) return;
            input.value = phrase;
            // Synthesize an input event so pinned.js's existing debounce
            // handler picks it up — the path is the same one a typed-in
            // phrase takes.
            input.dispatchEvent(new Event('input', { bubbles: true }));
            // Don't steal focus; leaving focus inside the row keeps keyboard
            // navigation working.
        }

        function draftPinFor(phrase) {
            if (!phrase) return false;
            opts.activateTab('pinned');
            // After tab activation the Pinned panel is visible; seed via the
            // editor handle the detail() function captured at mount.
            var editors = opts.getEditors();
            var ed = editors.pinned;
            var ok = ed && typeof ed.draftPhrase === 'function' && ed.draftPhrase(phrase);
            if (ok) flash(s('profiles.detail.insights.draftedPin', 'Drafted in Pinned tab — fill in a target.'));
            return !!ok;
        }

        function draftSynonymFor(phrase) {
            if (!phrase) return false;
            // Synonym tab needs to be mounted before we can seed it.
            opts.ensureSynonymsMounted();
            opts.activateTab('synonyms');
            var editors = opts.getEditors();
            var ed = editors.synonyms;
            // Replacement-style template — matches the most common synonym
            // mining shape ("offending phrase => something Graph already finds").
            var rule = phrase + ' => ';
            var ok = ed && typeof ed.draftRule === 'function' && ed.draftRule(rule);
            if (ok) flash(s('profiles.detail.insights.draftedSynonym', 'Drafted in Synonyms tab — finish the rule.'));
            return !!ok;
        }

        var flashTimer = null;
        function flash(message) {
            if (!root) return;
            var existing = root.querySelector('.gst-prof-ins__flash');
            if (existing && existing.parentNode) existing.parentNode.removeChild(existing);

            var el = document.createElement('div');
            el.className = 'gst-prof-ins__flash';
            el.textContent = message;
            // Anchor onto the parent panel so the toast survives even though
            // we just navigated away from the Insights tab.
            var panel = document.querySelector('.gst-prof-panel[data-panel="insights"]');
            if (panel) panel.appendChild(el);

            // Force layout, then add the shown class for the transition.
            void el.offsetWidth;
            el.classList.add('is-shown');

            if (flashTimer) clearTimeout(flashTimer);
            flashTimer = setTimeout(function () {
                el.classList.remove('is-shown');
                setTimeout(function () {
                    if (el.parentNode) el.parentNode.removeChild(el);
                }, 220);
            }, 2400);
        }

        // CSS.escape polyfill — old Edge / quiet selector edge cases.
        function cssEscape(v) {
            if (window.CSS && typeof window.CSS.escape === 'function') return window.CSS.escape(v);
            return String(v).replace(/[^a-zA-Z0-9_-]/g, function (c) {
                return '\\' + c.charCodeAt(0).toString(16) + ' ';
            });
        }

        // Initial load.
        fetchAll();
    }

    var _auditLoaded = false;

    function loadAudit(key) {
        if (_auditLoaded) return;
        _auditLoaded = true;

        var host = document.getElementById('gst-prof-audit-host');
        var badge = document.getElementById('gst-prof-audit-badge');
        if (!host) return;

        GST.showLoading(host);

        GST.fetchJson(API_BASE + '/' + encodeURIComponent(key) + '/audit?take=100').then(function(rows) {
            if (!rows || rows.length === 0) {
                GST.showEmpty(host, s('profiles.detail.audit.empty', 'No edits recorded yet.'));
                if (badge) badge.hidden = true;
                return;
            }
            renderAudit(host, rows);
            if (badge) {
                badge.hidden = false;
                badge.textContent = rows.length;
            }
        }).catch(function(err) {
            host.innerHTML = '<p class="gst-muted">' + escHtml(s('profiles.requestFailed', 'Failed to load audit log.')) + '</p>';
            console.error('Audit log failed', err);
        });
    }

    function renderAudit(host, rows) {
        var html = '<table class="gst-table gst-prof-audit-table"><thead><tr>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.when', 'When')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.who', 'Who')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.kind', 'Kind')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.action', 'Action')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.subject', 'Subject')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.locale', 'Locale')) + '</th>'
            + '</tr></thead><tbody>';
        rows.forEach(function(r) {
            html += '<tr>'
                + '<td>' + escHtml(relativeTime(r.at)) + '</td>'
                + '<td>' + escHtml(r.actorName || r.actorId || '—') + '</td>'
                + '<td>' + escHtml(r.kind || '—') + '</td>'
                + '<td>' + escHtml(r.action || '—') + '</td>'
                + '<td>' + escHtml(r.subject || '—') + '</td>'
                + '<td>' + escHtml(r.locale || '—') + '</td>'
                + '</tr>';
        });
        html += '</tbody></table>';
        host.innerHTML = html;
    }

    window.GST = window.GST || {};
    window.GST.profiles = { index: index, detail: detail };
})();
