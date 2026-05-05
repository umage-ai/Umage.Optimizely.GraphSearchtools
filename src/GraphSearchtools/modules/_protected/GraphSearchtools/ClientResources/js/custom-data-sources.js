/**
 * Graph Search Tools — Custom Data Sources.
 *
 * Lists non-CMS data sources registered in Optimizely Graph and lets ops
 * trigger a full resync per source. Source registration happens out-of-band
 * (custom CMS connectors / PIM pipelines), so this view is read + nudge
 * only — no create/delete in v1.
 */
(function () {
    'use strict';

    var GST = window.GST = window.GST || {};
    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.customDataSources) || {};

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var grid = document.getElementById('cds-grid');

    // ── Utilities ────────────────────────────────────────────────

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
                    var msg = STRINGS.load_failed || 'Request failed';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            // 202 Accepted has no body; 204 either.
            if (resp.status === 204 || resp.status === 202) return null;
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

    function fmtDate(value) {
        if (!value) return '—';
        try {
            var d = new Date(value);
            if (isNaN(d.getTime())) return '—';
            return d.toLocaleString();
        } catch (_) { return '—'; }
    }

    function fmtItems(value) {
        if (value == null) return '—';
        try { return Number(value).toLocaleString(); } catch (_) { return String(value); }
    }

    /**
     * Maps Graph's source status string to a (badge variant, label) pair.
     * Strings come from STRINGS so the labels translate, but the variant
     * choice stays here so it tracks the colour vocabulary used elsewhere
     * in the addon (Webhooks status badge, Health KPIs).
     */
    function statusBadge(status) {
        var key = (status || '').toLowerCase();
        var variant = 'default';
        var label = STRINGS.status_unknown || 'Unknown';
        if (key === 'healthy' || key === 'ok' || key === 'active' || key === 'completed' || key === 'idle') {
            variant = 'success';
            label = STRINGS.status_healthy || 'Healthy';
        } else if (key === 'syncing' || key === 'running' || key === 'inprogress' || key === 'in_progress' || key === 'in-progress' || key === 'pending') {
            variant = 'warning';
            label = STRINGS.status_syncing || 'Syncing';
        } else if (key === 'stale' || key === 'outdated' || key === 'expired') {
            variant = 'warning';
            label = STRINGS.status_stale || 'Stale';
        } else if (key === 'failed' || key === 'error' || key === 'errored') {
            variant = 'danger';
            label = STRINGS.status_failed || 'Failed';
        }
        return '<span class="gst-badge gst-badge--' + variant + '" title="' + escHtml(status || '') + '">' + escHtml(label) + '</span>';
    }

    // ── Render ───────────────────────────────────────────────────

    function renderGrid(rows) {
        grid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="6" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No data sources registered.') + '</p></td>';
            grid.appendChild(emptyTr);
            return;
        }
        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            tr.innerHTML =
                '<td><code>' + escHtml(row.name || '') + '</code></td>' +
                '<td>' + escHtml(row.type || '—') + '</td>' +
                '<td>' + escHtml(fmtItems(row.itemCount)) + '</td>' +
                '<td>' + escHtml(fmtDate(row.lastSyncedAt)) + '</td>' +
                '<td>' + statusBadge(row.status) + '</td>' +
                '<td class="gst-cds-actions-col"><button type="button" class="gst-btn gst-btn--sm" data-action="sync" data-name="' + escHtml(row.name) + '">' + escHtml(STRINGS.sync || 'Sync now') + '</button></td>';

            tr.querySelector('[data-action="sync"]').addEventListener('click', function (e) {
                triggerSync(row, e.currentTarget);
            });
            grid.appendChild(tr);
        });
    }

    function load() {
        setAlert('');
        ajax(BASE + '/CustomDataSourcesApi/List')
            .then(renderGrid)
            .catch(function (err) {
                setAlert((STRINGS.load_failed || 'Could not load data sources.') + ' ' + err.message, true);
            });
    }

    // ── Sync ─────────────────────────────────────────────────────

    function triggerSync(row, button) {
        var msg = (STRINGS.sync_confirm || 'Trigger a full resync of \'%1\'?').replace('%1', row.name);
        if (!window.confirm(msg)) return;
        if (button) button.disabled = true;
        ajax(BASE + '/CustomDataSourcesApi/Sync?name=' + encodeURIComponent(row.name), { method: 'POST' })
            .then(function () {
                setAlert((STRINGS.sync_started || 'Sync started for \'%1\'.').replace('%1', row.name), false);
                // Give Graph a beat to flip the status, then refresh.
                setTimeout(load, 750);
            })
            .catch(function (err) {
                if (button) button.disabled = false;
                setAlert((STRINGS.sync_failed || 'Could not trigger sync.') + ' ' + err.message, true);
            });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    GST.customDataSources = {
        load: load,
        triggerSync: triggerSync
    };

    load();
})();
