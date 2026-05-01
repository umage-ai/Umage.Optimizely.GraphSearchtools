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

    addButton.addEventListener('click', addNewRow);
    pinFilter.addEventListener('input', renderGrid);
    pinCollectionFilter.addEventListener('change', renderGrid);

    document.addEventListener('click', function (e) {
        if (activeDropdown && !e.target.closest('.gst-pin-content-cell')) {
            closeActiveDropdown();
        }
    });

    loadAll();
})();
