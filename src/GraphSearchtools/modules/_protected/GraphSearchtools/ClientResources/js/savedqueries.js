/**
 * Graph Search Tools — Saved Queries.
 *
 * CRUD over the local DDS-backed list of named Search Console presets.
 * The "Run" action navigates to Search Console with the preset values in the
 * URL hash; Search Console is responsible for reading them on load.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.savedqueries) || {};

    var alertBox = document.getElementById('gst-alert');
    var grid = document.getElementById('sq-grid');
    var filterInput = document.getElementById('sq-filter');
    var addBtn = document.getElementById('sq-add');
    var emptyEl = document.getElementById('sq-empty');
    var contentEl = document.getElementById('sq-content');
    var emptyAddBtn = document.getElementById('sq-empty-add');

    var dialog = document.getElementById('sq-dialog');
    var dialogTitle = document.getElementById('sq-dialog-title');
    var dialogClose = document.getElementById('sq-dialog-close');
    var dialogCancel = document.getElementById('sq-dialog-cancel');
    var dialogSave = document.getElementById('sq-dialog-save');

    var nameInput = document.getElementById('sq-name');
    var descInput = document.getElementById('sq-description');
    var queryInput = document.getElementById('sq-query');
    var localeSelect = document.getElementById('sq-locale');
    var rankingSelect = document.getElementById('sq-ranking');
    var weightInput = document.getElementById('sq-weight');
    var minScoreInput = document.getElementById('sq-min-score');
    var limitInput = document.getElementById('sq-limit');

    var allItems = [];
    var sites = [];
    var editingId = null;

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
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            return resp.status === 204 ? null : resp.json();
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

    function loadAll() {
        Promise.all([
            ajax(BASE + '/SitesApi/List'),
            ajax(BASE + '/SavedQueriesApi/List')
        ]).then(function (results) {
            sites = results[0] || [];
            allItems = results[1] || [];
            renderLocaleOptions();
            render();
        }).catch(function (err) { setAlert(err.message, true); });
    }

    function renderLocaleOptions() {
        localeSelect.innerHTML = '';
        var optAny = document.createElement('option');
        optAny.value = '';
        optAny.textContent = STRINGS.any_locale || 'Any locale';
        localeSelect.appendChild(optAny);
        sites.forEach(function (site) {
            var opt = document.createElement('option');
            opt.value = site.languageCode;
            opt.textContent = site.title + ' (' + site.languageCode + ')';
            localeSelect.appendChild(opt);
        });
    }

    function getFiltered() {
        var text = filterInput.value.trim().toLowerCase();
        if (!text) return allItems;
        return allItems.filter(function (i) {
            return (i.name || '').toLowerCase().indexOf(text) !== -1
                || (i.query || '').toLowerCase().indexOf(text) !== -1
                || (i.description || '').toLowerCase().indexOf(text) !== -1;
        });
    }

    function render() {
        // No items at all → show the empty-state CTA card and hide the grid.
        // Some items but the filter excludes them all → keep the grid visible
        // (so the filter input stays reachable) and show an empty row.
        if (!allItems.length) {
            emptyEl.hidden = false;
            contentEl.hidden = true;
            return;
        }
        emptyEl.hidden = true;
        contentEl.hidden = false;

        grid.innerHTML = '';
        var items = getFiltered();
        if (!items.length) {
            var tr = document.createElement('tr');
            var td = document.createElement('td');
            td.colSpan = 5;
            td.className = 'gst-sq-empty';
            td.textContent = STRINGS.no_filter_match || 'No saved queries match the filter.';
            tr.appendChild(td);
            grid.appendChild(tr);
            return;
        }
        items.forEach(function (item) { grid.appendChild(buildRow(item)); });
    }

    function buildRow(item) {
        var tr = document.createElement('tr');

        var tdName = document.createElement('td');
        var nameDiv = document.createElement('div');
        nameDiv.className = 'gst-sq-name';
        nameDiv.textContent = item.name;
        tdName.appendChild(nameDiv);
        if (item.description) {
            var descDiv = document.createElement('div');
            descDiv.className = 'gst-sq-desc';
            descDiv.textContent = item.description;
            tdName.appendChild(descDiv);
        }
        tr.appendChild(tdName);

        var tdQuery = document.createElement('td');
        tdQuery.className = 'gst-sq-query-cell';
        tdQuery.textContent = item.query || '';
        tr.appendChild(tdQuery);

        var tdRanking = document.createElement('td');
        tdRanking.className = 'gst-sq-col-ranking';
        tdRanking.textContent = item.ranking || 'RELEVANCE';
        tr.appendChild(tdRanking);

        var tdLocale = document.createElement('td');
        tdLocale.className = 'gst-sq-col-locale';
        tdLocale.textContent = item.locale || '—';
        tr.appendChild(tdLocale);

        var tdActions = document.createElement('td');
        tdActions.className = 'gst-sq-col-actions';
        tdActions.appendChild(button(STRINGS.action_run || 'Run', 'gst-btn gst-btn--sm gst-btn--primary', function () { runInConsole(item); }));
        tdActions.appendChild(button(STRINGS.action_edit || 'Edit', 'gst-btn gst-btn--sm', function () { openDialog(item); }));
        tdActions.appendChild(button(STRINGS.action_delete || 'Delete', 'gst-btn gst-btn--sm', function () { deleteItem(item); }));
        tr.appendChild(tdActions);
        return tr;
    }

    function button(label, className, onClick) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = className;
        b.textContent = label;
        b.addEventListener('click', onClick);
        return b;
    }

    function runInConsole(item) {
        var hash = '#' + new URLSearchParams({
            q: item.query || '',
            locale: item.locale || '',
            ranking: item.ranking || 'RELEVANCE',
            weight: String(item.semanticWeight),
            min: item.minimumScore == null ? '' : String(item.minimumScore),
            limit: String(item.limit || 25)
        }).toString();
        window.location.href = BASE + '/GraphSearchtools/SearchConsole' + hash;
    }

    function openDialog(item) {
        editingId = item ? item.id : null;
        dialogTitle.textContent = item
            ? (STRINGS.edit_title || 'Edit saved query')
            : (STRINGS.new_title || 'New saved query');
        nameInput.value = item ? item.name : '';
        descInput.value = item ? item.description : '';
        queryInput.value = item ? item.query : '';
        localeSelect.value = item && item.locale ? item.locale : '';
        rankingSelect.value = item ? item.ranking : 'RELEVANCE';
        weightInput.value = item ? item.semanticWeight : 0.2;
        minScoreInput.value = item && item.minimumScore != null ? item.minimumScore : '';
        limitInput.value = item ? item.limit : 25;
        dialog.hidden = false;
        nameInput.focus();
    }

    function closeDialog() {
        dialog.hidden = true;
        editingId = null;
    }

    function save() {
        var payload = {
            name: nameInput.value.trim(),
            description: descInput.value.trim(),
            query: queryInput.value.trim(),
            locale: localeSelect.value || null,
            ranking: rankingSelect.value || 'RELEVANCE',
            semanticWeight: parseFloat(weightInput.value) || 0.2,
            minimumScore: minScoreInput.value === '' ? null : parseFloat(minScoreInput.value),
            limit: Math.max(1, Math.min(100, parseInt(limitInput.value, 10) || 25))
        };
        if (!payload.name) {
            setAlert(STRINGS.error_name_required || 'Name is required.', true);
            return;
        }
        var promise = editingId
            ? ajax(BASE + '/SavedQueriesApi/Update?id=' + encodeURIComponent(editingId), { method: 'PUT', body: payload })
            : ajax(BASE + '/SavedQueriesApi/Create', { method: 'POST', body: payload });
        promise.then(function (saved) {
            if (editingId) {
                allItems = allItems.map(function (i) { return i.id === editingId ? saved : i; });
            } else {
                allItems.push(saved);
            }
            render();
            closeDialog();
            setAlert(editingId ? (STRINGS.updated || 'Saved query updated.') : (STRINGS.created || 'Saved query created.'));
        }).catch(function (err) { setAlert(err.message, true); });
    }

    function deleteItem(item) {
        if (!confirm((STRINGS.confirm_delete || 'Delete saved query "%1"?').replace('%1', item.name))) return;
        ajax(BASE + '/SavedQueriesApi/Delete?id=' + encodeURIComponent(item.id), { method: 'DELETE' })
            .then(function () {
                allItems = allItems.filter(function (i) { return i.id !== item.id; });
                render();
                setAlert(STRINGS.deleted || 'Saved query deleted.');
            })
            .catch(function (err) { setAlert(err.message, true); });
    }

    addBtn.addEventListener('click', function () { openDialog(null); });
    emptyAddBtn.addEventListener('click', function () { openDialog(null); });
    filterInput.addEventListener('input', render);
    dialogClose.addEventListener('click', closeDialog);
    dialogCancel.addEventListener('click', closeDialog);
    dialogSave.addEventListener('click', save);
    dialog.addEventListener('click', function (e) { if (e.target === dialog) closeDialog(); });

    loadAll();
})();
