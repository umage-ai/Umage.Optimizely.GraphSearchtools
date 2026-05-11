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
        var alertEl = document.getElementById('gst-pin-tab-alert');
        var addBtn = document.getElementById('gst-pinedit-add');
        var addEmpty = document.getElementById('gst-pinedit-empty-add');
        var globalFilter = document.getElementById('gst-pinedit-global');
        var countEl = document.getElementById('gst-pinedit-count');
        var drawer = document.getElementById('gst-pinedit-drawer');
        var drawerCount = document.getElementById('gst-pinedit-dirty-count');
        var saveAllBtn = document.getElementById('gst-pinedit-save-all');
        var discardBtn = document.getElementById('gst-pinedit-discard');
        var sortBtns = document.querySelectorAll('.gst-pinedit__sortbtn');
        var pagerEl = document.getElementById('gst-pinedit-pager');
        var pagerStatusEl = document.getElementById('gst-pinedit-pager-status');
        var prevBtn = document.getElementById('gst-pinedit-prev');
        var nextBtn = document.getElementById('gst-pinedit-next');

        if (!rowsTbody) return null;

        var PAGE_SIZE = 20;

        var state = {
            site: siteSel && !siteSel.disabled ? siteSel.value : '',
            locale: localeSel && !localeSel.disabled ? localeSel.value : '',
            collectionId: null,
            pinnedKey: null,
            isGeneric: !!opts.isGeneric,
            rows: [],
            // Persistent UI state across renders
            sort: { field: 'phrases', dir: 'asc' },
            global: '',
            page: 1,
            activeTypeahead: null
        };

        function setAlert(message, isError) {
            if (!alertEl) return;
            if (!message) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = message;
            alertEl.classList.toggle('gst-alert--danger', !!isError);
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
                var av = (a[f] || '').toString().toLowerCase();
                var bv = (b[f] || '').toString().toLowerCase();
                return av.localeCompare(bv) * d;
            });
            return rows;
        }

        function dirtyCount() {
            var n = 0;
            state.rows.forEach(function (r) { if (r._dirty) n++; });
            return n;
        }

        function pageCountFor(total) {
            return GST.editGrid.pageCount(total, PAGE_SIZE);
        }

        function clampPage(total) {
            var pc = pageCountFor(total);
            if (state.page > pc) state.page = pc;
            if (state.page < 1) state.page = 1;
        }

        function refreshChrome(total) {
            GST.editGrid.refreshSortCarets(sortBtns, state.sort);
            var n = dirtyCount();
            if (drawer) {
                drawer.hidden = n === 0;
                if (drawerCount) drawerCount.textContent = String(n);
            }
            if (countEl) {
                if (state.rows.length === 0) {
                    countEl.textContent = '0';
                } else {
                    var displayedTotal = (typeof total === 'number') ? total : getDisplayedRows().length;
                    countEl.textContent = displayedTotal === state.rows.length
                        ? String(state.rows.length)
                        : displayedTotal + ' / ' + state.rows.length;
                }
            }
        }

        function renderRows() {
            closeTypeahead();
            rowsTbody.innerHTML = '';

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
                tr.innerHTML = '<td colspan="3">'
                    + escHtml(s('profiles.detail.pinned.noMatches', 'No pins match the current filters.'))
                    + '</td>';
                rowsTbody.appendChild(tr);
                if (pagerEl) pagerEl.hidden = true;
                refreshChrome(0);
                return;
            }

            var start = (state.page - 1) * PAGE_SIZE;
            var pageRows = rows.slice(start, start + PAGE_SIZE);
            pageRows.forEach(function (row) { rowsTbody.appendChild(buildRow(row)); });
            GST.editGrid.renderPager({
                pagerEl: pagerEl, statusEl: pagerStatusEl,
                prevBtn: prevBtn, nextBtn: nextBtn,
                page: state.page, total: total, pageSize: PAGE_SIZE
            });
            refreshChrome(total);
        }

        function buildRow(row) {
            var tr = document.createElement('tr');
            tr.className = 'gst-pinedit__row'
                + (row._dirty ? ' is-dirty' : '')
                + (row._isNew ? ' is-new' : '');

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

            // Pill is a "click to re-pick" affordance — only meaningful when
            // a target is already resolved. New rows / unresolved rows go
            // straight to input mode so we don't render two competing
            // "Search content by name…" placeholders side by side.
            pill.hidden = !hasName;

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

        // The first loadRows() promise — exposed via whenReady() so callers
        // (e.g. the Insights tab's inline pin editor) can defer their
        // canCreatePins check until state.pinnedKey is resolved instead of
        // racing the initial fetch.
        var initialLoad = null;

        function loadRows() {
            setAlert(null);
            // Switching site / locale gives a different rowset entirely; the
            // previous page index is meaningless in the new context.
            state.page = 1;
            var url = PROFILE_API + '/' + encodeURIComponent(profileKey)
                + '/pinned?site=' + encodeURIComponent(state.site || '')
                + '&locale=' + encodeURIComponent(state.locale || '');
            var p = ajax(url).then(function (resp) {
                resp = resp || {};
                state.collectionId = resp.collectionId || null;
                state.pinnedKey = resp.pinnedKey || null;
                state.isGeneric = !!resp.isGeneric;
                state.rows = (resp.rows || []).map(function (r) { return Object.assign({}, r); });
                return resolveNames();
            }).then(function () {
                renderRows();
            }).catch(function (err) {
                setAlert(err.message, true);
            });
            if (!initialLoad) initialLoad = p;
            return p;
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
                    // Defensive: a 400 from the controller means the URL went
                    // out with an empty id. That can only happen if the row
                    // was reloaded into the grid without an id (server bug)
                    // or _isNew was cleared without setRowId firing — surface
                    // a useful message rather than the opaque 400.
                    if (!row.id) {
                        setAlert('Cannot update — pinned item id missing. Reload the page to refresh.', true);
                        return Promise.reject(new Error('missing-id'));
                    }
                    if (!collectionId) {
                        setAlert('Cannot update — collection id missing. Reload the page to refresh.', true);
                        return Promise.reject(new Error('missing-collection'));
                    }
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
            // The new row appended at the end of state.rows lands on the last
            // page after sort. Jump there so the user sees what they just added
            // instead of a stale earlier page.
            state.page = pageCountFor(getDisplayedRows().length);
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

            // Synonym hint cache. We fetch the active language's synonym
            // rules — the language-specific blob AND the global blob, since
            // both apply at query time — the first time the user previews a
            // query in that language, then parse and reuse. The chip strip
            // shown above the results lists rules whose source side appears
            // in the query — a heuristic match, not proof Graph applied
            // them, so the label intentionally says "Matching synonym rules".
            var synRulesByLang = {};
            var synFetching = {};

            window.addEventListener('gst:synonyms-changed', function (e) {
                var lang = (e && e.detail && e.detail.lang) || '';
                if (!lang) {
                    // Global change — affects every language we've cached.
                    synRulesByLang = {};
                } else {
                    delete synRulesByLang[lang];
                }
                if (qInput.value.trim().length >= 2) {
                    refreshSynStripFor(qInput.value.trim());
                }
            });

            var debounce = null;
            var lastQuery = '';
            qInput.addEventListener('input', function () {
                clearTimeout(debounce);
                debounce = setTimeout(run, 320);
            });

            // Expose run() on the editor scope so the locale picker can
            // re-execute the preview after a switch — otherwise the user
            // would have to retype the same phrase to see the new branch's
            // results. lastQuery is reset so the staleness guard inside run()
            // doesn't reject the re-issued call.
            state.rerunTryIt = function () {
                lastQuery = '';
                run();
            };

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

            // .gst-serp__input wraps the magnifier glyph + input + spinner.
            // Toggling is-loading on it shows the spinner; mirroring it on
            // the results list dims the prior hits while the new ones load.
            var inputWrap = qInput.parentNode;
            function setLoading(on) {
                resultsEl.classList.toggle('is-loading', on);
                if (inputWrap) inputWrap.classList.toggle('is-loading', on);
            }

            function run() {
                var q = qInput.value.trim();
                if (q.length < 2) {
                    resultsEl.innerHTML = '';
                    clearStats();
                    renderSynStrip([]);
                    setLoading(false);
                    return;
                }
                lastQuery = q;
                setLoading(true);
                var url = PROFILE_API + '/' + encodeURIComponent(profileKey)
                    + '/preview?phrase=' + encodeURIComponent(q)
                    + '&locale=' + encodeURIComponent(state.locale || '');
                ajax(url).then(function (result) {
                    if (qInput.value.trim() !== lastQuery) return; // stale
                    setLoading(false);
                    var hits = (result && result.hits) || [];
                    var total = (result && result.totalCount) || 0;
                    var ms = (result && result.durationMs) || 0;
                    setStats(formatStats(hits.length, total, ms));
                    renderResults(hits, q);
                }).catch(function (err) {
                    if (qInput.value.trim() !== lastQuery) return;
                    setLoading(false);
                    resultsEl.innerHTML = '';
                    setStats((err && err.message) || s('profiles.detail.pinned.previewFailed', 'preview failed'), true);
                });
                refreshSynStripFor(q);
            }

            // ── Synonym chip strip ──────────────────────────────────────
            // Latest synonym matches for the active query — read by
            // renderResults() so the empty state can name the synonym target
            // ("no content matched 'asdfg' or its synonym 'track'") instead
            // of leaving the user wondering whether the rule even fired.
            var lastSynMatches = [];

            function refreshSynStripFor(q) {
                var lang = state.locale || '';
                fetchSynonymsForLang(lang).then(function (rules) {
                    if (qInput.value.trim() !== q) return; // stale
                    var matches = matchRules(q, rules);
                    lastSynMatches = matches;
                    renderSynStrip(matches);
                    // If results already rendered as empty, refresh the empty
                    // copy now that the chips arrived (the syn fetch and the
                    // hits fetch race; either can resolve first).
                    var emptyEl = resultsEl.querySelector('.gst-serp__empty');
                    if (emptyEl) emptyEl.textContent = emptyMessageFor(q);
                });
            }

            function emptyMessageFor(phrase) {
                // Pull replacement targets out of the latest match set —
                // equivalent rules don't have a "target" word the same way.
                var targets = [];
                lastSynMatches.forEach(function (m) {
                    if (m.type === 'replacement' && m.expansion && m.expansion.length) {
                        m.expansion.forEach(function (t) {
                            if (targets.indexOf(t) === -1) targets.push(t);
                        });
                    }
                });
                if (!targets.length) {
                    return s('profiles.detail.pinned.serpEmptyHelp',
                        'No content matched this phrase. Try a different term, or pin a target above.');
                }
                var targetList = targets.map(function (t) { return '"' + t + '"'; }).join(', ');
                var tmpl = s('profiles.detail.pinned.serpEmptyHelpSyn',
                    'No content matched "{phrase}" or its synonym {target}. Synonym changes can take a few seconds to propagate; try a different target term if "{target}" isn\'t in your indexed content.');
                return tmpl.replace(/\{phrase\}/g, phrase).replace(/\{target\}/g, targetList);
            }

            function ensureSynStripEl() {
                var el = document.getElementById('gst-pin-tryit-syn');
                if (el) return el;
                el = document.createElement('div');
                el.id = 'gst-pin-tryit-syn';
                el.className = 'gst-serp__syn';
                el.hidden = true;
                resultsEl.parentNode.insertBefore(el, resultsEl);
                return el;
            }

            function fetchScopeContent(lang) {
                var qs = lang
                    ? '?languageRouting=' + encodeURIComponent(lang) + '&slot=one'
                    : '?slot=one';
                return fetch((window.GST_BASE_URL || '') + '/SynonymsApi/Get' + qs, {
                    headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    credentials: 'same-origin'
                }).then(function (r) {
                    return r.ok ? r.json() : null;
                }).then(function (result) {
                    var content = result ? result.content : '';
                    if (content && content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
                        try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
                    }
                    return content || '';
                }).catch(function () { return ''; });
            }

            // Returns the merged rule set Graph would apply when querying
            // in `lang`: language-specific blob + global blob. Cached per
            // lang and invalidated by `gst:synonyms-changed`.
            function fetchSynonymsForLang(lang) {
                if (synRulesByLang[lang] !== undefined) return Promise.resolve(synRulesByLang[lang]);
                if (synFetching[lang]) return synFetching[lang];
                var langPromise = lang ? fetchScopeContent(lang) : Promise.resolve('');
                var globalPromise = fetchScopeContent('');
                var p = Promise.all([langPromise, globalPromise]).then(function (parts) {
                    var merged = [parts[0], parts[1]].filter(Boolean).join('\n');
                    var rules = parseSynonymRules(merged);
                    synRulesByLang[lang] = rules;
                    delete synFetching[lang];
                    return rules;
                }).catch(function () {
                    synRulesByLang[lang] = [];
                    delete synFetching[lang];
                    return [];
                });
                synFetching[lang] = p;
                return p;
            }

            function parseSynonymRules(content) {
                return content.split(/\r?\n/).map(function (line) {
                    var raw = line.trim();
                    if (!raw) return null;
                    if (raw.indexOf('=>') !== -1) {
                        var parts = raw.split('=>');
                        var lhs = parts[0].split(',').map(trimLower).filter(Boolean);
                        var rhs = parts.slice(1).join('=>').split(',').map(trimLower).filter(Boolean);
                        if (!lhs.length || !rhs.length) return null;
                        return { type: 'replacement', lhs: lhs, rhs: rhs };
                    }
                    if (raw.indexOf(',') !== -1) {
                        var terms = raw.split(',').map(trimLower).filter(Boolean);
                        if (terms.length < 2) return null;
                        return { type: 'equivalent', terms: terms };
                    }
                    return null;
                }).filter(Boolean);
            }

            function trimLower(t) { return t.trim().toLowerCase(); }

            // Whole-word match against a space-padded haystack. Multi-word
            // terms work because we just look for the term flanked by spaces.
            function wholeWord(haystackPadded, term) {
                if (!term) return false;
                return haystackPadded.indexOf(' ' + term + ' ') !== -1;
            }

            function matchRules(query, rules) {
                var hay = ' ' + query.toLowerCase() + ' ';
                var matches = [];
                rules.forEach(function (r) {
                    if (r.type === 'replacement') {
                        for (var i = 0; i < r.lhs.length; i++) {
                            if (wholeWord(hay, r.lhs[i])) {
                                matches.push({ type: 'replacement', match: r.lhs[i], expansion: r.rhs });
                                return;
                            }
                        }
                    } else if (r.type === 'equivalent') {
                        for (var j = 0; j < r.terms.length; j++) {
                            if (wholeWord(hay, r.terms[j])) {
                                var others = r.terms.filter(function (t, k) { return k !== j; });
                                matches.push({ type: 'equivalent', match: r.terms[j], expansion: others });
                                return;
                            }
                        }
                    }
                });
                return matches;
            }

            function renderSynStrip(matches) {
                var el = ensureSynStripEl();
                el.innerHTML = '';
                if (!matches || !matches.length) {
                    el.hidden = true;
                    return;
                }
                el.hidden = false;
                var label = document.createElement('span');
                label.className = 'gst-serp__syn-label';
                label.textContent = s('profiles.detail.pinned.synonymMatchedLabel', 'Matching synonym rules:');
                el.appendChild(label);
                matches.forEach(function (m) {
                    var chip = document.createElement('span');
                    chip.className = 'gst-serp__syn-chip is-' + m.type;
                    chip.title = m.type === 'replacement'
                        ? s('profiles.detail.pinned.synonymReplacementTip',
                            'Replacement rule — Graph would substitute this expansion.')
                        : s('profiles.detail.pinned.synonymEquivalentTip',
                            'Equivalent rule — Graph would also match these alternates.');
                    var src = document.createElement('strong');
                    src.className = 'gst-serp__syn-src';
                    src.textContent = m.match;
                    chip.appendChild(src);
                    var op = document.createElement('span');
                    op.className = 'gst-serp__syn-op';
                    op.textContent = m.type === 'replacement' ? '→' : '↔';
                    chip.appendChild(op);
                    var exp = document.createElement('span');
                    exp.className = 'gst-serp__syn-exp';
                    exp.textContent = m.expansion.join(', ');
                    chip.appendChild(exp);
                    el.appendChild(chip);
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
                    empty.textContent = emptyMessageFor(phrase);
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
                // Server-side: AlloySearchService asks Graph to wrap matched
                // tokens (lexical AND synonym-expanded) with SOH/STX markers
                // and picks the first _fulltext entry that carries them. So
                // for a `floop => alloy` synonym, the snippet bolds "alloy"
                // (the actual matched token), not the typed "floop".
                if (hit.fullTextSnippet) {
                    var snippet = document.createElement('p');
                    snippet.className = 'gst-serp__snippet';
                    snippet.appendChild(markedFragment(hit.fullTextSnippet));
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

                    // Wrapper hosts the <pre> plus a copy button anchored
                    // top-right. The wrapper carries the hidden state so the
                    // button hides with the panel.
                    var jsonWrap = document.createElement('div');
                    jsonWrap.className = 'gst-codeblock gst-serp__json-wrap';
                    jsonWrap.hidden = true;

                    var jsonPanel = document.createElement('pre');
                    jsonPanel.className = 'gst-serp__json';
                    jsonPanel.textContent = hit.raw;
                    jsonWrap.appendChild(jsonPanel);

                    if (window.GST && typeof window.GST.copyButton === 'function') {
                        jsonWrap.appendChild(window.GST.copyButton({
                            getValue: function () { return hit.raw; },
                            className: 'gst-copybtn--overlay'
                        }));
                    }

                    jsonBtn.addEventListener('click', function () {
                        var open = jsonWrap.hidden;
                        jsonWrap.hidden = !open;
                        jsonBtn.setAttribute('aria-expanded', open ? 'true' : 'false');
                        jsonBtn.classList.toggle('is-open', open);
                    });

                    li.appendChild(meta);
                    li.appendChild(jsonWrap);
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

            // Render a snippet that arrived with Graph's native highlight
            // markers (SOH = U+0001 start, STX = U+0002 end). Splits on the
            // markers, text-nodes the surrounding spans, wraps matched spans
            // in <mark>. Unlike highlightFragment, this trusts Graph's own
            // tokenizer so synonym-expanded matches highlight correctly.
            function markedFragment(text) {
                var frag = document.createDocumentFragment();
                if (!text) return frag;
                var i = 0;
                while (i < text.length) {
                    var open = text.indexOf('\u0001', i);
                    if (open < 0) {
                        frag.appendChild(document.createTextNode(text.slice(i)));
                        break;
                    }
                    if (open > i) {
                        frag.appendChild(document.createTextNode(text.slice(i, open)));
                    }
                    var close = text.indexOf('\u0002', open + 1);
                    if (close < 0) {
                        // Stray opener — render the rest as plain text rather
                        // than dropping it.
                        frag.appendChild(document.createTextNode(text.slice(open + 1)));
                        break;
                    }
                    var mark = document.createElement('mark');
                    mark.className = 'gst-serp__mark';
                    mark.textContent = text.slice(open + 1, close);
                    frag.appendChild(mark);
                    i = close + 1;
                }
                return frag;
            }
        }

        // Mirror the select's selected option into the visible value span
        // sitting beneath the transparent overlay select. The chip's pill
        // shape is the click target (the <select> covers it); the visible
        // text comes from this span, so we have to push every change here.
        function syncChipDisplay(selectEl, displayId) {
            if (!selectEl) return;
            var disp = document.getElementById(displayId);
            if (!disp) return;
            var opt = selectEl.options[selectEl.selectedIndex];
            disp.textContent = opt ? opt.text : selectEl.value;
        }

        // --- Wire pickers ---
        // Initial sync — Razor pre-renders the first option's label, but
        // an option further down the list could have been pre-selected
        // (e.g. via persisted state); cover that case before any change.
        syncChipDisplay(siteSel, 'gst-pin-site-display');
        syncChipDisplay(localeSel, 'gst-pin-locale-display');

        if (siteSel && !siteSel.disabled) {
            siteSel.addEventListener('change', function () {
                state.site = siteSel.value;
                syncChipDisplay(siteSel, 'gst-pin-site-display');
                loadRows();
            });
        }
        if (localeSel && !localeSel.disabled) {
            localeSel.addEventListener('change', function () {
                state.locale = localeSel.value;
                syncChipDisplay(localeSel, 'gst-pin-locale-display');
                loadRows();
                // Locale changed — the live preview's input listener only fires
                // on typing, so without a kick the SERP would still reflect the
                // previous branch. The Graph language filter depends on locale,
                // so re-run if the user already has a phrase in the box.
                var strip = document.getElementById('gst-pin-tryit-syn');
                if (strip) { strip.hidden = true; strip.innerHTML = ''; }
                if (typeof state.rerunTryIt === 'function') {
                    state.rerunTryIt();
                }
            });
        }

        [addBtn, addEmpty].forEach(function (b) {
            if (b) b.addEventListener('click', addNewRow);
        });

        // Global filter — debounce input → re-render.
        if (globalFilter) {
            var gd = null;
            globalFilter.addEventListener('input', function () {
                clearTimeout(gd);
                gd = setTimeout(function () {
                    state.global = globalFilter.value;
                    // Filter narrows the result set; staying on a page that no
                    // longer exists in the narrowed view is jarring. Reset.
                    state.page = 1;
                    renderRows();
                }, 120);
            });
        }

        GST.editGrid.wireSortHeaders(sortBtns, state.sort, function () {
            state.page = 1;
            renderRows();
        });

        GST.editGrid.wirePager({
            prevBtn: prevBtn, nextBtn: nextBtn,
            pageSize: PAGE_SIZE,
            getPage: function () { return state.page; },
            setPage: function (p) { state.page = p; },
            getTotal: function () { return getDisplayedRows().length; },
            onChange: renderRows
        });

        if (saveAllBtn) saveAllBtn.addEventListener('click', saveAll);
        if (discardBtn) discardBtn.addEventListener('click', discardAll);

        // Click anywhere outside a target cell → close typeahead.
        document.addEventListener('click', function (e) {
            if (state.activeTypeahead && !e.target.closest('.gst-pinedit__cell--target')) {
                closeTypeahead();
            }
        });

        loadRows();
        wireTryIt();

        return {
            reload: loadRows,

            /**
             * Resolves when the first loadRows() settles, so callers can
             * defer state.pinnedKey-dependent checks (canCreatePins) until
             * the editor has actually fetched its config. Always returns a
             * promise — never rejects — so callers can just `.then(...)`.
             */
            whenReady: function () { return initialLoad || Promise.resolve(); },

            /**
             * Look up content by free-text query against the pinned editor's
             * existing typeahead endpoint. Used by the Insights tab inline
             * pin editor so callers can rely on the same matching rules /
             * locale scoping that the typeahead in the table cell uses.
             */
            lookupContent: function (query) {
                var locale = state.locale ? '&locale=' + encodeURIComponent(state.locale) : '';
                return ajax(BASE + '/ContentLookupApi/Search?q=' + encodeURIComponent(query) + locale);
            },

            /**
             * Whether the editor is in a state where new pins can be created
             * (collection key resolved, profile actually applies pinned).
             * The Insights inline editor disables its save button against this.
             */
            canCreatePins: function () {
                return !!state.pinnedKey && !!opts.queryAppliesPinned;
            },

            /**
             * Create-and-save a pinned item for `phrase` against `target`.
             * Skips the table-edit flow used by draftPhrase — the inline
             * Insights editor already collected the target so we go straight
             * to the wire. Adds the saved row to the table state on success
             * so opening the Pinned tab shows it without a reload.
             */
            createPin: function (phrase, target) {
                if (!phrase || !target || !target.targetKey) {
                    return Promise.reject(new Error('phrase and target are required'));
                }
                var row = {
                    id: null,
                    collectionId: state.collectionId,
                    collectionKey: state.pinnedKey,
                    phrases: phrase,
                    targetKey: target.targetKey,
                    contentName: target.contentName || '',
                    contentType: target.contentType || '',
                    language: state.locale || null,
                    priority: 1000,
                    isActive: true,
                    _dirty: true,
                    _isNew: true
                };
                state.rows.push(row);
                return saveRow(row).then(function () {
                    renderRows();
                    return row;
                });
            },

            /**
             * Seed a new draft pin row pre-filled with `phrase` and focus the
             * target cell — used by the Insights tab "draft pin" CTA so the
             * editor opens ready for the marketer to pick a target. Returns
             * true on success; false if the editor is in unwired/disabled state.
             */
            draftPhrase: function (phrase) {
                if (!phrase) return false;
                state.rows.push({
                    id: null,
                    collectionId: state.collectionId,
                    collectionKey: state.pinnedKey,
                    phrases: phrase,
                    targetKey: '',
                    contentName: '',
                    contentType: '',
                    language: state.locale || null,
                    priority: 1000,
                    isActive: true,
                    _dirty: true,
                    _isNew: true
                });
                state.page = pageCountFor(getDisplayedRows().length);
                renderRows();
                // Focus the target cell so the marketer's next click picks
                // a content target rather than re-typing the phrase.
                var rows = rowsTbody.querySelectorAll('tr.gst-pinedit__row');
                var last = rows[rows.length - 1];
                if (last) {
                    last.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                    var targetInput = last.querySelector('.gst-pinedit__cell--target input, .gst-pinedit__cell--target .gst-pinedit__input');
                    if (targetInput && targetInput.focus) targetInput.focus();
                }
                return true;
            }
        };
    }

    window.GST = window.GST || {};
    window.GST.pinned = window.GST.pinned || {};
    window.GST.pinned.editor = editor;
})();
