/**
 * Graph Search Tools — Pinned results editor.
 *
 * Vanilla JS, no framework. Uses GST helpers (fetchJson / postJson) and
 * window.GST_BASE_URL for routing. Strings come from window.GST_STRINGS.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinned) || {};

    var alertBox = document.getElementById('gst-alert');
    var pinGrid = document.getElementById('pin-grid');
    var pinFilter = document.getElementById('pin-filter');
    var pinCollectionFilter = document.getElementById('pin-collection-filter');
    var addButton = document.getElementById('pin-add');

    var sites = [];
    var collections = [];
    var allRows = [];
    var sortField = null;
    var sortAsc = true;
    var activeDropdown = null;

    // --- HTTP helpers ---

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Request failed';
                    try {
                        var parsed = t ? JSON.parse(t) : null;
                        if (parsed && parsed.message) msg = parsed.message;
                    } catch (_) { /* not JSON, ignore */ }
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            if (resp.status === 204) return null;
            return resp.json();
        });
    }

    function setAlert(message, isError) {
        if (!alertBox) return; // legacy DOM not present (Profile detail page)
        if (!message) {
            alertBox.hidden = true;
            alertBox.textContent = '';
            alertBox.classList.remove('gst-alert--danger');
            return;
        }
        alertBox.hidden = false;
        alertBox.textContent = message;
        alertBox.classList.toggle('gst-alert--danger', !!isError);
    }

    // --- Initial load ---

    function loadAll() {
        Promise.all([
            ajax(BASE + '/SitesApi/List'),
            ajax(BASE + '/PinnedApi/Collections')
        ]).then(function (results) {
            sites = results[0] || [];
            collections = results[1] || [];
            renderSiteFilter();
            return loadAllItems();
        }).then(function () {
            return resolveContentNames();
        }).then(function () {
            renderGrid();
        }).catch(function (err) {
            setAlert(err.message, true);
        });
    }

    function loadAllItems() {
        allRows = [];
        return Promise.all(collections.map(function (col) {
            return ajax(BASE + '/PinnedApi/Items?collectionId=' + encodeURIComponent(col.id))
                .then(function (items) {
                    var site = findSiteByKey(col.key);
                    (items || []).forEach(function (item) {
                        allRows.push({
                            collectionId: col.id,
                            collectionKey: col.key,
                            siteTitle: site ? site.title : col.key,
                            id: item.id,
                            phrases: item.phrases,
                            targetKey: item.targetKey,
                            contentName: '',
                            contentType: '',
                            language: item.language || '',
                            priority: item.priority != null ? item.priority : 1000,
                            isActive: item.isActive,
                            dirty: false,
                            isNew: false
                        });
                    });
                });
        }));
    }

    function findSiteByKey(key) {
        for (var i = 0; i < sites.length; i++) if (sites[i].collectionKey === key) return sites[i];
        return null;
    }

    function findCollectionByKey(key) {
        for (var i = 0; i < collections.length; i++) if (collections[i].key === key) return collections[i];
        return null;
    }

    function ensureCollection(collectionKey) {
        var existing = findCollectionByKey(collectionKey);
        if (existing) return Promise.resolve(existing);
        var site = findSiteByKey(collectionKey);
        var title = site ? site.title : collectionKey;
        return ajax(BASE + '/PinnedApi/CreateCollection', {
            method: 'POST',
            body: { title: title, key: collectionKey, isActive: true }
        }).then(function (col) {
            collections.push(col);
            return col;
        });
    }

    function resolveContentNames() {
        var seen = {};
        var guids = [];
        for (var i = 0; i < allRows.length; i++) {
            var g = allRows[i].targetKey;
            if (g && !seen[g]) { seen[g] = true; guids.push(g); }
        }
        if (guids.length === 0) return Promise.resolve();
        return ajax(BASE + '/ContentLookupApi/Resolve', {
            method: 'POST',
            body: guids
        }).then(function (resolved) {
            var lookup = {};
            (resolved || []).forEach(function (hit) {
                lookup[(hit.contentGuid || '').toLowerCase()] = hit;
            });
            allRows.forEach(function (row) {
                var hit = lookup[(row.targetKey || '').toLowerCase()];
                if (hit) {
                    row.contentName = hit.name;
                    row.contentType = hit.contentType;
                }
            });
        }).catch(function () { /* resolve failure is non-fatal */ });
    }

    // --- Render: filters ---

    function renderSiteFilter() {
        pinCollectionFilter.innerHTML = '';
        var optAll = document.createElement('option');
        optAll.value = '';
        optAll.textContent = STRINGS.all_sites || 'All sites';
        pinCollectionFilter.appendChild(optAll);
        sites.forEach(function (site) {
            var opt = document.createElement('option');
            opt.value = site.collectionKey;
            opt.textContent = site.title;
            pinCollectionFilter.appendChild(opt);
        });
    }

    // --- Filtering & sorting ---

    function getFilteredRows() {
        var text = pinFilter.value.trim().toLowerCase();
        var siteKey = pinCollectionFilter.value;
        var rows = allRows;
        if (siteKey) rows = rows.filter(function (r) { return r.collectionKey === siteKey; });
        if (text) {
            rows = rows.filter(function (r) {
                return (r.phrases || '').toLowerCase().indexOf(text) !== -1
                    || (r.targetKey || '').toLowerCase().indexOf(text) !== -1
                    || (r.contentName || '').toLowerCase().indexOf(text) !== -1
                    || (r.siteTitle || '').toLowerCase().indexOf(text) !== -1;
            });
        }
        if (sortField) {
            var key = sortField === 'content' ? 'contentName' : sortField === 'collection' ? 'siteTitle' : 'phrases';
            rows = rows.slice().sort(function (a, b) {
                var av = (a[key] || '').toLowerCase();
                var bv = (b[key] || '').toLowerCase();
                return sortAsc ? av.localeCompare(bv) : bv.localeCompare(av);
            });
        }
        return rows;
    }

    // --- Render grid ---

    function renderGrid() {
        closeActiveDropdown();
        pinGrid.innerHTML = '';
        var rows = getFilteredRows();
        rows.forEach(function (row) { pinGrid.appendChild(buildRow(row)); });
        renderSortArrows();
    }

    function buildRow(row) {
        var tr = document.createElement('tr');
        tr.className = 'gst-pin-row' + (row.dirty ? ' is-dirty' : '') + (row.isNew ? ' is-new' : '');

        // --- Site cell ---
        var colCell = document.createElement('td');
        if (row.isNew) {
            var sel = document.createElement('select');
            sel.className = 'gst-cell-input';
            sites.forEach(function (s) {
                var opt = document.createElement('option');
                opt.value = s.collectionKey;
                opt.textContent = s.title;
                if (s.collectionKey === row.collectionKey) opt.selected = true;
                sel.appendChild(opt);
            });
            sel.addEventListener('change', function () {
                row.collectionKey = sel.value;
                var site = findSiteByKey(sel.value);
                row.siteTitle = site ? site.title : sel.value;
                markDirty(row, tr);
            });
            colCell.appendChild(sel);
        } else {
            colCell.textContent = row.siteTitle;
        }
        tr.appendChild(colCell);

        // --- Phrase cell ---
        var phraseCell = document.createElement('td');
        var phraseInput = document.createElement('input');
        phraseInput.type = 'text';
        phraseInput.className = 'gst-cell-input';
        phraseInput.value = row.phrases || '';
        phraseInput.addEventListener('input', function () {
            row.phrases = phraseInput.value;
            markDirty(row, tr);
        });
        phraseCell.appendChild(phraseInput);
        tr.appendChild(phraseCell);

        // --- Content cell ---
        var contentCell = document.createElement('td');
        contentCell.className = 'gst-pin-content-cell';
        var dropdown = document.createElement('div');
        dropdown.className = 'gst-pin-search-dropdown';

        if (row.contentName && !row.isNew) {
            buildContentPill(row, contentCell, dropdown, tr);
        } else {
            buildContentInput(row, contentCell, dropdown, tr);
        }
        contentCell.appendChild(dropdown);
        tr.appendChild(contentCell);

        // --- Actions cell ---
        var actCell = document.createElement('td');
        actCell.className = 'gst-pin-actions';
        actCell.appendChild(buildActionButton('save', row, tr, function () { saveRow(row); }));
        actCell.appendChild(buildActionButton('delete', row, tr, function () { deleteRow(row); }));
        tr.appendChild(actCell);

        return tr;
    }

    function buildActionButton(kind, row, tr, onClick) {
        var btn = document.createElement('button');
        btn.type = 'button';
        if (kind === 'save') {
            btn.className = 'gst-pin-save-btn' + (row.dirty ? ' is-dirty' : '');
            btn.innerHTML = '&#x2714;';
            btn.title = STRINGS.action_save || 'Save';
        } else {
            btn.className = 'gst-pin-delete-btn';
            btn.innerHTML = '&#x2716;';
            btn.title = STRINGS.action_delete || 'Delete';
        }
        btn.addEventListener('click', onClick);
        return btn;
    }

    function buildContentPill(row, contentCell, dropdown, tr) {
        var pill = document.createElement('span');
        pill.className = pillClassName(row.contentType);
        pill.textContent = row.contentName;
        pill.title = row.targetKey;

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'gst-cell-input';
        input.value = row.contentName;
        input.autocomplete = 'off';
        input.placeholder = STRINGS.search_placeholder || 'Search for content...';
        input.hidden = true;

        pill.addEventListener('click', function () {
            pill.hidden = true;
            input.hidden = false;
            input.focus();
            input.select();
        });

        var debounce = null;
        input.addEventListener('input', function () {
            clearTimeout(debounce);
            debounce = setTimeout(function () { searchForCell(input.value.trim(), dropdown, row, input, tr, pill); }, 300);
        });
        input.addEventListener('blur', function () {
            setTimeout(function () {
                if (dropdown.style.display === 'none' || dropdown.style.display === '') {
                    input.hidden = true;
                    pill.hidden = false;
                    pill.textContent = row.contentName || row.targetKey;
                    pill.className = pillClassName(row.contentType);
                }
            }, 200);
        });

        contentCell.appendChild(pill);
        contentCell.appendChild(input);
    }

    function buildContentInput(row, contentCell, dropdown, tr) {
        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'gst-cell-input';
        input.value = row.contentName || row.targetKey || '';
        input.autocomplete = 'off';
        input.placeholder = STRINGS.search_placeholder || 'Search for content...';

        var debounce = null;
        input.addEventListener('input', function () {
            clearTimeout(debounce);
            debounce = setTimeout(function () { searchForCell(input.value.trim(), dropdown, row, input, tr, null); }, 300);
        });
        input.addEventListener('focus', function () {
            if (dropdown.children.length > 0) {
                dropdown.style.display = 'block';
                setActiveDropdownRef(dropdown);
            }
        });
        contentCell.appendChild(input);
    }

    function pillClassName(contentType) {
        var ct = (contentType || '').toLowerCase();
        var modifier = 'is-unknown';
        // Heuristic mapping: anything containing "page" looks like a page;
        // otherwise treat as content. The host can extend the styles for any
        // value of contentType via CSS — class is derived from the raw name.
        if (ct.indexOf('page') !== -1) modifier = 'is-page';
        else if (ct === 'product') modifier = 'is-product';
        return 'gst-pin-content-pill ' + modifier;
    }

    function markDirty(row, tr) {
        row.dirty = true;
        tr.classList.add('is-dirty');
        var saveBtn = tr.querySelector('.gst-pin-save-btn');
        if (saveBtn) saveBtn.classList.add('is-dirty');
    }

    // --- Inline content search ---

    function setActiveDropdownRef(dropdown) {
        if (activeDropdown && activeDropdown !== dropdown) activeDropdown.style.display = 'none';
        activeDropdown = dropdown;
    }

    function closeActiveDropdown() {
        if (activeDropdown) { activeDropdown.style.display = 'none'; activeDropdown = null; }
    }

    function searchForCell(query, dropdown, row, input, tr, pill) {
        if (!query || query.length < 2) { dropdown.style.display = 'none'; return; }
        var locale = row.collectionKey ? '&locale=' + encodeURIComponent(row.collectionKey) : '';
        ajax(BASE + '/ContentLookupApi/Search?q=' + encodeURIComponent(query) + locale)
            .then(function (results) {
                dropdown.innerHTML = '';
                results = results || [];
                if (!results.length) { dropdown.style.display = 'none'; return; }
                results.forEach(function (hit) {
                    var div = document.createElement('div');
                    div.className = 'gst-search-hit';
                    div.addEventListener('click', function () {
                        row.targetKey = hit.contentGuid;
                        row.contentName = hit.name;
                        row.contentType = hit.contentType;
                        input.value = hit.name;
                        dropdown.style.display = 'none';
                        if (pill) {
                            pill.className = pillClassName(hit.contentType);
                            pill.textContent = hit.name;
                            pill.title = hit.contentGuid;
                            input.hidden = true;
                            pill.hidden = false;
                        }
                        markDirty(row, tr);
                    });
                    var name = document.createElement('span');
                    name.className = 'gst-search-hit-name';
                    name.textContent = hit.name;
                    var meta = document.createElement('span');
                    meta.className = 'gst-search-hit-meta';
                    meta.textContent = hit.contentType + ' · ' + hit.language;
                    div.appendChild(name);
                    div.appendChild(meta);
                    dropdown.appendChild(div);
                });
                dropdown.style.display = 'block';
                setActiveDropdownRef(dropdown);
            })
            .catch(function () { dropdown.style.display = 'none'; });
    }

    // --- CRUD ---

    function saveRow(row) {
        if (!row.phrases.trim() || !row.targetKey.trim()) {
            setAlert(STRINGS.error_phrase_and_content_required || 'Phrase and content are required.', true);
            return;
        }
        var payload = {
            phrases: row.phrases.trim(),
            targetKey: row.targetKey.trim(),
            language: row.language || null,
            priority: row.priority,
            isActive: row.isActive !== false
        };
        ensureCollection(row.collectionKey)
            .then(function (col) {
                row.collectionId = col.id;
                if (row.isNew) {
                    return ajax(BASE + '/PinnedApi/CreateItem?collectionId=' + encodeURIComponent(col.id), {
                        method: 'POST',
                        body: payload
                    }).then(function (result) {
                        row.id = result.id;
                        row.isNew = false;
                        setAlert(STRINGS.created || 'Pinned item created.');
                    });
                } else {
                    return ajax(BASE + '/PinnedApi/UpdateItem?collectionId=' + encodeURIComponent(col.id) + '&id=' + encodeURIComponent(row.id), {
                        method: 'PUT',
                        body: payload
                    }).then(function () {
                        setAlert(STRINGS.updated || 'Pinned item updated.');
                    });
                }
            })
            .then(function () {
                row.dirty = false;
                renderGrid();
            })
            .catch(function (err) { setAlert(err.message, true); });
    }

    function deleteRow(row) {
        if (row.isNew) {
            allRows = allRows.filter(function (r) { return r !== row; });
            renderGrid();
            return;
        }
        if (!confirm(STRINGS.confirm_delete || 'Delete this pinned item?')) return;
        ajax(BASE + '/PinnedApi/DeleteItem?collectionId=' + encodeURIComponent(row.collectionId) + '&id=' + encodeURIComponent(row.id), {
            method: 'DELETE'
        }).then(function () {
            allRows = allRows.filter(function (r) { return r !== row; });
            renderGrid();
            setAlert(STRINGS.deleted || 'Pinned item deleted.');
        }).catch(function (err) { setAlert(err.message, true); });
    }

    function addNewRow() {
        if (!sites.length) {
            setAlert(STRINGS.no_sites || 'No sites available.', true);
            return;
        }
        var defaultSite = sites[0];
        allRows.push({
            collectionId: null,
            collectionKey: defaultSite.collectionKey,
            siteTitle: defaultSite.title,
            id: null,
            phrases: '',
            targetKey: '',
            contentName: '',
            contentType: '',
            language: '',
            priority: 1000,
            isActive: true,
            dirty: true,
            isNew: true
        });
        renderGrid();
        var rows = pinGrid.querySelectorAll('tr');
        var lastRow = rows[rows.length - 1];
        if (lastRow) {
            lastRow.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            var input = lastRow.querySelector('.gst-cell-input');
            if (input && input.focus) input.focus();
        }
    }

    // --- Sorting ---

    function renderSortArrows() {
        var headers = document.querySelectorAll('.gst-pin-table th[data-sort]');
        for (var i = 0; i < headers.length; i++) {
            var th = headers[i];
            var arrow = th.querySelector('.gst-sort-arrow');
            if (!arrow) continue;
            if (th.getAttribute('data-sort') === sortField) {
                arrow.textContent = sortAsc ? '▲' : '▼';
                arrow.classList.add('is-active');
            } else {
                arrow.textContent = '';
                arrow.classList.remove('is-active');
            }
        }
    }

    var sortHeaders = document.querySelectorAll('.gst-pin-table th[data-sort]');
    for (var hi = 0; hi < sortHeaders.length; hi++) {
        (function (th) {
            th.addEventListener('click', function () {
                var field = th.getAttribute('data-sort');
                if (sortField === field) sortAsc = !sortAsc;
                else { sortField = field; sortAsc = true; }
                renderGrid();
            });
        })(sortHeaders[hi]);
    }

    // --- Bind events ---
    //
    // The Phase 2.5 redesign moved the legacy free-form Pinned page to a 301
    // redirect (Profiles → {profile} → Pinned tab takes its place). The bundle
    // still loads on the Profiles index/detail because the layout includes
    // it for the editor factory below — guard the legacy bindings so missing
    // DOM elements don't throw on those pages.

    if (pinGrid && addButton && pinFilter && pinCollectionFilter) {
        addButton.addEventListener('click', addNewRow);
        pinFilter.addEventListener('input', renderGrid);
        pinCollectionFilter.addEventListener('change', renderGrid);

        document.addEventListener('click', function (e) {
            if (activeDropdown && !e.target.closest('.gst-pin-content-cell')) {
                closeActiveDropdown();
            }
        });

        loadAll();
    }
})();

