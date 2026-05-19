/**
 * Graph Search Tools — Changelog tab module.
 *
 * Shared renderer for the global audit-log feed surfaced under the Pinned
 * and Synonyms tools. Both call GST.changelog.mount({...}) on page boot
 * with their own kind filter + column set; the module handles the fetch,
 * tab-activation deferred load, and refresh wiring.
 *
 * Backed by GET /AuditLogApi/Recent?take=200&kind=…
 */
(function () {
    'use strict';

    var API = (window.GST_BASE_URL || '') + '/AuditLogApi';
    var KNOWN_COLUMNS = ['when', 'who', 'kind', 'action', 'subject', 'channel', 'locale', 'slot', 'collection'];

    function s(path, fallback) { return GST.s(path, fallback); }
    function escHtml(v) { return GST.escHtml(v); }

    /** "2 hrs ago", "—", etc. Mirrors channels.js's relativeTime so the
     *  cadence reads the same across surfaces. */
    function relativeTime(iso) {
        if (!iso) return '—';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return '—';
        var diff = (Date.now() - d.getTime()) / 1000;
        if (diff < 60)     return s('shared.time.justNow', 'just now');
        if (diff < 3600)   return Math.floor(diff / 60) + ' ' + s('shared.time.minutesAgo', 'min ago');
        if (diff < 86400)  return Math.floor(diff / 3600) + ' ' + s('shared.time.hoursAgo', 'hrs ago');
        return Math.floor(diff / 86400) + ' ' + s('shared.time.daysAgo', 'days ago');
    }

    /**
     * Build the cells for one row in the order specified by opts.columns.
     * Unknown column names are tolerated (rendered as empty TD) so a typo
     * in the bootstrap doesn't blow up the row layout.
     */
    function buildCell(col, row) {
        switch (col) {
            case 'when':       return relativeTime(row.at);
            case 'who':        return row.actorName || row.actorId || '—';
            case 'kind':       return row.kind || '—';
            case 'action':     return row.action || '—';
            case 'subject':    return row.subject || '—';
            case 'channel':    return row.channelKey || '—';
            case 'locale':     return row.locale || row.slot ? (row.locale || s('shared.locale.global', 'Global')) : '—';
            case 'slot':       return row.slot || '—';
            case 'collection': return row.collectionKey || '—';
            default:           return '';
        }
    }

    function renderEmpty(host, columns) {
        host.innerHTML = '<tr><td colspan="' + columns.length + '" class="gst-empty"><p>' +
            escHtml(s('changelog.empty', 'No changes recorded yet.')) + '</p></td></tr>';
    }

    function renderLoading(host, columns) {
        host.innerHTML = '<tr><td colspan="' + columns.length + '"><p class="gst-muted">' +
            escHtml(s('shared.loading', 'Loading…')) + '</p></td></tr>';
    }

    function renderError(host, columns) {
        host.innerHTML = '<tr><td colspan="' + columns.length + '"><p class="gst-muted">' +
            escHtml(s('changelog.load_failed', 'Could not load changelog.')) + '</p></td></tr>';
    }

    /**
     * mount({ rowsHost, refreshBtn, tabBtn, kinds, columns }) — defers the
     * first fetch until the tab is activated so the page-load cost is paid
     * lazily, and rebinds the same handler to the explicit refresh button.
     * Safe to call when tabBtn is omitted (e.g. if the tab is the default).
     */
    function mount(opts) {
        opts = opts || {};
        var host = typeof opts.rowsHost === 'string' ? document.querySelector(opts.rowsHost) : opts.rowsHost;
        if (!host) return;
        var refreshBtn = typeof opts.refreshBtn === 'string' ? document.querySelector(opts.refreshBtn) : opts.refreshBtn;
        var tabBtn = typeof opts.tabBtn === 'string' ? document.querySelector(opts.tabBtn) : opts.tabBtn;
        var columns = Array.isArray(opts.columns) && opts.columns.length
            ? opts.columns.filter(function (c) { return KNOWN_COLUMNS.indexOf(c) >= 0; })
            : ['when', 'who', 'kind', 'action', 'subject'];
        var kindParam = opts.kinds ? '&kind=' + encodeURIComponent(opts.kinds) : '';
        var loaded = false;

        function fetchAndRender() {
            renderLoading(host, columns);
            GST.fetchJson(API + '/Recent?take=200' + kindParam).then(function (rows) {
                if (!rows || !rows.length) { renderEmpty(host, columns); return; }
                host.innerHTML = rows.map(function (r) {
                    return '<tr>' + columns.map(function (c) {
                        return '<td>' + escHtml(buildCell(c, r)) + '</td>';
                    }).join('') + '</tr>';
                }).join('');
            }).catch(function (err) {
                console.error('Changelog load failed', err);
                renderError(host, columns);
            });
        }

        function ensureLoaded() {
            if (loaded) return;
            loaded = true;
            fetchAndRender();
        }

        if (tabBtn) {
            tabBtn.addEventListener('click', ensureLoaded);
        } else {
            // No tab gate — load immediately.
            ensureLoaded();
        }
        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                loaded = true;
                fetchAndRender();
            });
        }
    }

    window.GST = window.GST || {};
    window.GST.changelog = { mount: mount };
})();
