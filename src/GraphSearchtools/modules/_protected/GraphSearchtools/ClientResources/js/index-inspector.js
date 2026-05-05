/**
 * Graph Search Tools — Index Inspector.
 *
 * Surfaces a snapshot of how the Optimizely Graph index is populated per
 * content type, plus a top-line count of items missing the basic editorial
 * fields (Name / Title). Read-only — Refresh re-runs the inspection.
 *
 * Phase 4 §6: "Index size by content type; missing fields (Name/Title);
 * strings flagged unsearchable that 'should be'; recent reindex deltas."
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.indexInspector) || {};

    // ── DOM ─────────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var refreshBtn = document.getElementById('ii-refresh');
    var grid = document.getElementById('ii-grid');
    var totalCell = document.getElementById('ii-total');
    var typesCell = document.getElementById('ii-types');
    var missingNameCell = document.getElementById('ii-missing-name');
    var capturedCell = document.getElementById('ii-captured');

    // ── Utilities ──────────────────────────────────────────────────

    function ajax(url) {
        return fetch(url, {
            method: 'GET',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.load_failed || 'Could not load index snapshot';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
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

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function fmtCount(n) {
        if (n == null) return '—';
        try { return Number(n).toLocaleString(); } catch (_) { return String(n); }
    }

    function fmtMissing(n) {
        // Distinguish "field not applicable to this type" (null) from
        // "field applicable but zero missing" (0). Editors care which.
        if (n == null) return '<span class="gst-muted">—</span>';
        return escHtml(fmtCount(n));
    }

    function fmtDate(value) {
        if (!value) return '—';
        try {
            var d = new Date(value);
            if (isNaN(d.getTime())) return '—';
            return d.toLocaleString();
        } catch (_) { return '—'; }
    }

    // ── Render ─────────────────────────────────────────────────────

    function renderStats(snapshot) {
        totalCell.textContent = fmtCount(snapshot.totalItems);
        typesCell.textContent = fmtCount((snapshot.perContentType || []).length);
        missingNameCell.textContent = fmtCount(snapshot.missingNameCount);
        capturedCell.textContent = fmtDate(snapshot.capturedAt);
    }

    function renderGrid(rows) {
        grid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="5" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No content types in the index.') + '</p></td>';
            grid.appendChild(emptyTr);
            return;
        }
        // Already sorted count-desc by the service, but defend against a
        // server reordering — the warning-row visual only makes sense if the
        // user can locate the worst offenders quickly.
        var sorted = rows.slice().sort(function (a, b) { return (b.count || 0) - (a.count || 0); });
        sorted.forEach(function (row) {
            var tr = document.createElement('tr');
            var hasWarning = row.missingNameCount && row.missingNameCount > 0;
            if (hasWarning) tr.classList.add('gst-row--warn');
            var nameCell = escHtml(row.name || '');
            if (hasWarning) {
                nameCell += ' <span class="gst-badge gst-badge--warning" title="' + escHtml(STRINGS.status_warn || 'Items missing Name') + '">!</span>';
            }
            tr.innerHTML =
                '<td>' + nameCell + '</td>' +
                '<td>' + escHtml(fmtCount(row.count)) + '</td>' +
                '<td>' + fmtMissing(row.missingNameCount) + '</td>' +
                '<td>' + fmtMissing(row.missingTeaserCount) + '</td>' +
                '<td>' + fmtMissing(row.missingMainBodyCount) + '</td>';
            grid.appendChild(tr);
        });
    }

    function load() {
        setAlert('');
        refreshBtn.disabled = true;
        ajax(BASE + '/IndexInspectorApi/Get')
            .then(function (snapshot) {
                renderStats(snapshot || {});
                renderGrid((snapshot && snapshot.perContentType) || []);
            })
            .catch(function (err) {
                setAlert((STRINGS.load_failed || 'Could not load index snapshot.') + ' ' + err.message, true);
            })
            .then(function () { refreshBtn.disabled = false; });
    }

    // ── Wire-up ────────────────────────────────────────────────────

    refreshBtn.addEventListener('click', load);
    load();
})();
