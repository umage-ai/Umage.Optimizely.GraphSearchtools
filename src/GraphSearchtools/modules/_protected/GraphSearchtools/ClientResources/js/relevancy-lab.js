/**
 * Graph Search Tools — Relevancy Lab.
 *
 * Three modes share one page (tabbed):
 *   1. Sets    — list, edit, delete golden query sets. Each set is a
 *                bundle of (phrase, expectedTop[]) pairs. Expected hits use
 *                the shared GST.contentPicker to look up CMS content.
 *   2. Run     — pick a saved set + RankingConfig, dispatch the run, render
 *                per-query NDCG/MRR + the run aggregate. Persisted runs
 *                appear in the history table directly below.
 *   3. Compare — pick two runs, see per-phrase deltas with the larger swings
 *                surfaced first.
 *
 * Vanilla JS, namespace: GST.relevancyLab. All API paths derive from
 * window.GST_BASE_URL so the same JS works under every shell host.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.relevancyLab) || {};
    var SHARED = (window.GST_STRINGS && window.GST_STRINGS.shared) || {};

    function s(key, fallback) { return STRINGS[key] || fallback; }

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
                    var msg = SHARED.failed || 'Request failed';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    var err = new Error(msg + ' (' + resp.status + ')');
                    err.status = resp.status;
                    throw err;
                });
            }
            if (resp.status === 204) return null;
            var ct = resp.headers.get('content-type') || '';
            if (ct.indexOf('application/json') >= 0) return resp.json();
            return resp.text();
        });
    }

    function escHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function fmtScore(n) {
        if (n == null || isNaN(n)) return '—';
        return (Math.round(n * 1000) / 1000).toFixed(3);
    }

    function fmtDelta(n) {
        if (n == null || isNaN(n)) return '—';
        var rounded = Math.round(n * 1000) / 1000;
        var sign = rounded > 0 ? '+' : '';
        return sign + rounded.toFixed(3);
    }

    function fmtTimestamp(iso) {
        if (!iso) return '';
        try {
            var d = new Date(iso);
            if (isNaN(d.getTime())) return iso;
            return d.toLocaleString();
        } catch (_) { return iso; }
    }

    function setAlert(elem, message, isError) {
        if (!elem) return;
        if (!message) {
            elem.hidden = true;
            elem.textContent = '';
            elem.classList.remove('gst-alert--danger');
            return;
        }
        elem.hidden = false;
        elem.textContent = message;
        elem.classList.toggle('gst-alert--danger', !!isError);
    }

    // ──────────────────────────────────────────────────────────────────
    //   Sets pane
    // ──────────────────────────────────────────────────────────────────

    function renderSetsList(rootEl, state) {
        var container = rootEl.querySelector('[data-rl="sets-list"]');
        if (!container) return;
        if (!state.sets || state.sets.length === 0) {
            container.innerHTML = '<div class="gst-rl-empty">' + escHtml(s('sets_empty', 'No golden sets yet. Click "New set" to create one.')) + '</div>';
            return;
        }
        var rows = state.sets.map(function (set) {
            var itemCount = (set.items && set.items.length) || 0;
            return [
                '<div class="gst-rl-set-row" data-set-id="' + escHtml(set.id) + '">',
                '  <div class="gst-rl-set-row__main">',
                '    <strong>' + escHtml(set.name) + '</strong>',
                set.locale ? ' <span class="gst-muted">[' + escHtml(set.locale) + ']</span>' : '',
                set.description ? '<div class="gst-muted gst-rl-set-row__desc">' + escHtml(set.description) + '</div>' : '',
                '  </div>',
                '  <div class="gst-rl-set-row__meta">',
                '    <span>' + itemCount + ' ' + escHtml(s('items_count', 'phrase(s)')) + '</span>',
                '    <button type="button" class="gst-btn gst-btn--sm" data-rl="set-edit">' + escHtml(s('edit', 'Edit')) + '</button>',
                '  </div>',
                '</div>'
            ].join('');
        }).join('');
        container.innerHTML = rows;
    }

    function renderSetEditor(rootEl, state) {
        var card = rootEl.querySelector('[data-rl="set-editor"]');
        if (!card) return;
        var editing = state.editingSet;
        card.hidden = !editing;
        if (!editing) return;

        rootEl.querySelector('[data-rl="set-editor-title"]').textContent =
            editing.id ? s('set_editor_edit', 'Edit golden set') : s('set_editor_new', 'New golden set');
        rootEl.querySelector('[data-rl-field="name"]').value = editing.name || '';
        rootEl.querySelector('[data-rl-field="locale"]').value = editing.locale || '';
        rootEl.querySelector('[data-rl-field="description"]').value = editing.description || '';

        rootEl.querySelector('[data-rl="set-delete"]').hidden = !editing.id;

        renderItems(rootEl, editing);
    }

    function renderItems(rootEl, editing) {
        var container = rootEl.querySelector('[data-rl="items"]');
        if (!container) return;
        var items = editing.items || [];
        if (items.length === 0) {
            container.innerHTML = '<div class="gst-rl-empty gst-rl-empty--small">' + escHtml(s('items_empty', 'No phrases yet — add the queries you want to score.')) + '</div>';
            return;
        }
        container.innerHTML = items.map(function (item, idx) {
            var hits = (item.expectedTop || []).map(function (hit, hidx) {
                return [
                    '<div class="gst-rl-hit" data-hit-idx="' + hidx + '">',
                    '  <input type="text" class="gst-rl-hit__link" data-rl-field="contentLink" value="' + escHtml(hit.contentLink || '') + '" placeholder="' + escHtml(s('hit_link_placeholder', 'GUID or content id')) + '" />',
                    '  <label class="gst-rl-hit__weight">',
                    '    <span>' + escHtml(s('hit_weight', 'Weight')) + '</span>',
                    '    <input type="number" min="0" step="1" data-rl-field="weight" value="' + escHtml(hit.weight != null ? hit.weight : 1) + '" />',
                    '  </label>',
                    '  <button type="button" class="gst-btn gst-btn--sm" data-rl="hit-pick">' + escHtml(s('hit_pick', 'Pick…')) + '</button>',
                    '  <button type="button" class="gst-btn gst-btn--sm" data-rl="hit-remove" title="' + escHtml(s('hit_remove', 'Remove hit')) + '">×</button>',
                    '</div>'
                ].join('');
            }).join('');
            return [
                '<fieldset class="gst-rl-item" data-item-idx="' + idx + '">',
                '  <div class="gst-rl-item__head">',
                '    <input type="text" class="gst-rl-item__phrase" data-rl-field="phrase" value="' + escHtml(item.phrase || '') + '" placeholder="' + escHtml(s('phrase_placeholder', 'Search phrase')) + '" />',
                '    <button type="button" class="gst-btn gst-btn--sm" data-rl="item-remove" title="' + escHtml(s('item_remove', 'Remove phrase')) + '">×</button>',
                '  </div>',
                '  <div class="gst-rl-hits">' + hits + '</div>',
                '  <button type="button" class="gst-btn gst-btn--sm" data-rl="hit-add">' + escHtml(s('hit_add', '+ Add expected hit')) + '</button>',
                '</fieldset>'
            ].join('');
        }).join('');
    }

    function syncEditorFromDom(rootEl, state) {
        var editing = state.editingSet;
        if (!editing) return;
        editing.name = rootEl.querySelector('[data-rl-field="name"]').value || '';
        editing.locale = rootEl.querySelector('[data-rl-field="locale"]').value || '';
        editing.description = rootEl.querySelector('[data-rl-field="description"]').value || '';

        var items = [];
        Array.prototype.forEach.call(rootEl.querySelectorAll('fieldset.gst-rl-item'), function (fs) {
            var phrase = fs.querySelector('[data-rl-field="phrase"]').value || '';
            var hits = [];
            Array.prototype.forEach.call(fs.querySelectorAll('.gst-rl-hit'), function (hitEl) {
                var link = hitEl.querySelector('[data-rl-field="contentLink"]').value || '';
                var weight = parseInt(hitEl.querySelector('[data-rl-field="weight"]').value, 10);
                hits.push({ contentLink: link, weight: isNaN(weight) ? 1 : weight });
            });
            items.push({ phrase: phrase, expectedTop: hits });
        });
        editing.items = items;
    }

    // ──────────────────────────────────────────────────────────────────
    //   Run pane
    // ──────────────────────────────────────────────────────────────────

    function refreshRunSetSelect(rootEl, state) {
        var sel = rootEl.querySelector('[data-rl="run-set"]');
        if (!sel) return;
        var prev = sel.value;
        sel.innerHTML = (state.sets || []).map(function (set) {
            return '<option value="' + escHtml(set.id) + '">' + escHtml(set.name) + '</option>';
        }).join('');
        if (prev) sel.value = prev;
    }

    function renderRunResults(rootEl, run) {
        var card = rootEl.querySelector('[data-rl="run-results"]');
        if (!card) return;
        if (!run) { card.hidden = true; return; }
        card.hidden = false;

        var summary = (s('run_summary', 'Run %name — NDCG@10 %ndcg · MRR %mrr'))
            .replace('%name', run.goldenSetName || '')
            .replace('%ndcg', fmtScore(run.ndcg10))
            .replace('%mrr', fmtScore(run.mrr));
        rootEl.querySelector('[data-rl="run-summary"]').textContent = summary;

        var exportLink = rootEl.querySelector('[data-rl="run-export"]');
        if (exportLink) exportLink.setAttribute('href', BASE + '/RelevancyLabApi/ExportCsv?id=' + encodeURIComponent(run.id));

        var table = rootEl.querySelector('[data-rl="run-table"]');
        if (!table) return;
        var rows = (run.perQuery || []).map(function (q) {
            var top = (q.actualTop || []).slice(0, 5).map(escHtml).join('<br/>');
            var err = q.error ? '<div class="gst-rl-err">' + escHtml(q.error) + '</div>' : '';
            return [
                '<tr>',
                '  <td>' + escHtml(q.phrase) + err + '</td>',
                '  <td class="num">' + fmtScore(q.ndcg10) + '</td>',
                '  <td class="num">' + fmtScore(q.mrr) + '</td>',
                '  <td class="num">' + (q.hits || 0) + '</td>',
                '  <td><div class="gst-rl-toplist">' + (top || '<span class="gst-muted">—</span>') + '</div></td>',
                '</tr>'
            ].join('');
        }).join('');
        table.innerHTML = [
            '<table class="gst-table">',
            '  <thead><tr>',
            '    <th>' + escHtml(s('col_phrase', 'Phrase')) + '</th>',
            '    <th class="num">NDCG@10</th>',
            '    <th class="num">MRR</th>',
            '    <th class="num">' + escHtml(s('col_hits', 'Hits')) + '</th>',
            '    <th>' + escHtml(s('col_top', 'Top results')) + '</th>',
            '  </tr></thead>',
            '  <tbody>' + (rows || '<tr><td colspan="5" class="gst-muted">' + escHtml(s('run_empty', 'No phrases evaluated.')) + '</td></tr>') + '</tbody>',
            '</table>'
        ].join('');
    }

    function renderHistory(rootEl, runs) {
        var container = rootEl.querySelector('[data-rl="history-list"]');
        if (!container) return;
        if (!runs || runs.length === 0) {
            container.innerHTML = '<div class="gst-rl-empty gst-rl-empty--small">' + escHtml(s('history_empty', 'No runs recorded yet.')) + '</div>';
            return;
        }
        var rows = runs.map(function (run) {
            var cfg = run.config || {};
            var cfgLabel = (cfg.ranking || 'Relevance') + ' · w=' + (cfg.semanticWeight != null ? cfg.semanticWeight : 0)
                + (cfg.minScore != null ? ' · min=' + cfg.minScore : '');
            return [
                '<tr>',
                '  <td>' + escHtml(fmtTimestamp(run.at)) + '</td>',
                '  <td>' + escHtml(run.goldenSetName || '') + '</td>',
                '  <td>' + escHtml(cfgLabel) + '</td>',
                '  <td class="num">' + fmtScore(run.ndcg10) + '</td>',
                '  <td class="num">' + fmtScore(run.mrr) + '</td>',
                '  <td><a href="' + BASE + '/RelevancyLabApi/ExportCsv?id=' + encodeURIComponent(run.id) + '" class="gst-btn gst-btn--sm">' + escHtml(s('run_export_csv', 'Export CSV')) + '</a></td>',
                '</tr>'
            ].join('');
        }).join('');
        container.innerHTML = [
            '<table class="gst-table">',
            '  <thead><tr>',
            '    <th>' + escHtml(s('col_when', 'When')) + '</th>',
            '    <th>' + escHtml(s('col_set', 'Set')) + '</th>',
            '    <th>' + escHtml(s('col_config', 'Config')) + '</th>',
            '    <th class="num">NDCG@10</th>',
            '    <th class="num">MRR</th>',
            '    <th></th>',
            '  </tr></thead>',
            '  <tbody>' + rows + '</tbody>',
            '</table>'
        ].join('');
    }

    // ──────────────────────────────────────────────────────────────────
    //   Compare pane
    // ──────────────────────────────────────────────────────────────────

    function refreshCompareSelects(rootEl, runs) {
        ['compare-a', 'compare-b'].forEach(function (key) {
            var sel = rootEl.querySelector('[data-rl="' + key + '"]');
            if (!sel) return;
            var prev = sel.value;
            sel.innerHTML = (runs || []).map(function (run) {
                var label = (run.goldenSetName || '') + ' · ' + fmtTimestamp(run.at) + ' · NDCG ' + fmtScore(run.ndcg10);
                return '<option value="' + escHtml(run.id) + '">' + escHtml(label) + '</option>';
            }).join('');
            if (prev) sel.value = prev;
        });
    }

    function renderCompareResults(rootEl, result) {
        var card = rootEl.querySelector('[data-rl="compare-results"]');
        if (!card) return;
        if (!result) { card.hidden = true; return; }
        card.hidden = false;

        var summary = (s('compare_summary', 'NDCG@10 delta %ndcg · MRR delta %mrr'))
            .replace('%ndcg', fmtDelta(result.ndcgDelta))
            .replace('%mrr', fmtDelta(result.mrrDelta));
        rootEl.querySelector('[data-rl="compare-summary"]').textContent = summary;

        var table = rootEl.querySelector('[data-rl="compare-table"]');
        if (!table) return;
        var rows = (result.entries || []).map(function (entry) {
            var cls = '';
            if (entry.ndcgDelta > 0.05) cls = 'gst-rl-delta--up';
            else if (entry.ndcgDelta < -0.05) cls = 'gst-rl-delta--down';
            return [
                '<tr class="' + cls + '">',
                '  <td>' + escHtml(entry.phrase) + '</td>',
                '  <td class="num">' + fmtScore(entry.ndcgA) + '</td>',
                '  <td class="num">' + fmtScore(entry.ndcgB) + '</td>',
                '  <td class="num"><strong>' + fmtDelta(entry.ndcgDelta) + '</strong></td>',
                '  <td class="num">' + fmtScore(entry.mrrA) + '</td>',
                '  <td class="num">' + fmtScore(entry.mrrB) + '</td>',
                '  <td class="num">' + fmtDelta(entry.mrrDelta) + '</td>',
                '</tr>'
            ].join('');
        }).join('');
        table.innerHTML = [
            '<table class="gst-table">',
            '  <thead><tr>',
            '    <th>' + escHtml(s('col_phrase', 'Phrase')) + '</th>',
            '    <th class="num">NDCG A</th>',
            '    <th class="num">NDCG B</th>',
            '    <th class="num">Δ NDCG</th>',
            '    <th class="num">MRR A</th>',
            '    <th class="num">MRR B</th>',
            '    <th class="num">Δ MRR</th>',
            '  </tr></thead>',
            '  <tbody>' + rows + '</tbody>',
            '</table>'
        ].join('');
    }

    // ──────────────────────────────────────────────────────────────────
    //   Boot
    // ──────────────────────────────────────────────────────────────────

    function boot(opts) {
        var rootEl = (opts && opts.container) || document.body;
        var alertBox = document.getElementById('gst-alert');

        var state = {
            sets: [],
            runs: [],
            editingSet: null,
            currentRun: null,
            currentCompare: null
        };

        function loadSets() {
            return ajax(BASE + '/RelevancyLabApi/ListSets').then(function (sets) {
                state.sets = Array.isArray(sets) ? sets : [];
                renderSetsList(rootEl, state);
                refreshRunSetSelect(rootEl, state);
            }).catch(function (err) {
                setAlert(alertBox, s('load_failed', 'Could not load Relevancy Lab.') + ' ' + (err.message || ''), true);
            });
        }

        function loadRuns() {
            return ajax(BASE + '/RelevancyLabApi/ListRuns').then(function (runs) {
                state.runs = Array.isArray(runs) ? runs : [];
                renderHistory(rootEl, state.runs);
                refreshCompareSelects(rootEl, state.runs);
            }).catch(function () { /* tolerant — empty history is fine */ });
        }

        // Tabs
        Array.prototype.forEach.call(rootEl.querySelectorAll('[data-rl-tab]'), function (btn) {
            btn.addEventListener('click', function () {
                var key = btn.getAttribute('data-rl-tab');
                Array.prototype.forEach.call(rootEl.querySelectorAll('[data-rl-tab]'), function (b) {
                    var active = b === btn;
                    b.classList.toggle('gst-tab--active', active);
                    b.setAttribute('aria-selected', active ? 'true' : 'false');
                });
                Array.prototype.forEach.call(rootEl.querySelectorAll('[data-rl-pane]'), function (p) {
                    p.hidden = p.getAttribute('data-rl-pane') !== key;
                });
            });
        });

        // ─── Sets editor wiring ──────────────────
        var setsList = rootEl.querySelector('[data-rl="sets-list"]');
        if (setsList) {
            setsList.addEventListener('click', function (e) {
                var btn = e.target.closest('[data-rl="set-edit"]');
                if (!btn) return;
                var row = btn.closest('[data-set-id]');
                if (!row) return;
                var id = row.getAttribute('data-set-id');
                ajax(BASE + '/RelevancyLabApi/GetSet?id=' + encodeURIComponent(id)).then(function (set) {
                    state.editingSet = set || { id: '', name: '', locale: '', description: '', items: [] };
                    renderSetEditor(rootEl, state);
                }).catch(function (err) {
                    setAlert(alertBox, s('load_failed', 'Could not load set.') + ' ' + (err.message || ''), true);
                });
            });
        }

        var newBtn = rootEl.querySelector('[data-rl="set-new"]');
        if (newBtn) newBtn.addEventListener('click', function () {
            state.editingSet = { id: '', name: '', locale: '', description: '', items: [] };
            renderSetEditor(rootEl, state);
        });

        var editorCard = rootEl.querySelector('[data-rl="set-editor"]');
        if (editorCard) {
            editorCard.addEventListener('click', function (e) {
                var btn = e.target.closest('button[data-rl]');
                if (!btn) return;
                var act = btn.getAttribute('data-rl');
                if (act === 'set-cancel') {
                    state.editingSet = null;
                    renderSetEditor(rootEl, state);
                } else if (act === 'set-save') {
                    syncEditorFromDom(rootEl, state);
                    btn.disabled = true;
                    ajax(BASE + '/RelevancyLabApi/UpsertSet', { method: 'POST', body: state.editingSet })
                        .then(function (saved) {
                            state.editingSet = saved;
                            return loadSets();
                        })
                        .then(function () {
                            renderSetEditor(rootEl, state);
                            setAlert(alertBox, s('set_saved', 'Golden set saved.'), false);
                            setTimeout(function () { setAlert(alertBox, ''); }, 1500);
                        })
                        .catch(function (err) {
                            setAlert(alertBox, s('save_failed', 'Could not save set.') + ' ' + (err.message || ''), true);
                        })
                        .finally(function () { btn.disabled = false; });
                } else if (act === 'set-delete') {
                    if (!state.editingSet || !state.editingSet.id) return;
                    if (!window.confirm(s('confirm_delete_set', 'Delete this golden set?'))) return;
                    ajax(BASE + '/RelevancyLabApi/DeleteSet?id=' + encodeURIComponent(state.editingSet.id), { method: 'POST' })
                        .then(function () {
                            state.editingSet = null;
                            return loadSets();
                        })
                        .then(function () {
                            renderSetEditor(rootEl, state);
                            setAlert(alertBox, s('set_deleted', 'Golden set deleted.'), false);
                            setTimeout(function () { setAlert(alertBox, ''); }, 1500);
                        })
                        .catch(function (err) {
                            setAlert(alertBox, s('delete_failed', 'Could not delete set.') + ' ' + (err.message || ''), true);
                        });
                } else if (act === 'item-add') {
                    syncEditorFromDom(rootEl, state);
                    state.editingSet.items.push({ phrase: '', expectedTop: [{ contentLink: '', weight: 1 }] });
                    renderItems(rootEl, state.editingSet);
                } else if (act === 'item-remove') {
                    var fs = btn.closest('fieldset.gst-rl-item');
                    if (!fs) return;
                    var idx = parseInt(fs.getAttribute('data-item-idx'), 10);
                    syncEditorFromDom(rootEl, state);
                    state.editingSet.items.splice(idx, 1);
                    renderItems(rootEl, state.editingSet);
                } else if (act === 'hit-add') {
                    var fs2 = btn.closest('fieldset.gst-rl-item');
                    if (!fs2) return;
                    var idx2 = parseInt(fs2.getAttribute('data-item-idx'), 10);
                    syncEditorFromDom(rootEl, state);
                    state.editingSet.items[idx2].expectedTop = state.editingSet.items[idx2].expectedTop || [];
                    state.editingSet.items[idx2].expectedTop.push({ contentLink: '', weight: 1 });
                    renderItems(rootEl, state.editingSet);
                } else if (act === 'hit-remove') {
                    var fs3 = btn.closest('fieldset.gst-rl-item');
                    var hitEl = btn.closest('.gst-rl-hit');
                    if (!fs3 || !hitEl) return;
                    var idx3 = parseInt(fs3.getAttribute('data-item-idx'), 10);
                    var hidx = parseInt(hitEl.getAttribute('data-hit-idx'), 10);
                    syncEditorFromDom(rootEl, state);
                    state.editingSet.items[idx3].expectedTop.splice(hidx, 1);
                    renderItems(rootEl, state.editingSet);
                } else if (act === 'hit-pick') {
                    if (typeof GST.contentPicker !== 'function') return;
                    GST.contentPicker({}).then(function (picked) {
                        if (!picked) return;
                        var hitEl = btn.closest('.gst-rl-hit');
                        if (!hitEl) return;
                        var input = hitEl.querySelector('[data-rl-field="contentLink"]');
                        // Prefer the GUID when the picker yields one — otherwise fall
                        // back to the numeric Id; either form roundtrips through the
                        // service's NormaliseLink matcher.
                        if (input) input.value = picked.guid || picked.guidValue || picked.id || '';
                    });
                }
            });
        }

        // ─── Run pane wiring ──────────────────
        var runWeight = rootEl.querySelector('[data-rl="run-weight"]');
        var runWeightOut = rootEl.querySelector('[data-rl="run-weight-out"]');
        if (runWeight && runWeightOut) {
            runWeight.addEventListener('input', function () {
                runWeightOut.textContent = (parseFloat(runWeight.value) || 0).toFixed(2);
            });
        }
        var runGo = rootEl.querySelector('[data-rl="run-go"]');
        if (runGo) {
            runGo.addEventListener('click', function () {
                var setId = (rootEl.querySelector('[data-rl="run-set"]') || {}).value;
                if (!setId) {
                    setAlert(alertBox, s('run_pick_set', 'Pick a golden set first.'), true);
                    return;
                }
                var minScoreRaw = (rootEl.querySelector('[data-rl="run-minscore"]') || {}).value;
                var body = {
                    goldenSetId: setId,
                    config: {
                        ranking: (rootEl.querySelector('[data-rl="run-ranking"]') || {}).value || 'Relevance',
                        semanticWeight: parseFloat((rootEl.querySelector('[data-rl="run-weight"]') || {}).value) || 0,
                        minScore: minScoreRaw === '' || minScoreRaw == null ? null : parseFloat(minScoreRaw)
                    }
                };
                runGo.disabled = true;
                ajax(BASE + '/RelevancyLabApi/Run', { method: 'POST', body: body })
                    .then(function (run) {
                        state.currentRun = run;
                        renderRunResults(rootEl, run);
                        return loadRuns();
                    })
                    .catch(function (err) {
                        setAlert(alertBox, s('run_failed', 'Run failed.') + ' ' + (err.message || ''), true);
                    })
                    .finally(function () { runGo.disabled = false; });
            });
        }

        // ─── Compare pane wiring ──────────────────
        var compareGo = rootEl.querySelector('[data-rl="compare-go"]');
        if (compareGo) {
            compareGo.addEventListener('click', function () {
                var a = (rootEl.querySelector('[data-rl="compare-a"]') || {}).value;
                var b = (rootEl.querySelector('[data-rl="compare-b"]') || {}).value;
                if (!a || !b) {
                    setAlert(alertBox, s('compare_pick_runs', 'Pick two runs to compare.'), true);
                    return;
                }
                if (a === b) {
                    setAlert(alertBox, s('compare_same', 'Pick two different runs.'), true);
                    return;
                }
                compareGo.disabled = true;
                ajax(BASE + '/RelevancyLabApi/Compare?a=' + encodeURIComponent(a) + '&b=' + encodeURIComponent(b))
                    .then(function (result) {
                        state.currentCompare = result;
                        renderCompareResults(rootEl, result);
                    })
                    .catch(function (err) {
                        setAlert(alertBox, s('compare_failed', 'Could not compare runs.') + ' ' + (err.message || ''), true);
                    })
                    .finally(function () { compareGo.disabled = false; });
            });
        }

        loadSets().then(loadRuns);

        return {
            reloadSets: loadSets,
            reloadRuns: loadRuns,
            getState: function () { return state; }
        };
    }

    window.GST = window.GST || {};
    window.GST.relevancyLab = { boot: boot };
})();
