/**
 * Graph Search Tools — Webhooks.
 *
 * Lists Optimizely Graph webhooks, lets the editor register new ones, and
 * delete existing ones. Edits are deliberately not exposed: Graph's webhook
 * admin API doesn't support PATCH/PUT, so the workflow is "delete + create"
 * — surfaced honestly via the info banner at the top of the page.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.webhooks) || {};

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var addBtn = document.getElementById('wh-add');
    var grid = document.getElementById('wh-grid');

    var dialog = document.getElementById('wh-dialog');
    var dlgClose = document.getElementById('wh-dialog-close');
    var dlgCancel = document.getElementById('wh-dialog-cancel');
    var dlgSave = document.getElementById('wh-dialog-save');
    var dlgUrl = document.getElementById('wh-dlg-url');
    var dlgMethod = document.getElementById('wh-dlg-method');
    var dlgHeaders = document.getElementById('wh-dlg-headers');
    var dlgHeadersAdd = document.getElementById('wh-dlg-headers-add');
    var dlgFilters = document.getElementById('wh-dlg-filters');
    var dlgFiltersAdd = document.getElementById('wh-dlg-filters-add');

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

    function countOf(obj) {
        if (!obj) return 0;
        if (Array.isArray(obj)) return obj.length;
        if (typeof obj === 'object') return Object.keys(obj).length;
        return 0;
    }

    function tooltipFor(obj) {
        if (!obj) return '';
        try { return JSON.stringify(obj, null, 2); } catch (_) { return String(obj); }
    }

    // ── Render ───────────────────────────────────────────────────

    function renderGrid(rows) {
        grid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="7" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No webhooks registered yet.') + '</p></td>';
            grid.appendChild(emptyTr);
            return;
        }
        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            var headerCount = countOf(row.headers);
            var filterCount = countOf(row.filters);
            var statusBadge = row.disabled
                ? '<span class="gst-badge gst-badge--warning">' + escHtml(STRINGS.status_disabled || 'Disabled') + '</span>'
                : '<span class="gst-badge gst-badge--success">' + escHtml(STRINGS.status_active || 'Active') + '</span>';

            tr.innerHTML =
                '<td><code>' + escHtml(row.url || '') + '</code></td>' +
                '<td>' + escHtml(row.method || 'POST') + '</td>' +
                '<td><span class="gst-badge gst-badge--default" title="' + escHtml(tooltipFor(row.headers)) + '">' + headerCount + '</span></td>' +
                '<td><span class="gst-badge gst-badge--default" title="' + escHtml(tooltipFor(row.filters)) + '">' + filterCount + '</span></td>' +
                '<td>' + statusBadge + '</td>' +
                '<td>' + escHtml(fmtDate(row.createdAt)) + '</td>' +
                '<td class="gst-webhook-actions-col"><button type="button" class="gst-btn gst-btn--sm gst-btn--danger" data-action="delete" data-id="' + escHtml(row.id) + '">' + escHtml(STRINGS.delete || 'Delete') + '</button></td>';

            tr.querySelector('[data-action="delete"]').addEventListener('click', function () {
                deleteWebhook(row);
            });
            grid.appendChild(tr);
        });
    }

    function load() {
        setAlert('');
        ajax(BASE + '/WebhooksApi/List')
            .then(renderGrid)
            .catch(function (err) {
                setAlert((STRINGS.load_failed || 'Could not load webhooks.') + ' ' + err.message, true);
            });
    }

    // ── Delete ───────────────────────────────────────────────────

    function deleteWebhook(row) {
        var msg = STRINGS.delete_confirm || 'Delete this webhook?';
        if (!window.confirm(msg)) return;
        ajax(BASE + '/WebhooksApi/Delete?id=' + encodeURIComponent(row.id), { method: 'DELETE' })
            .then(function () { load(); })
            .catch(function (err) {
                setAlert((STRINGS.delete_failed || 'Could not delete webhook.') + ' ' + err.message, true);
            });
    }

    // ── Create dialog ────────────────────────────────────────────

    function addKvRow(container, key, value) {
        var row = document.createElement('div');
        row.className = 'gst-webhook-kv-row';
        row.innerHTML =
            '<input type="text" class="gst-webhook-kv-key" placeholder="key" />' +
            '<input type="text" class="gst-webhook-kv-value" placeholder="value" />' +
            '<button type="button" class="gst-btn gst-btn--sm gst-webhook-kv-remove" aria-label="Remove">&times;</button>';
        if (key !== undefined) row.querySelector('.gst-webhook-kv-key').value = key;
        if (value !== undefined) row.querySelector('.gst-webhook-kv-value').value = value;
        row.querySelector('.gst-webhook-kv-remove').addEventListener('click', function () {
            row.remove();
        });
        container.appendChild(row);
    }

    function readKvRows(container) {
        var out = {};
        var rows = container.querySelectorAll('.gst-webhook-kv-row');
        for (var i = 0; i < rows.length; i++) {
            var k = (rows[i].querySelector('.gst-webhook-kv-key').value || '').trim();
            var v = (rows[i].querySelector('.gst-webhook-kv-value').value || '').trim();
            if (!k) continue;
            out[k] = v;
        }
        return Object.keys(out).length > 0 ? out : null;
    }

    function openDialog() {
        dlgUrl.value = '';
        dlgMethod.value = 'POST';
        dlgHeaders.innerHTML = '';
        dlgFilters.innerHTML = '';
        dialog.hidden = false;
        setTimeout(function () { dlgUrl.focus(); }, 0);
    }

    function closeDialog() {
        dialog.hidden = true;
    }

    function saveWebhook() {
        var url = (dlgUrl.value || '').trim();
        if (!url || !/^https?:\/\//i.test(url)) {
            setAlert(STRINGS.save_failed || 'Could not save webhook.', true);
            dlgUrl.focus();
            return;
        }
        var payload = {
            url: url,
            method: dlgMethod.value || 'POST',
            headers: readKvRows(dlgHeaders),
            filters: readKvRows(dlgFilters)
        };
        ajax(BASE + '/WebhooksApi/Create', { method: 'POST', body: payload })
            .then(function () {
                closeDialog();
                load();
            })
            .catch(function (err) {
                setAlert((STRINGS.save_failed || 'Could not save webhook.') + ' ' + err.message, true);
            });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    addBtn.addEventListener('click', openDialog);
    dlgClose.addEventListener('click', closeDialog);
    dlgCancel.addEventListener('click', closeDialog);
    dlgSave.addEventListener('click', saveWebhook);
    dlgHeadersAdd.addEventListener('click', function () { addKvRow(dlgHeaders); });
    dlgFiltersAdd.addEventListener('click', function () { addKvRow(dlgFilters); });
    dialog.addEventListener('click', function (e) {
        if (e.target === dialog) closeDialog();
    });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && !dialog.hidden) closeDialog();
    });

    load();
})();