/**
 * Phase 2.5 §4.1 — Profile-scoped Pinned editor.
 *
 * Mounted from the Profile detail view's Pinned tab. Hydrates the site/locale
 * pickers (already rendered by Razor) and wires the rows table + add/save/delete
 * actions. Writes go through PinnedApi/{Create,Update,Delete}Item with a
 * profileKey query parameter so the audit log gets a row.
 *
 * Usage:
 *   GST.pinned.editor({
 *     profileKey: 'site-search',
 *     sites:    ['corporate', 'blog'],
 *     locales:  ['en', 'da'],
 *     isGeneric: false,
 *     isSiteShared: false,
 *     pinnedKeyFormula: 'site-{locale}',
 *     hasGraphQLDoc: true,
 *   });
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinned) || {};
    var PROFILE_API = '/EPiServer/cms/graphsearchtools/api/profiles';

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

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Request failed';
                    try {
                        var parsed = t ? JSON.parse(t) : null;
                        if (parsed && parsed.message) msg = parsed.message;
                    } catch (_) { /* ignore non-JSON bodies */ }
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            if (resp.status === 204) return null;
            return resp.json();
        });
    }

    /**
     * Resolves the Pinned key locally for display. Mirrors the server-side
     * SearchProfile.PinnedKeyForLocale formula passed in via pinnedKeyFormula —
     * the JS doesn't know about locales/sites until the user picks one.
     */
    function resolveKey(formula, locale) {
        if (!formula) return null;
        if (formula.indexOf('{locale}') === -1) return formula;
        return formula.replace('{locale}', locale || '');
    }

    function editor(opts) {
        opts = opts || {};
        var profileKey = opts.profileKey;
        if (!profileKey) return null;

        // Phase 6 redesign — flat, sortable, filterable grid with content-by-name
        // typeahead and in-place editing. The phrase-tabs grouping was dropped
        // because it forced the user to switch contexts to see all pins; phrase
        // is now just a sortable column, and a global filter narrows everything.

        var rowsTbody = document.getElementById('gst-pinedit-rows');
        var emptyEl = document.getElementById('gst-pinedit-empty');
        var siteSel = document.getElementById('gst-pin-site');
        var localeSel = document.getElementById('gst-pin-locale');
        var keylineEl = document.getElementById('gst-pin-keyline-value');
        var alertEl = document.getElementById('gst-pin-tab-alert');
        var addBtn = document.getElementById('gst-pin-add'); // legacy header button (unused but kept tolerant)
        var addBtn2 = document.getElementById('gst-pinedit-add');
        var addEmpty = document.getElementById('gst-pinedit-empty-add');
        var globalFilter = document.getElementById('gst-pinedit-global');
        var countEl = document.getElementById('gst-pinedit-count');
        var drawer = document.getElementById('gst-pinedit-drawer');
        var drawerCount = document.getElementById('gst-pinedit-dirty-count');
        var saveAllBtn = document.getElementById('gst-pinedit-save-all');
        var discardBtn = document.getElementById('gst-pinedit-discard');
        var sortBtns = document.querySelectorAll('.gst-pinedit__sortbtn');

        if (!rowsTbody) return null;

        var state = {
            site: siteSel && !siteSel.disabled ? siteSel.value : '',
            locale: localeSel && !localeSel.disabled ? localeSel.value : '',
            collectionId: null,
            pinnedKey: null,
            isGeneric: !!opts.isGeneric,
            rows: [],
            // Persistent UI state across renders
            sort: { field: 'priority', dir: 'asc' },
            global: '',
            activeTypeahead: null
        };

        function setAlert(message, isError) {
            if (!alertEl) return;
            if (!message) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = message;
            alertEl.classList.toggle('gst-alert--danger', !!isError);
        }

        function refreshKeyline() {
            if (!keylineEl) return;
            var resolved = resolveKey(opts.pinnedKeyFormula, state.locale) || '—';
            keylineEl.textContent = resolved;
        }

        // Resolve content names for any rows whose targetKey looks GUID-ish.
        // Keeps the grid free of GUIDs once the round-trip lands.
        function resolveNames() {
            var seen = {};
            var guids = [];
            state.rows.forEach(function (r) {
                var k = (r.targetKey || '').trim();
                if (!k) return;
                if (r.contentName) return; // already resolved server-side
                if (!seen[k.toLowerCase()]) { seen[k.toLowerCase()] = true; guids.push(k); }
            });
            if (!guids.length) return Promise.resolve();
            return ajax(BASE + '/ContentLookupApi/Resolve', { method: 'POST', body: guids })
                .then(function (resolved) {
                    var lookup = {};
                    (resolved || []).forEach(function (h) { lookup[(h.contentGuid || '').toLowerCase()] = h; });
                    state.rows.forEach(function (r) {
                        var hit = lookup[(r.targetKey || '').toLowerCase()];
                        if (hit) {
                            r.contentName = hit.name;
                            r.contentType = hit.contentType;
                            r.language = r.language || hit.language;
                        }
                    });
                })
                .catch(function () { /* lookup is best-effort */ });
        }

        function getDisplayedRows() {
            var rows = state.rows.slice();
            var g = (state.global || '').toLowerCase();
            if (g) {
                rows = rows.filter(function (r) {
                    return [r.phrases, r.contentName, r.targetKey, r.contentType, r.language]
                        .some(function (v) { return (v || '').toLowerCase().indexOf(g) !== -1; });
                });
            }
            var f = state.sort.field, d = state.sort.dir === 'desc' ? -1 : 1;
            rows.sort(function (a, b) {
                var av = a[f], bv = b[f];
                if (f === 'priority' || f === 'rank') {
                    av = av == null ? Number.POSITIVE_INFINITY : Number(av);
                    bv = bv == null ? Number.POSITIVE_INFINITY : Number(bv);
                    return (av - bv) * d;
                }
                av = (av || '').toString().toLowerCase();
                bv = (bv || '').toString().toLowerCase();
                return av.localeCompare(bv) * d;
            });
            return rows;
        }

        function dirtyCount() {
            var n = 0;
            state.rows.forEach(function (r) { if (r._dirty) n++; });
            return n;
        }

        function refreshChrome() {
            // Sort carets reflect the active column.
            sortBtns.forEach(function (btn) {
                var th = btn.parentElement;
                var field = th.getAttribute('data-sort');
                th.classList.remove('is-asc', 'is-desc');
                if (field === state.sort.field) th.classList.add(state.sort.dir === 'desc' ? 'is-desc' : 'is-asc');
            });

            var n = dirtyCount();
            if (drawer) {
                drawer.hidden = n === 0;
                if (drawerCount) drawerCount.textContent = String(n);
            }
            if (countEl) countEl.textContent = state.rows.length === 0
                ? '0'
                : (getDisplayedRows().length + ' / ' + state.rows.length);
        }

        function renderRows() {
            closeTypeahead();
            rowsTbody.innerHTML = '';
            var rows = getDisplayedRows();

            if (state.rows.length === 0) {
                if (emptyEl) emptyEl.hidden = false;
                refreshChrome();
                return;
            }
            if (emptyEl) emptyEl.hidden = true;

            if (rows.length === 0) {
                var tr = document.createElement('tr');
                tr.className = 'gst-pinedit__norows';
                tr.innerHTML = '<td colspan="6">'
                    + escHtml(s('profiles.detail.pinned.noMatches', 'No pins match the current filters.'))
                    + '</td>';
                rowsTbody.appendChild(tr);
                refreshChrome();
                return;
            }

            rows.forEach(function (row, i) { rowsTbody.appendChild(buildRow(row, i + 1)); });
            refreshChrome();
        }

        function typeChipClass(contentType) {
            var ct = (contentType || '').toLowerCase();
            if (ct.indexOf('product') !== -1) return 'gst-pinedit__chip is-product';
            if (ct.indexOf('article') !== -1 || ct.indexOf('news') !== -1) return 'gst-pinedit__chip is-editorial';
            if (ct.indexOf('page') !== -1) return 'gst-pinedit__chip is-page';
            return 'gst-pinedit__chip is-other';
        }

        function buildRow(row, rank) {
            var tr = document.createElement('tr');
            tr.className = 'gst-pinedit__row'
                + (row._dirty ? ' is-dirty' : '')
                + (row._isNew ? ' is-new' : '');

            // # — display rank (post-sort), tabular-num.
            var rankCell = document.createElement('td');
            rankCell.className = 'gst-pinedit__cell gst-pinedit__cell--rank';
            rankCell.textContent = String(rank).padStart(2, '0');
            tr.appendChild(rankCell);

            // Phrase — in-place text input with a hint icon for multi-phrase.
            var phraseCell = document.createElement('td');
            phraseCell.className = 'gst-pinedit__cell gst-pinedit__cell--phrase';
            var phraseInput = document.createElement('input');
            phraseInput.type = 'text';
            phraseInput.className = 'gst-pinedit__input';
            phraseInput.placeholder = s('pinned.detail.phrasePlaceholder', 'Phrase');
            phraseInput.value = row.phrases || '';
            phraseInput.addEventListener('input', function () {
                row.phrases = phraseInput.value;
                markDirty(row, tr);
            });
            phraseCell.appendChild(phraseInput);
            tr.appendChild(phraseCell);

            // Target — name pill + click-to-edit typeahead. New rows skip
            // the pill and start in input mode.
            var targetCell = document.createElement('td');
            targetCell.className = 'gst-pinedit__cell gst-pinedit__cell--target';
            buildTargetCell(row, tr, targetCell);
            tr.appendChild(targetCell);

            // Type chip — read-only echo of the resolved contentType.
            var typeCell = document.createElement('td');
            typeCell.className = 'gst-pinedit__cell gst-pinedit__cell--type';
            if (row.contentType) {
                var chip = document.createElement('span');
                chip.className = typeChipClass(row.contentType);
                chip.textContent = row.contentType;
                typeCell.appendChild(chip);
            } else if (!row._isNew) {
                var dim = document.createElement('span');
                dim.className = 'gst-pinedit__chip is-other';
                dim.textContent = s('profiles.detail.pinned.unresolved', 'unresolved');
                typeCell.appendChild(dim);
            }
            tr.appendChild(typeCell);

            // Priority — narrow numeric input. Tabular numerals.
            var priCell = document.createElement('td');
            priCell.className = 'gst-pinedit__cell gst-pinedit__cell--priority';
            var priInput = document.createElement('input');
            priInput.type = 'number';
            priInput.className = 'gst-pinedit__input gst-pinedit__input--num';
            priInput.value = row.priority != null ? row.priority : 1000;
            priInput.step = 100;
            priInput.addEventListener('input', function () {
                var v = parseFloat(priInput.value);
                row.priority = isNaN(v) ? null : v;
                markDirty(row, tr);
            });
            priCell.appendChild(priInput);
            tr.appendChild(priCell);

            // Actions — Save + Delete with status-aware coloring.
            var actCell = document.createElement('td');
            actCell.className = 'gst-pinedit__cell gst-pinedit__cell--actions';
            actCell.appendChild(buildActionBtn('save', row, tr, function () { saveRow(row, tr); }));
            actCell.appendChild(buildActionBtn('delete', row, tr, function () { deleteRow(row); }));
            tr.appendChild(actCell);

            return tr;
        }

        function buildTargetCell(row, tr, cell) {
            cell.innerHTML = '';
            cell.style.position = 'relative';
            var hasName = !!(row.contentName && !row._isNew);

            var pill = document.createElement('button');
            pill.type = 'button';
            pill.className = 'gst-pinedit__target' + (row.targetKey ? '' : ' is-empty');
            pill.title = row.targetKey || '';
            if (hasName) {
                pill.innerHTML = '<span class="gst-pinedit__target__name"></span>'
                    + '<span class="gst-pinedit__target__edit" aria-hidden="true">↪</span>';
                pill.querySelector('.gst-pinedit__target__name').textContent = row.contentName;
            } else {
                pill.innerHTML = '<span class="gst-pinedit__target__name gst-pinedit__target__name--placeholder"></span>';
                pill.querySelector('.gst-pinedit__target__name').textContent =
                    row.targetKey || s('profiles.detail.pinned.targetPlaceholder', 'Search content by name…');
            }

            var input = document.createElement('input');
            input.type = 'text';
            input.className = 'gst-pinedit__input gst-pinedit__input--target';
            input.placeholder = s('profiles.detail.pinned.targetPlaceholder', 'Search content by name…');
            input.autocomplete = 'off';
            input.value = row.contentName || row.targetKey || '';
            input.hidden = hasName; // hide while pill is shown

            var dropdown = document.createElement('div');
            dropdown.className = 'gst-pinedit__typeahead';

            function openInput() {
                pill.hidden = true;
                input.hidden = false;
                input.focus();
                input.select();
            }
            function showPill() {
                input.hidden = true;
                pill.hidden = false;
                if (row.contentName) {
                    pill.classList.remove('is-empty');
                    pill.querySelector('.gst-pinedit__target__name').textContent = row.contentName;
                    pill.querySelector('.gst-pinedit__target__name').classList.remove('gst-pinedit__target__name--placeholder');
                    if (!pill.querySelector('.gst-pinedit__target__edit')) {
                        var arrow = document.createElement('span');
                        arrow.className = 'gst-pinedit__target__edit';
                        arrow.setAttribute('aria-hidden', 'true');
                        arrow.textContent = '↪';
                        pill.appendChild(arrow);
                    }
                }
            }

            pill.addEventListener('click', openInput);

            var debounce = null;
            input.addEventListener('input', function () {
                clearTimeout(debounce);
                debounce = setTimeout(function () { searchTypeahead(input.value.trim(), dropdown, row, input, tr, cell); }, 280);
            });
            input.addEventListener('focus', function () {
                if (dropdown.children.length > 0) {
                    dropdown.style.display = 'block';
                    setActiveTypeahead(dropdown);
                }
            });
            input.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') {
                    closeTypeahead();
                    if (row.contentName && !row._isNew) showPill();
                }
            });
            input.addEventListener('blur', function () {
                // Defer so dropdown clicks fire first.
                setTimeout(function () {
                    if (dropdown.style.display !== 'block') {
                        if (row.contentName && !row._isNew) showPill();
                    }
                }, 200);
            });

            cell.appendChild(pill);
            cell.appendChild(input);
            cell.appendChild(dropdown);
        }

        function setActiveTypeahead(dd) {
            if (state.activeTypeahead && state.activeTypeahead !== dd) state.activeTypeahead.style.display = 'none';
            state.activeTypeahead = dd;
        }
        function closeTypeahead() {
            if (state.activeTypeahead) { state.activeTypeahead.style.display = 'none'; state.activeTypeahead = null; }
        }

        function searchTypeahead(query, dropdown, row, input, tr, cell) {
            if (!query || query.length < 2) { dropdown.style.display = 'none'; return; }
            var locale = state.locale ? '&locale=' + encodeURIComponent(state.locale) : '';
            ajax(BASE + '/ContentLookupApi/Search?q=' + encodeURIComponent(query) + locale)
                .then(function (results) {
                    dropdown.innerHTML = '';
                    results = results || [];
                    if (!results.length) {
                        var none = document.createElement('div');
                        none.className = 'gst-pinedit__typeahead__none';
                        none.textContent = s('profiles.detail.pinned.noTypeaheadResults', 'No matches.');
                        dropdown.appendChild(none);
                        dropdown.style.display = 'block';
                        setActiveTypeahead(dropdown);
                        return;
                    }
                    results.forEach(function (hit) {
                        var item = document.createElement('div');
                        item.className = 'gst-pinedit__typeahead__hit';
                        var name = document.createElement('span');
                        name.className = 'gst-pinedit__typeahead__name';
                        name.textContent = hit.name;
                        var meta = document.createElement('span');
                        meta.className = 'gst-pinedit__typeahead__meta';
                        meta.textContent = (hit.contentType || '') + (hit.language ? ' · ' + hit.language : '');
                        item.appendChild(name);
                        item.appendChild(meta);
                        item.addEventListener('mousedown', function (e) {
                            // mousedown so it fires before the input's blur.
                            e.preventDefault();
                            row.targetKey = hit.contentGuid;
                            row.contentName = hit.name;
                            row.contentType = hit.contentType;
                            row.language = hit.language || row.language;
                            input.value = hit.name;
                            closeTypeahead();
                            markDirty(row, tr);
                            // Re-render this cell to swap input → pill.
                            buildTargetCell(row, tr, cell);
                            // Update the type chip cell beside it.
                            var typeCell = tr.querySelector('.gst-pinedit__cell--type');
                            if (typeCell) {
                                typeCell.innerHTML = '';
                                var chip = document.createElement('span');
                                chip.className = typeChipClass(row.contentType);
                                chip.textContent = row.contentType || '';
                                typeCell.appendChild(chip);
                            }
                        });
                        dropdown.appendChild(item);
                    });
                    dropdown.style.display = 'block';
                    setActiveTypeahead(dropdown);
                })
                .catch(function () { dropdown.style.display = 'none'; });
        }

        function buildActionBtn(kind, row, tr, onClick) {
            var btn = document.createElement('button');
            btn.type = 'button';
            if (kind === 'save') {
                btn.className = 'gst-pinedit__action gst-pinedit__action--save' + (row._dirty ? ' is-dirty' : '');
                btn.innerHTML = '<span aria-hidden="true">✓</span>';
                btn.title = STRINGS.action_save || 'Save';
            } else {
                btn.className = 'gst-pinedit__action gst-pinedit__action--delete';
                btn.innerHTML = '<span aria-hidden="true">×</span>';
                btn.title = STRINGS.action_delete || 'Delete';
            }
            btn.addEventListener('click', onClick);
            return btn;
        }

        function markDirty(row, tr) {
            row._dirty = true;
            tr.classList.add('is-dirty');
            var saveBtn = tr.querySelector('.gst-pinedit__action--save');
            if (saveBtn) saveBtn.classList.add('is-dirty');
            refreshChrome();
        }

        // Construct a blank, dirty, unsaved row scoped to the current
        // (collection, locale). Used both by the explicit "+ Add pin" action
        // and as the auto-seed when a (site, locale) has no pins yet — so the
        // editor opens ready-to-fill instead of showing an empty placeholder.
        function makeBlankRow() {
            return {
                id: null,
                collectionId: state.collectionId,
                collectionKey: state.pinnedKey,
                phrases: '',
                targetKey: '',
                contentName: '',
                contentType: '',
                language: state.locale || null,
                priority: 1000,
                isActive: true,
                _dirty: true,
                _isNew: true
            };
        }

        function loadRows() {
            setAlert(null);
            var url = PROFILE_API + '/' + encodeURIComponent(profileKey)
                + '/pinned?site=' + encodeURIComponent(state.site || '')
                + '&locale=' + encodeURIComponent(state.locale || '');
            ajax(url).then(function (resp) {
                resp = resp || {};
                state.collectionId = resp.collectionId || null;
                state.pinnedKey = resp.pinnedKey || null;
                state.isGeneric = !!resp.isGeneric;
                state.rows = (resp.rows || []).map(function (r) { return Object.assign({}, r); });
                refreshKeyline();
                return resolveNames();
            }).then(function () {
                if (state.rows.length === 0) {
                    state.rows.push(makeBlankRow());
                }
                renderRows();
            }).catch(function (err) {
                setAlert(err.message, true);
            });
        }

        function buildPayload(row) {
            return {
                phrases: (row.phrases || '').trim(),
                targetKey: (row.targetKey || '').trim(),
                language: state.locale || null,
                priority: row.priority != null ? row.priority : 1000,
                isActive: row.isActive !== false
            };
        }

        // Lazy-create the underlying Graph collection on first save. The
        // pinned key is resolved server-side from the profile's formula and
        // returned as resp.pinnedKey by /api/profiles/{key}/pinned, so we can
        // call CreateCollection without asking the marketer to do anything
        // out-of-band. Caches the resolved id back on state so subsequent
        // saves in the same session don't re-create.
        function ensureCollection() {
            if (state.collectionId) return Promise.resolve(state.collectionId);
            if (!state.pinnedKey) {
                return Promise.reject(new Error(s('profiles.detail.pinned.noKey',
                    'No pinned key resolved for this profile yet — pick a site / locale first.')));
            }
            return ajax(BASE + '/PinnedApi/CreateCollection', {
                method: 'POST',
                body: { title: state.pinnedKey, key: state.pinnedKey, isActive: true }
            }).then(function (col) {
                state.collectionId = col && col.id;
                if (!state.collectionId) {
                    throw new Error(s('profiles.detail.pinned.collectionCreateFailed',
                        'Failed to create pinned collection.'));
                }
                return state.collectionId;
            });
        }

        function saveRow(row, tr) {
            var payload = buildPayload(row);
            if (!payload.phrases || !payload.targetKey) {
                setAlert(STRINGS.error_phrase_and_content_required || 'Phrase and content are required.', true);
                return Promise.reject(new Error('validation'));
            }
            return ensureCollection().then(function (collectionId) {
                var qs = '&profileKey=' + encodeURIComponent(profileKey)
                    + '&site=' + encodeURIComponent(state.site || '')
                    + '&locale=' + encodeURIComponent(state.locale || '');
                var url, method, wasNew = !!row._isNew;
                if (wasNew) {
                    url = BASE + '/PinnedApi/CreateItem?collectionId=' + encodeURIComponent(collectionId) + qs;
                    method = 'POST';
                } else {
                    url = BASE + '/PinnedApi/UpdateItem?collectionId=' + encodeURIComponent(collectionId)
                        + '&id=' + encodeURIComponent(row.id) + qs;
                    method = 'PUT';
                }
                return ajax(url, { method: method, body: payload }).then(function (result) {
                    if (wasNew && result && result.id) { row.id = result.id; row._isNew = false; }
                    row._dirty = false;
                    setAlert(STRINGS[wasNew ? 'created' : 'updated']
                        || (wasNew ? 'Pinned item created.' : 'Pinned item updated.'));
                    renderRows();
                });
            }).catch(function (err) { setAlert(err.message, true); throw err; });
        }

        function deleteRow(row) {
            if (row._isNew) {
                state.rows = state.rows.filter(function (r) { return r !== row; });
                renderRows();
                return;
            }
            if (!confirm(STRINGS.confirm_delete || 'Delete this pinned item?')) return;
            var qs = '?collectionId=' + encodeURIComponent(state.collectionId)
                + '&id=' + encodeURIComponent(row.id)
                + '&profileKey=' + encodeURIComponent(profileKey)
                + '&site=' + encodeURIComponent(state.site || '')
                + '&locale=' + encodeURIComponent(state.locale || '')
                + '&phrases=' + encodeURIComponent(row.phrases || '');
            ajax(BASE + '/PinnedApi/DeleteItem' + qs, { method: 'DELETE' }).then(function () {
                state.rows = state.rows.filter(function (r) { return r !== row; });
                setAlert(STRINGS.deleted || 'Pinned item deleted.');
                renderRows();
            }).catch(function (err) { setAlert(err.message, true); });
        }

        function addNewRow() {
            state.rows.push(makeBlankRow());
            renderRows();
            // Focus the new row's phrase input.
            var rows = rowsTbody.querySelectorAll('tr.gst-pinedit__row');
            var last = rows[rows.length - 1];
            if (last) {
                last.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                var input = last.querySelector('.gst-pinedit__input');
                if (input && input.focus) input.focus();
            }
        }

        function saveAll() {
            var dirty = state.rows.filter(function (r) { return r._dirty; });
            if (!dirty.length) return;
            // Sequential to keep error messages legible.
            var p = Promise.resolve();
            dirty.forEach(function (r) {
                p = p.then(function () { return saveRow(r, /* tr */ null).catch(function () { /* keep going */ }); });
            });
        }

        function discardAll() {
            // New rows drop; existing dirty rows reload from server.
            state.rows = state.rows.filter(function (r) { return !r._isNew; });
            state.rows.forEach(function (r) { delete r._dirty; });
            loadRows();
        }

        // --- Live preview. SERP-style result list rendered from the
        //     registered profile's GraphQL document. The server projects each
        //     item via a heuristic field chain (Name/Title, Url/RelativePath,
        //     _fulltext/Excerpt) and ships the raw JSON alongside so the
        //     "{ }" toggle on each card can inspect everything the query
        //     returns, not just the rendered fields. ---
        function wireTryIt() {
            if (!opts.hasGraphQLDoc) return;
            var qInput = document.getElementById('gst-pin-tryit-q');
            var resultsEl = document.getElementById('gst-pin-tryit-results');
            var stats = document.getElementById('gst-pin-tryit-stats');
            if (!qInput || !resultsEl) return;

            var debounce = null;
            var lastQuery = '';
            qInput.addEventListener('input', function () {
                clearTimeout(debounce);
                debounce = setTimeout(run, 320);
            });

            function clearStats() {
                if (!stats) return;
                stats.textContent = '';
                stats.classList.remove('gst-serp__stats--err');
            }
            function setStats(text, isError) {
                if (!stats) return;
                stats.textContent = text;
                stats.classList.toggle('gst-serp__stats--err', !!isError);
            }

            function run() {
                var q = qInput.value.trim();
                if (q.length < 2) {
                    resultsEl.innerHTML = '';
                    clearStats();
                    return;
                }
                lastQuery = q;
                resultsEl.classList.add('is-loading');
                var url = PROFILE_API + '/' + encodeURIComponent(profileKey)
                    + '/preview?phrase=' + encodeURIComponent(q)
                    + '&locale=' + encodeURIComponent(state.locale || '');
                ajax(url).then(function (result) {
                    if (qInput.value.trim() !== lastQuery) return; // stale
                    resultsEl.classList.remove('is-loading');
                    var hits = (result && result.hits) || [];
                    var total = (result && result.totalCount) || 0;
                    var ms = (result && result.durationMs) || 0;
                    setStats(formatStats(hits.length, total, ms));
                    renderResults(hits, q);
                }).catch(function (err) {
                    if (qInput.value.trim() !== lastQuery) return;
                    resultsEl.classList.remove('is-loading');
                    resultsEl.innerHTML = '';
                    setStats((err && err.message) || s('profiles.detail.pinned.previewFailed', 'preview failed'), true);
                });
            }

            function formatStats(shown, total, ms) {
                // "12 of 47 hits · 142 ms"  /  "0 hits · 142 ms"
                if (!total && !shown) return s('profiles.detail.pinned.serpEmpty', 'No matches.') + ' · ' + ms + ' ms';
                if (!total || total === shown) return shown + ' hits · ' + ms + ' ms';
                return shown + ' of ' + total + ' hits · ' + ms + ' ms';
            }

            function renderResults(hits, phrase) {
                resultsEl.innerHTML = '';
                if (!hits.length) {
                    var empty = document.createElement('li');
                    empty.className = 'gst-serp__empty';
                    empty.textContent = s('profiles.detail.pinned.serpEmptyHelp',
                        'No content matched this phrase. Try a different term, or pin a target above.');
                    resultsEl.appendChild(empty);
                    return;
                }
                // Pin status is decided server-side (ProfilesService.RunPreviewAsync
                // intersects the collection's item targetKeys with the hit GUIDs);
                // the JS just reads the boolean. Falls back to false when the
                // registered query doesn't project ContentLink.GuidValue.
                hits.forEach(function (hit, idx) {
                    resultsEl.appendChild(buildResultCard(hit, phrase, idx, !!hit.pinned));
                });
            }

            function buildResultCard(hit, phrase, idx, isPinned) {
                var li = document.createElement('li');
                li.className = 'gst-serp__hit' + (isPinned ? ' is-pinned' : '');
                li.style.setProperty('--gst-serp-stagger', (idx * 28) + 'ms');

                if (isPinned) {
                    var ribbon = document.createElement('span');
                    ribbon.className = 'gst-serp__pin-ribbon';
                    ribbon.setAttribute('aria-hidden', 'true');
                    ribbon.innerHTML = '<svg viewBox="0 0 12 12" width="11" height="11">'
                        + '<path d="M6 1.5 L7.4 4.4 L10.5 4.7 L8.2 6.8 L8.9 9.9 L6 8.4 L3.1 9.9 L3.8 6.8 L1.5 4.7 L4.6 4.4 Z" fill="currentColor"/>'
                        + '</svg>';
                    li.appendChild(ribbon);
                }

                // — Header row: title (left) + score badge (right) —
                var header = document.createElement('div');
                header.className = 'gst-serp__hit-head';

                var title = document.createElement('a');
                title.className = 'gst-serp__title';
                title.href = hit.url || '#';
                if (!hit.url) title.classList.add('is-disabled');
                title.target = hit.url ? '_blank' : '_self';
                title.rel = 'noopener noreferrer';
                title.appendChild(highlightFragment(hit.name || s('profiles.detail.pinned.serpUntitled', '(untitled)'), phrase));
                header.appendChild(title);

                if (hit.score) {
                    var score = document.createElement('span');
                    score.className = 'gst-serp__score';
                    score.title = s('profiles.detail.pinned.serpScoreTooltip', 'Graph relevance score');
                    score.textContent = formatScore(hit.score);
                    header.appendChild(score);
                }
                li.appendChild(header);

                // — URL row —
                if (hit.url) {
                    var urlLine = document.createElement('div');
                    urlLine.className = 'gst-serp__url';
                    var glyph = document.createElement('span');
                    glyph.className = 'gst-serp__url-glyph';
                    glyph.textContent = '›';
                    urlLine.appendChild(glyph);
                    urlLine.appendChild(document.createTextNode(' ' + hit.url));
                    li.appendChild(urlLine);
                }

                // — Snippet —
                if (hit.fullTextSnippet) {
                    var snippet = document.createElement('p');
                    snippet.className = 'gst-serp__snippet';
                    snippet.appendChild(highlightFragment(hit.fullTextSnippet, phrase));
                    li.appendChild(snippet);
                }

                // — Meta row: pinned chip (if applicable) + type chip + language + JSON toggle —
                var meta = document.createElement('div');
                meta.className = 'gst-serp__meta';

                if (isPinned) {
                    var pinChip = document.createElement('span');
                    pinChip.className = 'gst-serp__chip gst-serp__chip--pinned';
                    pinChip.textContent = s('profiles.detail.pinned.serpPinnedBadge', 'Pinned');
                    pinChip.title = s('profiles.detail.pinned.serpPinnedTooltip',
                        'This result is locked to the top by a pin in this profile.');
                    meta.appendChild(pinChip);
                }

                if (hit.contentType) {
                    var chip = document.createElement('span');
                    chip.className = 'gst-serp__chip';
                    chip.textContent = hit.contentType;
                    meta.appendChild(chip);
                }
                if (hit.language) {
                    var lang = document.createElement('span');
                    lang.className = 'gst-serp__lang';
                    lang.textContent = hit.language;
                    meta.appendChild(lang);
                }

                if (hit.raw) {
                    var jsonBtn = document.createElement('button');
                    jsonBtn.type = 'button';
                    jsonBtn.className = 'gst-serp__json-toggle';
                    jsonBtn.setAttribute('aria-expanded', 'false');
                    jsonBtn.innerHTML = '<span class="gst-serp__json-toggle__brace">{ }</span>'
                        + '<span class="gst-serp__json-toggle__label">'
                        + escHtml(s('profiles.detail.pinned.serpShowJson', 'inspect'))
                        + '</span>';
                    meta.appendChild(jsonBtn);

                    var jsonPanel = document.createElement('pre');
                    jsonPanel.className = 'gst-serp__json';
                    jsonPanel.hidden = true;
                    jsonPanel.textContent = hit.raw;

                    jsonBtn.addEventListener('click', function () {
                        var open = jsonPanel.hidden;
                        jsonPanel.hidden = !open;
                        jsonBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
                        jsonBtn.classList.toggle('is-open', open);
                    });

                    li.appendChild(meta);
                    li.appendChild(jsonPanel);
                } else {
                    li.appendChild(meta);
                }

                return li;
            }

            function formatScore(score) {
                // Keep it short — Graph scores often have many decimals.
                if (score >= 100) return Math.round(score).toString();
                if (score >= 10) return score.toFixed(1);
                return score.toFixed(2);
            }

            // Splits text on case-insensitive matches of `phrase` (or its
            // whitespace-tokenized parts) and wraps matches in <mark>. Returns
            // a DocumentFragment so callers can append directly.
            function highlightFragment(text, phrase) {
                var frag = document.createDocumentFragment();
                if (!text) return frag;
                if (!phrase || phrase.length < 2) {
                    frag.appendChild(document.createTextNode(text));
                    return frag;
                }
                var tokens = phrase.split(/\s+/).filter(function (t) { return t.length >= 2; });
                if (!tokens.length) {
                    frag.appendChild(document.createTextNode(text));
                    return frag;
                }
                var pattern = new RegExp('(' + tokens.map(escapeRegex).join('|') + ')', 'gi');
                var lastIdx = 0;
                text.replace(pattern, function (match, _g1, offset) {
                    if (offset > lastIdx) {
                        frag.appendChild(document.createTextNode(text.slice(lastIdx, offset)));
                    }
                    var mark = document.createElement('mark');
                    mark.className = 'gst-serp__mark';
                    mark.textContent = match;
                    frag.appendChild(mark);
                    lastIdx = offset + match.length;
                    return match;
                });
                if (lastIdx < text.length) {
                    frag.appendChild(document.createTextNode(text.slice(lastIdx)));
                }
                return frag;
            }

            function escapeRegex(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
        }

        // --- Wire pickers ---
        if (siteSel && !siteSel.disabled) {
            siteSel.addEventListener('change', function () {
                state.site = siteSel.value;
                loadRows();
            });
        }
        if (localeSel && !localeSel.disabled) {
            localeSel.addEventListener('change', function () {
                state.locale = localeSel.value;
                loadRows();
            });
        }

        // Top + empty-state add buttons → addNewRow. The legacy gst-pin-add
        // button is still tolerated so older custom layouts don't break.
        [addBtn, addBtn2, addEmpty].forEach(function (b) {
            if (b) b.addEventListener('click', addNewRow);
        });

        // Global filter — debounce input → re-render.
        if (globalFilter) {
            var gd = null;
            globalFilter.addEventListener('input', function () {
                clearTimeout(gd);
                gd = setTimeout(function () { state.global = globalFilter.value; renderRows(); }, 120);
            });
        }

        // Sort headers.
        sortBtns.forEach(function (btn) {
            var th = btn.parentElement;
            var field = th.getAttribute('data-sort');
            btn.addEventListener('click', function () {
                if (state.sort.field === field) {
                    state.sort.dir = state.sort.dir === 'asc' ? 'desc' : 'asc';
                } else {
                    state.sort.field = field;
                    state.sort.dir = field === 'priority' || field === 'rank' ? 'asc' : 'asc';
                }
                renderRows();
            });
        });

        if (saveAllBtn) saveAllBtn.addEventListener('click', saveAll);
        if (discardBtn) discardBtn.addEventListener('click', discardAll);

        // Click anywhere outside a target cell → close typeahead.
        document.addEventListener('click', function (e) {
            if (state.activeTypeahead && !e.target.closest('.gst-pinedit__cell--target')) {
                closeTypeahead();
            }
        });

        refreshKeyline();
        loadRows();
        wireTryIt();

        return {
            reload: loadRows
        };
    }

    window.GST = window.GST || {};
    window.GST.pinned = window.GST.pinned || {};
    window.GST.pinned.editor = editor;
})();
