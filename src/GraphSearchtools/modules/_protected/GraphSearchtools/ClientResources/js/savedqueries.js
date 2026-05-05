/**
 * Graph Search Tools — Saved Queries.
 *
 * Combined runner + preset library:
 *   • Left rail lists DDS-persisted presets. Click a preset to load it into
 *     the runner and execute. Inline edit/delete per row.
 *   • Right column hosts the runner (phrase + locale + ranking knobs) and
 *     a results table. The exact GraphQL document is exposed via "Show query".
 *   • "Save" pops a small dialog (name + description) that saves the current
 *     runner state — updates the loaded preset if one is loaded, otherwise
 *     creates a new one. Holding Shift forces "save as new".
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.savedqueries) || {};

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var addBtn = document.getElementById('sq-add');
    var filterInput = document.getElementById('sq-filter');
    var presetsEl = document.getElementById('sq-presets');

    var queryInput = document.getElementById('sq-query');
    var localeSelect = document.getElementById('sq-locale');
    var runBtn = document.getElementById('sq-run');
    var saveBtn = document.getElementById('sq-save');

    var rankingSelect = document.getElementById('sq-ranking');
    var weightInput = document.getElementById('sq-weight');
    var weightOut = document.getElementById('sq-weight-out');
    var minScoreInput = document.getElementById('sq-min-score');
    var limitInput = document.getElementById('sq-limit');

    var meta = document.getElementById('sq-meta');
    var grid = document.getElementById('sq-grid');
    var showQueryBtn = document.getElementById('sq-show-query');
    var queryText = document.getElementById('sq-query-text');

    var dialog = document.getElementById('sq-dialog');
    var dialogTitle = document.getElementById('sq-dialog-title');
    var dialogClose = document.getElementById('sq-dialog-close');
    var dialogCancel = document.getElementById('sq-dialog-cancel');
    var dialogSave = document.getElementById('sq-dialog-save');
    var dlgName = document.getElementById('sq-dlg-name');
    var dlgDesc = document.getElementById('sq-dlg-description');

    // ── State ────────────────────────────────────────────────────
    var presets = [];
    var graphLocales = [];
    /** ID of the preset currently loaded into the runner — null = unsaved. */
    var loadedId = null;
    /** When true, dialog Save creates a new record instead of updating loadedId. */
    var saveAsNew = false;

    // ── HTTP ─────────────────────────────────────────────────────

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

    // ── Locales ──────────────────────────────────────────────────

    function renderLocales() {
        localeSelect.innerHTML = '';
        var optAll = document.createElement('option');
        optAll.value = '';
        optAll.textContent = STRINGS.all_locales || 'ALL';
        localeSelect.appendChild(optAll);
        graphLocales.forEach(function (code) {
            var opt = document.createElement('option');
            opt.value = code;
            opt.textContent = code;
            localeSelect.appendChild(opt);
        });
    }

    // ── Preset rail ──────────────────────────────────────────────

    function getFilteredPresets() {
        var text = filterInput.value.trim().toLowerCase();
        if (!text) return presets;
        return presets.filter(function (p) {
            return (p.name || '').toLowerCase().indexOf(text) !== -1
                || (p.query || '').toLowerCase().indexOf(text) !== -1
                || (p.description || '').toLowerCase().indexOf(text) !== -1;
        });
    }

    function renderPresets() {
        presetsEl.innerHTML = '';
        if (!presets.length) {
            var empty = document.createElement('div');
            empty.className = 'gst-sq-rail__empty';
            empty.textContent = STRINGS.no_items || 'No saved queries yet.';
            presetsEl.appendChild(empty);
            return;
        }
        var visible = getFilteredPresets();
        if (!visible.length) {
            var nope = document.createElement('div');
            nope.className = 'gst-sq-rail__empty';
            nope.textContent = STRINGS.no_filter_match || 'No saved queries match the filter.';
            presetsEl.appendChild(nope);
            return;
        }
        visible.forEach(function (p) { presetsEl.appendChild(buildPresetCard(p)); });
    }

    function buildPresetCard(p) {
        var card = document.createElement('div');
        card.className = 'gst-sq-preset' + (p.id === loadedId ? ' is-loaded' : '');
        card.setAttribute('role', 'button');
        card.tabIndex = 0;
        card.title = p.description || p.query || '';

        var header = document.createElement('div');
        header.className = 'gst-sq-preset__head';
        var name = document.createElement('span');
        name.className = 'gst-sq-preset__name';
        name.textContent = p.name;
        header.appendChild(name);

        var actions = document.createElement('div');
        actions.className = 'gst-sq-preset__actions';
        actions.appendChild(iconButton(STRINGS.action_edit || 'Edit', 'gst-sq-preset__act',
            'M12 20h9 M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4z',
            function (e) { e.stopPropagation(); openDialog(p); }));
        actions.appendChild(iconButton(STRINGS.action_delete || 'Delete', 'gst-sq-preset__act gst-sq-preset__act--danger',
            'M3 6h18 M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2 M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6',
            function (e) { e.stopPropagation(); deletePreset(p); }));
        header.appendChild(actions);
        card.appendChild(header);

        if (p.description) {
            var desc = document.createElement('div');
            desc.className = 'gst-sq-preset__desc';
            desc.textContent = p.description;
            card.appendChild(desc);
        }

        var meta = document.createElement('div');
        meta.className = 'gst-sq-preset__meta';
        var phrase = document.createElement('span');
        phrase.className = 'gst-sq-preset__phrase';
        phrase.textContent = p.query || '—';
        meta.appendChild(phrase);

        var pills = document.createElement('span');
        pills.className = 'gst-sq-preset__pills';
        pills.appendChild(pill(p.locale || (STRINGS.all_locales || 'ALL')));
        if (p.ranking && p.ranking !== 'RELEVANCE') pills.appendChild(pill(p.ranking));
        meta.appendChild(pills);
        card.appendChild(meta);

        card.addEventListener('click', function () { loadPreset(p, /*run*/ true); });
        card.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); loadPreset(p, /*run*/ true); }
        });
        return card;
    }

    function pill(text) {
        var s = document.createElement('span');
        s.className = 'gst-sq-pill';
        s.textContent = text;
        return s;
    }

    function iconButton(label, className, pathD, onClick) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'gst-btn gst-btn--icon ' + className;
        b.setAttribute('aria-label', label);
        b.title = label;
        b.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="' + pathD + '"/></svg>';
        b.addEventListener('click', onClick);
        return b;
    }

    // ── Runner state <-> form ────────────────────────────────────

    function loadPreset(p, runIt) {
        loadedId = p.id;
        queryInput.value = p.query || '';
        localeSelect.value = p.locale || '';
        rankingSelect.value = p.ranking || 'RELEVANCE';
        weightInput.value = (p.semanticWeight != null) ? p.semanticWeight : 0.2;
        weightOut.textContent = Number(weightInput.value).toFixed(2);
        minScoreInput.value = (p.minimumScore == null) ? '' : p.minimumScore;
        limitInput.value = p.limit || 25;
        renderPresets(); // refresh "is-loaded" highlight
        if (runIt) run();
    }

    function snapshotState() {
        return {
            query: queryInput.value.trim(),
            locale: localeSelect.value || null,
            ranking: rankingSelect.value || 'RELEVANCE',
            semanticWeight: parseFloat(weightInput.value) || 0.2,
            minimumScore: minScoreInput.value === '' ? null : parseFloat(minScoreInput.value),
            limit: Math.max(1, Math.min(100, parseInt(limitInput.value, 10) || 25))
        };
    }

    // ── Run ──────────────────────────────────────────────────────

    function run() {
        var state = snapshotState();
        if (!state.query) {
            setAlert(STRINGS.error_query_required || 'Enter a query to run.', true);
            return;
        }
        setAlert(null);
        runBtn.disabled = true;
        meta.textContent = STRINGS.running || 'Running…';
        showQueryBtn.hidden = true;
        queryText.hidden = true;

        ajax(BASE + '/SavedQueriesApi/Run', { method: 'POST', body: state })
            .then(renderResult)
            .catch(function (err) {
                setAlert((STRINGS.run_failed || 'Search failed') + ': ' + err.message, true);
                meta.textContent = '';
            })
            .then(function () { runBtn.disabled = false; });
    }

    function renderResult(result) {
        grid.innerHTML = '';
        var hits = (result && result.hits) || [];
        meta.textContent = (STRINGS.returned_for || 'Returned %1 of %2 in %3 ms')
            .replace('%1', hits.length)
            .replace('%2', result.totalCount)
            .replace('%3', result.durationMs);
        if (result && result.graphQuery) {
            queryText.textContent = result.graphQuery.trim();
            showQueryBtn.hidden = false;
        }

        if (!hits.length) {
            var emptyRow = document.createElement('tr');
            var cell = document.createElement('td');
            cell.colSpan = 6;
            cell.className = 'gst-sq-empty';
            cell.textContent = STRINGS.no_results || 'No matching content.';
            emptyRow.appendChild(cell);
            grid.appendChild(emptyRow);
            return;
        }

        hits.forEach(function (hit, index) {
            var tr = document.createElement('tr');

            var rankCell = document.createElement('td');
            rankCell.className = 'gst-sq-col-rank';
            rankCell.textContent = String(index + 1);
            tr.appendChild(rankCell);

            var nameCell = document.createElement('td');
            if (hit.contentId) {
                var link = document.createElement('a');
                link.href = (window.GST_CMS_URL || '') + '?language=' + encodeURIComponent(hit.language || '') + '#context=epi.cms.contentdata:///' + hit.contentId;
                link.target = '_blank';
                link.rel = 'noopener';
                link.textContent = hit.name || '(unnamed)';
                nameCell.appendChild(link);
            } else {
                nameCell.textContent = hit.name || '(unnamed)';
            }
            tr.appendChild(nameCell);

            var typeCell = document.createElement('td');
            typeCell.className = 'gst-sq-col-type';
            typeCell.textContent = hit.contentType || '';
            tr.appendChild(typeCell);

            var langCell = document.createElement('td');
            langCell.className = 'gst-sq-col-lang';
            langCell.textContent = hit.language || '';
            tr.appendChild(langCell);

            var scoreCell = document.createElement('td');
            scoreCell.className = 'gst-sq-col-score';
            scoreCell.textContent = (hit.score == null) ? '' : Number(hit.score).toFixed(3);
            tr.appendChild(scoreCell);

            var snipCell = document.createElement('td');
            snipCell.className = 'gst-sq-col-snippet';
            snipCell.textContent = hit.fullTextSnippet || '';
            tr.appendChild(snipCell);

            grid.appendChild(tr);
        });
    }

    // ── Save dialog ──────────────────────────────────────────────

    function openDialog(presetForEdit) {
        if (presetForEdit) {
            // Editing an existing preset — also load its values into the runner.
            loadedId = presetForEdit.id;
            saveAsNew = false;
            dialogTitle.textContent = STRINGS.edit_title || 'Edit saved query';
            dlgName.value = presetForEdit.name;
            dlgDesc.value = presetForEdit.description || '';
            loadPreset(presetForEdit, /*run*/ false);
        } else {
            // Saving the current runner state. If a preset is currently loaded
            // *and* shift wasn't held on the Save button, default to updating it.
            var existing = loadedId ? presets.find(function (p) { return p.id === loadedId; }) : null;
            if (existing && !saveAsNew) {
                dialogTitle.textContent = STRINGS.edit_title || 'Edit saved query';
                dlgName.value = existing.name;
                dlgDesc.value = existing.description || '';
            } else {
                dialogTitle.textContent = STRINGS.new_title || 'New saved query';
                dlgName.value = '';
                dlgDesc.value = '';
            }
        }
        dialog.hidden = false;
        dlgName.focus();
        dlgName.select();
    }

    function closeDialog() {
        dialog.hidden = true;
        saveAsNew = false;
    }

    function savePreset() {
        var name = dlgName.value.trim();
        if (!name) {
            setAlert(STRINGS.error_name_required || 'Name is required.', true);
            return;
        }
        var state = snapshotState();
        var payload = {
            name: name,
            description: dlgDesc.value.trim(),
            query: state.query,
            locale: state.locale,
            ranking: state.ranking,
            semanticWeight: state.semanticWeight,
            minimumScore: state.minimumScore,
            limit: state.limit
        };
        var existing = (loadedId && !saveAsNew) ? presets.find(function (p) { return p.id === loadedId; }) : null;
        var promise = existing
            ? ajax(BASE + '/SavedQueriesApi/Update?id=' + encodeURIComponent(existing.id), { method: 'PUT', body: payload })
            : ajax(BASE + '/SavedQueriesApi/Create', { method: 'POST', body: payload });

        promise.then(function (saved) {
            if (existing) {
                presets = presets.map(function (p) { return p.id === existing.id ? saved : p; });
            } else {
                presets.push(saved);
                loadedId = saved.id;
            }
            renderPresets();
            closeDialog();
            setAlert(existing ? (STRINGS.updated || 'Saved query updated.') : (STRINGS.created || 'Saved query saved.'));
        }).catch(function (err) { setAlert(err.message, true); });
    }

    function deletePreset(p) {
        if (!confirm((STRINGS.confirm_delete || 'Delete saved query "%1"?').replace('%1', p.name))) return;
        ajax(BASE + '/SavedQueriesApi/Delete?id=' + encodeURIComponent(p.id), { method: 'DELETE' })
            .then(function () {
                presets = presets.filter(function (x) { return x.id !== p.id; });
                if (loadedId === p.id) loadedId = null;
                renderPresets();
                setAlert(STRINGS.deleted || 'Saved query deleted.');
            })
            .catch(function (err) { setAlert(err.message, true); });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    weightInput.addEventListener('input', function () {
        weightOut.textContent = Number(weightInput.value).toFixed(2);
    });
    runBtn.addEventListener('click', run);
    queryInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); run(); }
    });
    showQueryBtn.addEventListener('click', function () {
        queryText.hidden = !queryText.hidden;
        showQueryBtn.textContent = queryText.hidden
            ? (STRINGS.show_query || 'Show query')
            : (STRINGS.hide_query || 'Hide query');
    });

    // Save: Shift-click forces "save as new" even when a preset is loaded.
    saveBtn.addEventListener('click', function (e) {
        if (!queryInput.value.trim()) {
            setAlert(STRINGS.error_query_required || 'Enter a query to run.', true);
            return;
        }
        saveAsNew = !!e.shiftKey;
        openDialog(null);
    });

    addBtn.addEventListener('click', function () {
        // "+ New preset" always opens an empty dialog.
        loadedId = null;
        saveAsNew = true;
        openDialog(null);
        renderPresets();
    });

    filterInput.addEventListener('input', renderPresets);
    dialogClose.addEventListener('click', closeDialog);
    dialogCancel.addEventListener('click', closeDialog);
    dialogSave.addEventListener('click', savePreset);
    dialog.addEventListener('click', function (e) { if (e.target === dialog) closeDialog(); });
    dlgName.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); savePreset(); } });

    // ── Boot ─────────────────────────────────────────────────────

    Promise.all([
        ajax(BASE + '/SitesApi/Locales'),
        ajax(BASE + '/SavedQueriesApi/List')
    ]).then(function (results) {
        graphLocales = results[0] || [];
        presets = results[1] || [];
        renderLocales();
        renderPresets();
    }).catch(function (err) { setAlert(err.message, true); });
})();
