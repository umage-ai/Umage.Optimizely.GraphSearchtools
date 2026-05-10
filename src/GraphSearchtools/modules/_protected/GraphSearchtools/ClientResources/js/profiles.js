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
        // intersection to be live from first paint.
        mountPinned();

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
     * Inline synonyms editor scoped to the profile detail page. Synonyms in
     * Optimizely Graph are global per language (one blob keyed by
     * languageRouting), so this panel always edits the global pool — it just
     * narrows the locale picker to the languages this profile cares about.
     */
    function mountSynonymsPanel(opts) {
        opts = opts || {};
        var BASE = window.GST_BASE_URL || '';
        var SLOT = 'one';

        // Local ajax helper — GST.fetchJson is GET-only, and synonyms uses
        // PUT/DELETE with JSON bodies. Mirrors the pattern in synonyms.js.
        function ajax(url, init) {
            init = init || {};
            var headers = { 'X-Requested-With': 'XMLHttpRequest' };
            if (init.body) headers['Content-Type'] = 'application/json';
            return fetch(url, {
                method: init.method || 'GET',
                headers: headers,
                credentials: 'same-origin',
                body: init.body ? JSON.stringify(init.body) : undefined
            }).then(function(resp) {
                if (!resp.ok) {
                    return resp.text().then(function(t) {
                        var msg = s('synonyms.request_failed', 'Request failed');
                        try {
                            var parsed = t ? JSON.parse(t) : null;
                            if (parsed && parsed.message) msg = parsed.message;
                        } catch (_) { /* not JSON */ }
                        throw new Error(msg + ' (' + resp.status + ')');
                    });
                }
                if (resp.status === 204) return null;
                return resp.json();
            });
        }

        // The synonyms panel piggy-backs on the live preview's locale chip
        // (`gst-pin-locale`) instead of carrying its own language picker —
        // there's only ever one "active" locale on the detail page, and
        // duplicating the picker invited the question "are these in sync?".
        // When the chip is in static mode (single-locale or generic profile)
        // the value is still meaningful: the locale code, or "" for global.
        var langSel  = document.getElementById('gst-pin-locale');
        var rowsHost = document.getElementById('gst-prof-syn-rows');
        var emptyEl  = document.getElementById('gst-prof-syn-empty');
        var addBtn   = document.getElementById('gst-prof-syn-add');
        var addEmpty = document.getElementById('gst-prof-syn-empty-add');
        var alertEl  = document.getElementById('gst-prof-syn-alert');
        var saveBtn  = document.getElementById('gst-prof-syn-save');
        var discardBtn = document.getElementById('gst-prof-syn-discard');
        var filterInput = document.getElementById('gst-prof-syn-filter');
        var countEl  = document.getElementById('gst-prof-syn-count');
        var pagerEl  = document.getElementById('gst-prof-syn-pager');
        var pagerStatusEl = document.getElementById('gst-prof-syn-pager-status');
        var prevBtn  = document.getElementById('gst-prof-syn-prev');
        var nextBtn  = document.getElementById('gst-prof-syn-next');
        var drawerEl = document.getElementById('gst-prof-syn-drawer');
        var drawerCount = document.getElementById('gst-prof-syn-dirty-count');
        var sortBtns = document.querySelectorAll('#gst-prof-syn .gst-pinedit__sortbtn');

        if (!rowsHost || !saveBtn) return;

        var PAGE_SIZE = 20;

        // Each row carries `scope`: 'lang' (the active locale's blob) or
        // 'global' (the no-language blob). Both apply at query time for
        // this locale, so we render them in one merged list. Lang-scoped
        // rows are editable inline; global rows are read-only here (manage
        // them in the dedicated Synonyms tool) so two profiles don't
        // accidentally race on the same global blob.
        //
        // `snapshots` keeps the joined-rules content of each scope as it
        // was loaded; save() compares the current per-scope content against
        // the snapshot and writes only the scopes that actually changed.
        var state = {
            lang: langSel ? langSel.value : '',
            rows: [],
            filter: '',
            page: 1,
            sort: { field: 'rule', dir: 'asc' },
            snapshots: { lang: '', global: '' }
        };

        function setAlert(msg, isError) {
            if (!alertEl) return;
            if (!msg) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = msg;
            alertEl.classList.toggle('gst-alert--danger', !!isError);
        }

        function dirtyCount() {
            var n = 0;
            state.rows.forEach(function(r) { if (r._dirty || r._isNew) n++; });
            return n;
        }

        function isDirty() { return dirtyCount() > 0; }

        function getDisplayedRows() {
            var rows = state.rows.slice();
            var q = state.filter;
            if (q) {
                rows = rows.filter(function(r) {
                    return (r.rule || '').toLowerCase().indexOf(q) !== -1;
                });
            }
            var f = state.sort.field, d = state.sort.dir === 'desc' ? -1 : 1;
            rows.sort(function(a, b) {
                var av = (a[f] || '').toString().toLowerCase();
                var bv = (b[f] || '').toString().toLowerCase();
                return av.localeCompare(bv) * d;
            });
            return rows;
        }

        function pageCountFor(total) {
            return GST.editGrid.pageCount(total, PAGE_SIZE);
        }

        function clampPage(total) {
            var pc = pageCountFor(total);
            if (state.page > pc) state.page = pc;
            if (state.page < 1) state.page = 1;
        }

        function refreshChrome(displayedTotal) {
            GST.editGrid.refreshSortCarets(sortBtns, state.sort);
            var n = dirtyCount();
            if (drawerEl) {
                drawerEl.hidden = n === 0;
                if (drawerCount) drawerCount.textContent = String(n);
            }
            if (countEl) {
                if (state.rows.length === 0) {
                    countEl.textContent = '0';
                } else {
                    var shown = (typeof displayedTotal === 'number') ? displayedTotal : getDisplayedRows().length;
                    countEl.textContent = shown === state.rows.length
                        ? String(state.rows.length)
                        : shown + ' / ' + state.rows.length;
                }
            }
        }

        function renderRows() {
            rowsHost.innerHTML = '';

            if (state.rows.length === 0) {
                if (emptyEl) emptyEl.hidden = false;
                if (pagerEl) pagerEl.hidden = true;
                refreshChrome(0);
                return;
            }
            if (emptyEl) emptyEl.hidden = true;

            var rows = getDisplayedRows();
            var total = rows.length;
            clampPage(total);

            if (total === 0) {
                var tr = document.createElement('tr');
                tr.className = 'gst-pinedit__norows';
                tr.innerHTML = '<td colspan="4">'
                    + escHtml(s('profiles.detail.synonyms.noMatches', 'No rules match the current filter.'))
                    + '</td>';
                rowsHost.appendChild(tr);
                if (pagerEl) pagerEl.hidden = true;
                refreshChrome(0);
                return;
            }

            var start = (state.page - 1) * PAGE_SIZE;
            rows.slice(start, start + PAGE_SIZE).forEach(function(row) {
                rowsHost.appendChild(buildRow(row));
            });
            GST.editGrid.renderPager({
                pagerEl: pagerEl, statusEl: pagerStatusEl,
                prevBtn: prevBtn, nextBtn: nextBtn,
                page: state.page, total: total, pageSize: PAGE_SIZE
            });
            refreshChrome(total);
        }

        function classifyRule(rule) {
            // "a => b" → replacement; "a, b, c" → equivalent; otherwise none.
            if (!rule) return '';
            if (rule.indexOf('=>') !== -1) return 'replacement';
            if (rule.indexOf(',')  !== -1) return 'equivalent';
            return 'other';
        }

        function setKindChip(chip, rule) {
            var kind = classifyRule(rule);
            chip.className = 'gst-prof-syn__kind is-' + (kind || 'other');
            chip.textContent = kind === 'replacement' ? '→'
                            : kind === 'equivalent'  ? '='
                            : '·';
            chip.title = kind || '';
        }

        function defaultScope() {
            // Editing target: the active locale's blob if a locale is active,
            // else the global blob (which matches what the panel is showing).
            return state.lang ? 'lang' : 'global';
        }

        function scopeLabel(scope) {
            if (scope === 'global') return s('profiles.detail.synonyms.scopeGlobal', 'global');
            return state.lang || '';
        }

        function markDirty(row, tr) {
            row._dirty = true;
            tr.classList.add('is-dirty');
            refreshChrome();
        }

        function buildRow(row) {
            // Global rows are read-only here — managed in the dedicated
            // Synonyms tool to avoid two profiles racing on the same blob.
            var isGlobalReadOnly = row.scope === 'global' && !!state.lang;
            var tr = document.createElement('tr');
            tr.className = 'gst-pinedit__row'
                + (row._dirty ? ' is-dirty' : '')
                + (row._isNew ? ' is-new' : '')
                + (isGlobalReadOnly ? ' is-readonly' : '');

            // Kind cell — small replacement / equivalent indicator chip.
            var kindCell = document.createElement('td');
            kindCell.className = 'gst-pinedit__cell gst-prof-syn__kind-cell';
            var kindChip = document.createElement('span');
            setKindChip(kindChip, row.rule);
            kindCell.appendChild(kindChip);
            tr.appendChild(kindCell);

            // Rule cell — in-place text input.
            var ruleCell = document.createElement('td');
            ruleCell.className = 'gst-pinedit__cell';
            var input = document.createElement('input');
            input.type = 'text';
            input.className = 'gst-pinedit__input';
            input.value = row.rule || '';
            input.placeholder = s('synonyms.rule_placeholder', 'H2O => water  or  laptop, computer, pc');
            if (isGlobalReadOnly) {
                input.readOnly = true;
                input.title = s('profiles.detail.synonyms.globalRowTip',
                    'Global rule — applies in every language. Manage in the Synonyms tool.');
            }
            input.addEventListener('input', function() {
                if (isGlobalReadOnly) return;
                row.rule = input.value;
                setKindChip(kindChip, row.rule);
                markDirty(row, tr);
            });
            ruleCell.appendChild(input);
            tr.appendChild(ruleCell);

            // Scope cell — lang/global pill.
            var scopeCell = document.createElement('td');
            scopeCell.className = 'gst-pinedit__cell';
            var scopeChip = document.createElement('span');
            scopeChip.className = 'gst-prof-syn__scope is-' + row.scope;
            scopeChip.textContent = scopeLabel(row.scope);
            scopeChip.title = row.scope === 'global'
                ? s('profiles.detail.synonyms.scopeGlobalTip',
                    'Stored in the global synonym blob — applies to every language.')
                : s('profiles.detail.synonyms.scopeLangTip',
                    "Stored in this language's synonym blob.");
            scopeCell.appendChild(scopeChip);
            tr.appendChild(scopeCell);

            // Actions cell — delete only (synonyms saves the whole blob,
            // so per-row Save would be misleading; the drawer handles it).
            var actCell = document.createElement('td');
            actCell.className = 'gst-pinedit__cell gst-pinedit__cell--actions';
            if (!isGlobalReadOnly) {
                var del = document.createElement('button');
                del.type = 'button';
                del.className = 'gst-pinedit__action gst-pinedit__action--delete';
                del.innerHTML = '<span aria-hidden="true">×</span>';
                del.title = s('synonyms.action_remove', 'Remove');
                del.addEventListener('click', function() {
                    state.rows = state.rows.filter(function(r) { return r !== row; });
                    renderRows();
                });
                actCell.appendChild(del);
            }
            tr.appendChild(actCell);

            return tr;
        }

        function addNewRow() {
            state.rows.unshift({
                rule: '', scope: defaultScope(),
                _dirty: true, _isNew: true
            });
            // Reset to first page so the just-added row is visible regardless
            // of where it lands in the alphabetical sort (an empty rule sorts
            // to the very top under ascending order).
            state.page = 1;
            renderRows();
            // Focus the new row's input.
            var first = rowsHost.querySelector('tr.gst-pinedit__row.is-new .gst-pinedit__input');
            if (first && first.focus) first.focus();
        }

        // Returns a normalized "rule\nrule\n…" string, dropping empties.
        // Used both for snapshot capture and dirty-scope detection.
        function normalize(content) {
            if (!content) return '';
            if (content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
                try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
            }
            return content.split(/\r?\n/)
                .map(function(line) { return line.trim(); })
                .filter(function(line) { return line.length > 0; })
                .join('\n');
        }

        function fetchScope(lang) {
            var qs = lang
                ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SLOT)
                : '?slot=' + encodeURIComponent(SLOT);
            return ajax(BASE + '/SynonymsApi/Get' + qs)
                .then(function(result) { return result ? result.content : ''; })
                .catch(function() { return ''; }); // 404 / no rules is fine
        }

        function loadForLang(lang) {
            setAlert(null);
            // Fetch both the language-specific blob and the global blob in
            // parallel. Both apply at query time for this locale, so we render
            // them in one merged list. When `lang` is empty, we skip the
            // duplicate fetch — the panel becomes the global editor.
            var fetchLang = lang ? fetchScope(lang) : Promise.resolve('');
            var fetchGlobal = fetchScope('');
            return Promise.all([fetchLang, fetchGlobal])
                .then(function(results) {
                    var langContent = normalize(results[0]);
                    var globalContent = normalize(results[1]);
                    state.lang = lang;
                    state.page = 1;
                    state.snapshots = { lang: langContent, global: globalContent };
                    state.rows = [];
                    if (lang && langContent) {
                        langContent.split('\n').forEach(function(rule) {
                            state.rows.push({ rule: rule, scope: 'lang' });
                        });
                    }
                    if (globalContent) {
                        globalContent.split('\n').forEach(function(rule) {
                            state.rows.push({ rule: rule, scope: 'global' });
                        });
                    }
                    renderRows();
                });
        }

        function joinedScopeContent(scope) {
            return state.rows
                .filter(function(r) { return r.scope === scope; })
                .map(function(r) { return (r.rule || '').trim(); })
                .filter(function(r) { return r.length > 0; })
                .join('\n');
        }

        function writeScope(scope, content) {
            // Empty content means delete the blob entirely. Note that we
            // never delete the global blob from this panel — global rows are
            // read-only when a locale is active, so the only way to reach
            // empty global from here is when the panel itself IS the global
            // editor (state.lang === '').
            var lang = scope === 'lang' ? state.lang : null;
            if (content) {
                return ajax(BASE + '/SynonymsApi/Update', {
                    method: 'PUT',
                    body: { content: content, languageRouting: lang, sourceRouting: null, slot: SLOT }
                });
            }
            var qs = lang
                ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SLOT)
                : '?slot=' + encodeURIComponent(SLOT);
            return ajax(BASE + '/SynonymsApi/Delete' + qs, { method: 'DELETE' });
        }

        function save() {
            // Diff per scope against the snapshot taken at load. Only write
            // scopes that actually changed — global is usually untouched
            // because it's read-only when a locale is active.
            var scopesToWrite = [];
            if (state.lang) {
                var newLang = joinedScopeContent('lang');
                if (newLang !== state.snapshots.lang) {
                    scopesToWrite.push({ scope: 'lang', content: newLang, lang: state.lang });
                }
            }
            var newGlobal = joinedScopeContent('global');
            if (newGlobal !== state.snapshots.global) {
                scopesToWrite.push({ scope: 'global', content: newGlobal, lang: '' });
            }
            if (!scopesToWrite.length) {
                state.rows.forEach(function(r) { delete r._dirty; delete r._isNew; });
                renderRows();
                return;
            }
            Promise.all(scopesToWrite.map(function(w) {
                return writeScope(w.scope, w.content);
            })).then(function() {
                scopesToWrite.forEach(function(w) {
                    state.snapshots[w.scope] = w.content;
                });
                state.rows = state.rows.filter(function(r) { return (r.rule || '').trim(); });
                state.rows.forEach(function(r) { delete r._dirty; delete r._isNew; });
                renderRows();
                setAlert(s('synonyms.saved', 'Synonyms saved.'));
                // Tell the live preview to drop its cached rules for each
                // scope we wrote so the next preview reflects the edit.
                scopesToWrite.forEach(function(w) {
                    window.dispatchEvent(new CustomEvent('gst:synonyms-changed', {
                        detail: { lang: w.lang }
                    }));
                });
            }).catch(function(err) {
                setAlert((err && err.message) || s('synonyms.request_failed', 'Failed to save synonyms.'), true);
            });
        }

        function discard() {
            loadForLang(state.lang);
        }

        // Track the live preview's locale. pinned.js installs its own
        // change handler on the same select; both fire and stay in sync.
        if (langSel) {
            langSel.addEventListener('change', function() {
                if (langSel.value === state.lang) return;
                if (isDirty()) {
                    var keep = confirm(s('synonyms.confirm_unsaved',
                        'You have unsaved synonym changes. Press OK to save, or Cancel to discard.'));
                    var p = keep ? Promise.resolve(save()) : Promise.resolve();
                    p.then(function() { loadForLang(langSel.value); });
                    return;
                }
                loadForLang(langSel.value);
            });
        }
        if (saveBtn)    saveBtn.addEventListener('click', save);
        if (discardBtn) discardBtn.addEventListener('click', discard);

        [addBtn, addEmpty].forEach(function(b) {
            if (b) b.addEventListener('click', addNewRow);
        });

        if (filterInput) {
            var filterDebounce = null;
            filterInput.addEventListener('input', function() {
                clearTimeout(filterDebounce);
                filterDebounce = setTimeout(function() {
                    state.filter = (filterInput.value || '').toLowerCase().trim();
                    state.page = 1;
                    renderRows();
                }, 80);
            });
        }

        GST.editGrid.wireSortHeaders(sortBtns, state.sort, function() {
            state.page = 1;
            renderRows();
        });

        GST.editGrid.wirePager({
            prevBtn: prevBtn, nextBtn: nextBtn,
            pageSize: PAGE_SIZE,
            getPage: function() { return state.page; },
            setPage: function(p) { state.page = p; },
            getTotal: function() { return getDisplayedRows().length; },
            onChange: renderRows
        });

        // The initial fetch is async; capture its promise so draftRule can
        // wait for it. Without this, a draft seeded by the Insights tab gets
        // wiped when loadForLang resolves and resets state.rows.
        var initialLoad = loadForLang(state.lang);

        function seedDraftRow(rule) {
            state.rows.unshift({
                rule: rule, scope: defaultScope(),
                _dirty: true, _isNew: true
            });
            state.page = 1;
            renderRows();
            var first = rowsHost.querySelector('tr.gst-pinedit__row.is-new .gst-pinedit__input');
            if (first) {
                if (first.focus) first.focus();
                // Park the caret at the end so the marketer types the
                // destination side of the rule next.
                if (typeof first.setSelectionRange === 'function') {
                    var n = (first.value || '').length;
                    first.setSelectionRange(n, n);
                }
            }
        }

        return {
            /**
             * Seed a new draft synonym row with `rule` already typed in and
             * focus the cell — used by the Insights tab "draft synonym" CTA so
             * a marketer can lock in the target side of a replacement rule
             * without retyping the offending phrase. Defers until the initial
             * load resolves so the seeded row isn't wiped by the load reset.
             */
            draftRule: function (rule) {
                if (!rule) return false;
                var seed = function () { seedDraftRow(rule); };
                initialLoad.then(seed, seed);
                return true;
            }
        };
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
        var state = {
            window: '24h',
            inflight: null
        };

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

        // Window pill click → state change → refetch.
        pillEls.forEach(function (pill) {
            pill.addEventListener('click', function () {
                if (pill.classList.contains('is-active')) return;
                pillEls.forEach(function (p) { p.classList.remove('is-active'); });
                pill.classList.add('is-active');
                state.window = pill.dataset.window || '24h';
                fetchAll();
            });
        });

        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                fetchAll();
            });
        }

        function fetchLane(slug) {
            var since = new Date(Date.now() - activeWindowMs()).toISOString();
            var url = SEARCHLOGS_API + '/' + slug
                + '?since=' + encodeURIComponent(since)
                + '&take=10'
                + '&profileKey=' + encodeURIComponent(profileKey);
            return GST.fetchJson(url);
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
                fetchLane('Top').catch(function (e) { return { _err: e }; }),
                fetchLane('ZeroResults').catch(function (e) { return { _err: e }; }),
                fetchLane('LowCtr').catch(function (e) { return { _err: e }; })
            ]).then(function (results) {
                if (state.inflight !== stamp) return;
                if (refreshBtn) refreshBtn.classList.remove('is-spinning');
                paintLane('top',    results[0], 'gst-prof-ins-top',    'gst-prof-ins-top-count');
                paintLane('zero',   results[1], 'gst-prof-ins-zero',   'gst-prof-ins-zero-count');
                paintLane('lowctr', results[2], 'gst-prof-ins-lowctr', 'gst-prof-ins-lowctr-count');
            });
        }

        function paintLoading(lane) {
            var listId = lane === 'top' ? 'gst-prof-ins-top'
                : lane === 'zero' ? 'gst-prof-ins-zero' : 'gst-prof-ins-lowctr';
            var listEl = document.getElementById(listId);
            if (!listEl) return;
            // Skeleton lives inside an <li> so the <ol> stays valid.
            listEl.innerHTML = '<li class="gst-prof-ins-lane__loading"><span></span></li>';
        }

        function paintLane(lane, payload, listId, countId) {
            var listEl = document.getElementById(listId);
            var countEl = document.getElementById(countId);
            if (!listEl) return;

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

            // 4: locale chip
            var locEl = document.createElement('span');
            locEl.className = 'gst-prof-ins-row__locale';
            if (row.locale) {
                locEl.textContent = row.locale;
            } else {
                locEl.hidden = true;
            }
            li.appendChild(locEl);

            // 5: actions
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
