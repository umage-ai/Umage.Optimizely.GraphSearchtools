/**
 * Graph Search Tools — shared synonyms editor.
 *
 * One implementation of the sortable / filterable / paged synonyms grid used
 * by both the standalone Synonyms tool and the Profile-detail Synonyms tab.
 * The two surfaces differ in which language picker drives the load and
 * whether the global blob is merged in alongside a language-scoped blob,
 * so those bits are passed in via opts.
 *
 * mount(opts):
 *   slot              — synonym slot name; defaults to 'one'
 *   mergeWithGlobal   — when true, a non-empty getLang() loads the lang AND
 *                       global blobs together; global rows are read-only
 *                       (the dedicated Synonyms tool stays the canonical place
 *                       to edit the global pool). Default false.
 *   getLang()         — returns the current language code or '' for global.
 *   onLangChange(fn)  — wires a change handler that receives the new lang.
 *   dom               — element references / selectors:
 *                       rowsHost, alertEl, addBtn, addEmpty?, emptyEl?,
 *                       saveBtn, discardBtn?, filterInput?, countEl?,
 *                       pagerEl?, pagerStatusEl?, prevBtn?, nextBtn?,
 *                       drawerEl?, drawerCount?, sortBtns? (NodeList).
 *   pageSize          — defaults to 20.
 *
 * Returns { draftRule(rule): bool, appendRule(lhs, rhs): Promise, reload(): Promise }.
 */
