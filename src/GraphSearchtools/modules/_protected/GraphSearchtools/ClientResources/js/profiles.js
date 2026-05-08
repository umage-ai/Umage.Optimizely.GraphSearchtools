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
        var auditLoaded = false;

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
                if (target === 'details')  loadAudit(key);
            });
        });

        function mountPinned() {
            if (pinnedMounted) return;
            if (!window.GST || !window.GST.pinned || typeof window.GST.pinned.editor !== 'function') return;
            pinnedMounted = true;
            window.GST.pinned.editor({
                profileKey: key,
                sites: opts.sites || [],
                locales: opts.locales || [],
                isGeneric: !!opts.isGeneric,
                isSiteShared: !!opts.isSiteShared,
                pinnedKeyFormula: opts.pinnedKeyFormula || null,
                hasGraphQLDoc: !!opts.hasGraphQLDoc
            });
        }

        function mountSynonyms() {
            if (synonymsMounted) return;
            synonymsMounted = true;
            mountSynonymsPanel({
                locales: opts.locales || []
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

        var langSel  = document.getElementById('gst-prof-syn-lang');
        var rowsHost = document.getElementById('gst-prof-syn-rows');
        var emptyEl  = document.getElementById('gst-prof-syn-empty');
        var alertEl  = document.getElementById('gst-prof-syn-alert');
        var addBtn   = document.getElementById('gst-prof-syn-add');
        var saveBtn  = document.getElementById('gst-prof-syn-save');
        var discardBtn = document.getElementById('gst-prof-syn-discard');

        if (!rowsHost || !saveBtn) return;

        var state = {
            lang: langSel ? langSel.value : '',
            rows: [],
            dirty: false,
            loaded: false
        };

        function setAlert(msg, isError) {
            if (!alertEl) return;
            if (!msg) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = msg;
            alertEl.classList.toggle('gst-alert--danger', !!isError);
        }

        function refreshChrome() {
            var visibleRows = state.rows.filter(function(r) { return r.isNew || (r.rule && r.rule.trim()); });
            if (emptyEl) emptyEl.hidden = visibleRows.length > 0;
            saveBtn.disabled = !state.dirty;
            if (discardBtn) discardBtn.hidden = !state.dirty;
        }

        function renderRows() {
            rowsHost.innerHTML = '';
            state.rows.forEach(function(row) { rowsHost.appendChild(buildRow(row)); });
            refreshChrome();
        }

        function classifyRule(rule) {
            // "a => b" → replacement; "a, b, c" → equivalent; otherwise none.
            if (!rule) return '';
            if (rule.indexOf('=>') !== -1) return 'replacement';
            if (rule.indexOf(',')  !== -1) return 'equivalent';
            return 'other';
        }

        function buildRow(row) {
            var li = document.createElement('li');
            li.className = 'gst-prof-syn__row'
                + (row.dirty ? ' is-dirty' : '')
                + (row.isNew ? ' is-new' : '');

            var kind = classifyRule(row.rule);
            var kindChip = document.createElement('span');
            kindChip.className = 'gst-prof-syn__kind is-' + (kind || 'other');
            kindChip.textContent = kind === 'replacement' ? '→'
                                : kind === 'equivalent'  ? '='
                                : '·';
            kindChip.title = kind || '';
            li.appendChild(kindChip);

            var input = document.createElement('input');
            input.type = 'text';
            input.className = 'gst-prof-syn__input';
            input.value = row.rule || '';
            input.placeholder = s('synonyms.rule_placeholder', 'H2O => water  or  laptop, computer, pc');
            input.addEventListener('input', function() {
                row.rule = input.value;
                row.dirty = true;
                state.dirty = true;
                li.classList.add('is-dirty');
                kindChip.className = 'gst-prof-syn__kind is-' + classifyRule(row.rule);
                kindChip.textContent = classifyRule(row.rule) === 'replacement' ? '→'
                                    : classifyRule(row.rule) === 'equivalent'  ? '='
                                    : '·';
                refreshChrome();
            });
            li.appendChild(input);

            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'gst-prof-syn__delbtn';
            del.title = s('synonyms.action_remove', 'Remove');
            del.innerHTML = '<svg viewBox="0 0 12 12" width="11" height="11" aria-hidden="true">'
                + '<path d="M3 3 L9 9 M9 3 L3 9" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/></svg>';
            del.addEventListener('click', function() {
                state.rows = state.rows.filter(function(r) { return r !== row; });
                state.dirty = true;
                renderRows();
            });
            li.appendChild(del);

            return li;
        }

        function loadForLang(lang) {
            setAlert(null);
            var qs = lang
                ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SLOT)
                : '?slot=' + encodeURIComponent(SLOT);
            return ajax(BASE + '/SynonymsApi/Get' + qs)
                .then(function(result) {
                    state.rows = [];
                    state.dirty = false;
                    state.lang = lang;
                    var content = result ? result.content : '';
                    if (content) {
                        // Server may double-quote-string the body; unwrap it.
                        if (content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
                            try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
                        }
                        content.split(/\r?\n/).forEach(function(line) {
                            var rule = line.trim();
                            if (!rule) return;
                            state.rows.push({ rule: rule, dirty: false, isNew: false });
                        });
                    }
                    renderRows();
                })
                .catch(function() {
                    // 404 / no rules is fine — render empty.
                    state.rows = [];
                    state.dirty = false;
                    state.lang = lang;
                    renderRows();
                });
        }

        function addRule() {
            state.rows.push({ rule: '', dirty: true, isNew: true });
            state.dirty = true;
            renderRows();
            // Focus the new row.
            var inputs = rowsHost.querySelectorAll('.gst-prof-syn__input');
            var last = inputs[inputs.length - 1];
            if (last && last.focus) last.focus();
        }

        function save() {
            var rules = state.rows
                .map(function(r) { return (r.rule || '').trim(); })
                .filter(function(r) { return r; });
            var content = rules.join('\n');
            var promise;
            if (content) {
                promise = ajax(BASE + '/SynonymsApi/Update', {
                    method: 'PUT',
                    body: {
                        content: content,
                        languageRouting: state.lang || null,
                        sourceRouting: null,
                        slot: SLOT
                    }
                });
            } else {
                var qs = state.lang
                    ? '?languageRouting=' + encodeURIComponent(state.lang) + '&slot=' + encodeURIComponent(SLOT)
                    : '?slot=' + encodeURIComponent(SLOT);
                promise = ajax(BASE + '/SynonymsApi/Delete' + qs, { method: 'DELETE' });
            }
            promise.then(function() {
                state.dirty = false;
                state.rows = state.rows.filter(function(r) { return (r.rule || '').trim(); });
                state.rows.forEach(function(r) { r.dirty = false; r.isNew = false; });
                renderRows();
                setAlert(s('synonyms.saved', 'Synonyms saved.'));
                // Tell the live preview to drop its cached rules for this lang
                // so the next preview reflects the edit.
                window.dispatchEvent(new CustomEvent('gst:synonyms-changed', {
                    detail: { lang: state.lang || '' }
                }));
            }).catch(function(err) {
                setAlert((err && err.message) || s('synonyms.request_failed', 'Failed to save synonyms.'), true);
            });
        }

        function discard() {
            loadForLang(state.lang);
        }

        if (langSel) {
            langSel.addEventListener('change', function() {
                if (state.dirty) {
                    var keep = confirm(s('synonyms.confirm_unsaved',
                        'You have unsaved synonym changes. Press OK to save, or Cancel to discard.'));
                    var p = keep ? Promise.resolve(save()) : Promise.resolve();
                    p.then(function() { loadForLang(langSel.value); });
                    return;
                }
                loadForLang(langSel.value);
            });
        }
        if (addBtn)     addBtn.addEventListener('click', addRule);
        if (saveBtn)    saveBtn.addEventListener('click', save);
        if (discardBtn) discardBtn.addEventListener('click', discard);

        // Pre-pick the first profile-scoped locale if the global blob is empty
        // and the profile only has one applicable language — reduces the steps
        // to "edit synonyms for this profile" by one click.
        if ((!langSel || langSel.value === '') && opts.locales && opts.locales.length === 1) {
            if (langSel) langSel.value = opts.locales[0];
            state.lang = opts.locales[0];
        }

        loadForLang(state.lang);
        refreshChrome();
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