(function () {
    'use strict';

    window.GST = window.GST || {};

    function s(path, fallback) {
        if (window.GST && typeof window.GST.s === 'function') return window.GST.s(path, fallback);
        return fallback;
    }
    function escHtml(v) {
        if (window.GST && typeof window.GST.escHtml === 'function') return window.GST.escHtml(v);
        return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
        });
    }

    function resolveEl(ref) {
        if (!ref) return null;
        if (typeof ref === 'string') return document.querySelector(ref);
        return ref;
    }
    function resolveAll(ref) {
        if (!ref) return [];
        if (typeof ref === 'string') return document.querySelectorAll(ref);
        return ref;
    }

    function ajax(url, init) {
        init = init || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (init.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: init.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: init.body ? JSON.stringify(init.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
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

    function classifyRule(rule) {
        // "a => b" → replacement; "a, b, c" → equivalent; otherwise none.
        if (!rule) return '';
        if (rule.indexOf('=>') !== -1) return 'replacement';
        if (rule.indexOf(',') !== -1) return 'equivalent';
        return 'other';
    }

    function setKindChip(chip, rule) {
        var kind = classifyRule(rule);
        chip.className = 'gst-prof-syn__kind is-' + (kind || 'other');
        chip.textContent = kind === 'replacement' ? '→'
                        : kind === 'equivalent' ? '='
                        : '·';
        chip.title = kind || '';
    }

    function normalize(content) {
        if (!content) return '';
        if (content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
            try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
        }
        return content.split(/\r?\n/)
            .map(function (line) { return line.trim(); })
            .filter(function (line) { return line.length > 0; })
            .join('\n');
    }

    GST.synonymsGrid = {
        mount: function (opts) {
            opts = opts || {};
            var BASE = window.GST_BASE_URL || '';
            var SLOT = opts.slot || 'one';
            var PAGE_SIZE = opts.pageSize || 20;
            var MERGE = !!opts.mergeWithGlobal;
            var dom = opts.dom || {};

            var rowsHost = resolveEl(dom.rowsHost);
            var saveBtn = resolveEl(dom.saveBtn);
            if (!rowsHost || !saveBtn) return null;

            var emptyEl = resolveEl(dom.emptyEl);
            var addBtn = resolveEl(dom.addBtn);
            var addEmpty = resolveEl(dom.addEmpty);
            var alertEl = resolveEl(dom.alertEl);
            var discardBtn = resolveEl(dom.discardBtn);
            var filterInput = resolveEl(dom.filterInput);
            var countEl = resolveEl(dom.countEl);
            var pagerEl = resolveEl(dom.pagerEl);
            var pagerStatusEl = resolveEl(dom.pagerStatusEl);
            var prevBtn = resolveEl(dom.prevBtn);
            var nextBtn = resolveEl(dom.nextBtn);
            var drawerEl = resolveEl(dom.drawerEl);
            var drawerCount = resolveEl(dom.drawerCount);
            var sortBtns = resolveAll(dom.sortBtns);

            var getLang = typeof opts.getLang === 'function' ? opts.getLang : function () { return ''; };

            // Each row carries `scope`: 'lang' (the active locale's blob) or
            // 'global' (the no-language blob). With mergeWithGlobal, both
            // apply at query time for the active lang and we render them in
            // one merged list — global rows become read-only so two surfaces
            // don't race on the same blob. Without merge, only one scope is
            // ever loaded (whichever the picker selects).
            //
            // `snapshots` keeps the joined-rules content of each scope as it
            // was loaded; save() compares the current per-scope content
            // against the snapshot and writes only the scopes that changed.
            var state = {
                lang: getLang(),
                rows: [],
                filter: '',
                page: 1,
                sort: { field: 'rule', dir: 'asc' },
                snapshots: { lang: '', global: '' }
            };

            function setAlert(msg, isError) {
                if (!alertEl) return;
                if (!msg) {
                    alertEl.hidden = true;
                    alertEl.textContent = '';
                    alertEl.classList.remove('gst-alert--danger');
                    return;
                }
                alertEl.hidden = false;
                alertEl.textContent = msg;
                alertEl.classList.toggle('gst-alert--danger', !!isError);
            }

            function dirtyCount() {
                var n = 0;
                state.rows.forEach(function (r) { if (r._dirty || r._isNew) n++; });
                return n;
            }
            function isDirty() { return dirtyCount() > 0; }

            function getDisplayedRows() {
                var rows = state.rows.slice();
                var q = state.filter;
                if (q) {
                    rows = rows.filter(function (r) {
                        return (r.rule || '').toLowerCase().indexOf(q) !== -1;
                    });
                }
                var f = state.sort.field, d = state.sort.dir === 'desc' ? -1 : 1;
                rows.sort(function (a, b) {
                    var av = (a[f] || '').toString().toLowerCase();
                    var bv = (b[f] || '').toString().toLowerCase();
                    return av.localeCompare(bv) * d;
                });
                return rows;
            }

            function pageCountFor(total) { return GST.editGrid.pageCount(total, PAGE_SIZE); }
            function clampPage(total) {
                var pc = pageCountFor(total);
                if (state.page > pc) state.page = pc;
                if (state.page < 1) state.page = 1;
            }

            function refreshChrome(displayedTotal) {
                if (sortBtns && sortBtns.length) GST.editGrid.refreshSortCarets(sortBtns, state.sort);
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
                rows.slice(start, start + PAGE_SIZE).forEach(function (row) {
                    rowsHost.appendChild(buildRow(row));
                });
                if (pagerEl) {
                    GST.editGrid.renderPager({
                        pagerEl: pagerEl, statusEl: pagerStatusEl,
                        prevBtn: prevBtn, nextBtn: nextBtn,
                        page: state.page, total: total, pageSize: PAGE_SIZE
                    });
                }
                refreshChrome(total);
            }

            function defaultScope() {
                // Editing target: the active locale's blob if a locale is
                // active, else the global blob (which matches what the panel
                // is showing).
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
                // Global rows are read-only ONLY in merge mode (when a
                // standalone tool isn't already showing them as the primary
                // scope). Outside merge mode the picker selected this scope,
                // so it's editable.
                var isGlobalReadOnly = MERGE && row.scope === 'global' && !!state.lang;
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
                input.addEventListener('input', function () {
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
                    del.addEventListener('click', function () {
                        state.rows = state.rows.filter(function (r) { return r !== row; });
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
                state.page = 1;
                renderRows();
                var first = rowsHost.querySelector('tr.gst-pinedit__row.is-new .gst-pinedit__input');
                if (first && first.focus) first.focus();
            }

            function fetchScope(lang) {
                var qs = lang
                    ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=' + encodeURIComponent(SLOT)
                    : '?slot=' + encodeURIComponent(SLOT);
                return ajax(BASE + '/SynonymsApi/Get' + qs)
                    .then(function (result) { return result ? result.content : ''; })
                    .catch(function () { return ''; }); // 404 / no rules is fine
            }

            function loadForLang(lang) {
                setAlert(null);
                // In merge mode, fetch lang AND global in parallel so the
                // active locale view shows every rule that applies to it.
                // Outside merge mode, fetch only the scope the picker chose.
                var fetchLang;
                var fetchGlobal;
                if (MERGE) {
                    fetchLang = lang ? fetchScope(lang) : Promise.resolve('');
                    fetchGlobal = fetchScope('');
                } else {
                    fetchLang = lang ? fetchScope(lang) : Promise.resolve('');
                    fetchGlobal = lang ? Promise.resolve('') : fetchScope('');
                }
                return Promise.all([fetchLang, fetchGlobal]).then(function (results) {
                    var langContent = normalize(results[0]);
                    var globalContent = normalize(results[1]);
                    state.lang = lang;
                    state.page = 1;
                    state.snapshots = { lang: langContent, global: globalContent };
                    state.rows = [];
                    if (lang && langContent) {
                        langContent.split('\n').forEach(function (rule) {
                            state.rows.push({ rule: rule, scope: 'lang' });
                        });
                    }
                    if (globalContent) {
                        globalContent.split('\n').forEach(function (rule) {
                            state.rows.push({ rule: rule, scope: 'global' });
                        });
                    }
                    renderRows();
                });
            }

            function joinedScopeContent(scope) {
                return state.rows
                    .filter(function (r) { return r.scope === scope; })
                    .map(function (r) { return (r.rule || '').trim(); })
                    .filter(function (r) { return r.length > 0; })
                    .join('\n');
            }

            function writeScope(scope, content) {
                // Empty content means delete the blob entirely.
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
                // Diff per scope against the snapshot taken at load. Only
                // write scopes that actually changed.
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
                    state.rows.forEach(function (r) { delete r._dirty; delete r._isNew; });
                    renderRows();
                    return Promise.resolve();
                }
                return Promise.all(scopesToWrite.map(function (w) {
                    return writeScope(w.scope, w.content);
                })).then(function () {
                    scopesToWrite.forEach(function (w) {
                        state.snapshots[w.scope] = w.content;
                    });
                    state.rows = state.rows.filter(function (r) { return (r.rule || '').trim(); });
                    state.rows.forEach(function (r) { delete r._dirty; delete r._isNew; });
                    renderRows();
                    setAlert(s('synonyms.saved', 'Synonyms saved.'));
                    // Tell other surfaces to drop their cached rules per
                    // scope so the next read reflects the edit (the live
                    // preview's synonym chip strip listens for this).
                    scopesToWrite.forEach(function (w) {
                        window.dispatchEvent(new CustomEvent('gst:synonyms-changed', {
                            detail: { lang: w.lang }
                        }));
                    });
                }).catch(function (err) {
                    setAlert((err && err.message) || s('synonyms.request_failed', 'Failed to save synonyms.'), true);
                });
            }

            function discard() { return loadForLang(state.lang); }

            // Wire the language picker — host supplies onLangChange so the
            // editor stays agnostic about which DOM element is the picker.
            if (typeof opts.onLangChange === 'function') {
                opts.onLangChange(function (newLang) {
                    if (newLang === state.lang) return;
                    if (isDirty()) {
                        var keep = confirm(s('synonyms.confirm_unsaved',
                            'You have unsaved synonym changes. Press OK to save, or Cancel to discard.'));
                        var p = keep ? Promise.resolve(save()) : Promise.resolve();
                        p.then(function () { loadForLang(newLang); });
                        return;
                    }
                    loadForLang(newLang);
                });
            }

            if (saveBtn) saveBtn.addEventListener('click', save);
            if (discardBtn) discardBtn.addEventListener('click', discard);

            [addBtn, addEmpty].forEach(function (b) {
                if (b) b.addEventListener('click', addNewRow);
            });

            if (filterInput) {
                var filterDebounce = null;
                filterInput.addEventListener('input', function () {
                    clearTimeout(filterDebounce);
                    filterDebounce = setTimeout(function () {
                        state.filter = (filterInput.value || '').toLowerCase().trim();
                        state.page = 1;
                        renderRows();
                    }, 80);
                });
            }

            if (sortBtns && sortBtns.length) {
                GST.editGrid.wireSortHeaders(sortBtns, state.sort, function () {
                    state.page = 1;
                    renderRows();
                });
            }

            if (pagerEl) {
                GST.editGrid.wirePager({
                    prevBtn: prevBtn, nextBtn: nextBtn,
                    pageSize: PAGE_SIZE,
                    getPage: function () { return state.page; },
                    setPage: function (p) { state.page = p; },
                    getTotal: function () { return getDisplayedRows().length; },
                    onChange: renderRows
                });
            }

            // The initial fetch is async; capture its promise so draftRule
            // can wait for it. Without this, a draft seeded right after
            // mount gets wiped when loadForLang resolves and resets rows.
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
                    if (typeof first.setSelectionRange === 'function') {
                        var n = (first.value || '').length;
                        first.setSelectionRange(n, n);
                    }
                }
            }

            return {
                /**
                 * Seed a new draft row pre-filled with `rule` and focus it —
                 * used by the Insights tab CTA. Defers until the initial
                 * load resolves so the seeded row isn't wiped by load reset.
                 */
                draftRule: function (rule) {
                    if (!rule) return false;
                    var seed = function () { seedDraftRow(rule); };
                    initialLoad.then(seed, seed);
                    return true;
                },

                /**
                 * Append a single replacement rule (`lhs => rhs`) to the
                 * active scope and persist it directly. Used by the
                 * Profile Insights inline synonym editor so a marketer
                 * doesn't have to switch tabs to complete a one-line edit.
                 *
                 * Scope: when a locale is active, write to that locale's
                 * blob; otherwise write to the global blob — same policy
                 * the drawer Save uses. Returns the underlying PUT promise.
                 */
                appendRule: function (lhs, rhs) {
                    if (!lhs || !rhs) return Promise.reject(new Error('lhs and rhs are required'));
                    return initialLoad.then(function () {
                        var scope = state.lang ? 'lang' : 'global';
                        var rule = lhs.trim() + ' => ' + rhs.trim();
                        state.rows.push({ rule: rule, scope: scope });
                        var content = joinedScopeContent(scope);
                        return writeScope(scope, content).then(function () {
                            state.snapshots[scope] = content;
                            renderRows();
                            // Mirror save()'s broadcast so the live preview
                            // drops its cached synonym rules for this lang.
                            window.dispatchEvent(new CustomEvent('gst:synonyms-changed', {
                                detail: { lang: scope === 'lang' ? state.lang : '' }
                            }));
                        });
                    });
                },

                reload: function () { return loadForLang(state.lang); }
            };
        }
    };
})();
