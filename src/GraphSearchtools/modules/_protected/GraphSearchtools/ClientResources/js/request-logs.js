/**
 * Graph Search Tools — Request Logs.
 *
 * Renders the most recent Graph queries with timing + ranking + result count.
 * All filtering (time window, errors-only, op-name search) is client-side
 * against an in-memory page; v1 deliberately does no server pagination so
 * the editor can flip filters without re-hitting the gateway. Click a row to
 * expand it inline and reveal the full GraphQL document + variables, with
 * a copy-to-clipboard button on the query.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.requestLogs) || {};

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var grid = document.getElementById('rl-grid');
    var refreshBtn = document.getElementById('rl-refresh');
    var onlyErrors = document.getElementById('rl-only-errors');
    var searchOp = document.getElementById('rl-search-op');
    var pillGroup = document.querySelector('.gst-rl-pillgroup');

    // ── State ────────────────────────────────────────────────────
    var allRows = [];          // last-fetched server response, unfiltered
    var expandedId = null;     // single open expansion at a time
    var currentWindow = '24h'; // matches the default-active pill

    // ── Utilities ────────────────────────────────────────────────

    function ajax(url) {
        return fetch(url, {
            method: 'GET',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.load_failed || 'Request failed';
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

    /**
     * Relative-time formatter — picks a unit lazily and renders without using
     * Intl.RelativeTimeFormat so old Edge installs keep working. We don't
     * stretch past "days" because the chosen window pills cap at 7d anyway.
     */
    function relTime(value) {
        if (!value) return '—';
        var d = new Date(value);
        if (isNaN(d.getTime())) return '—';
        var s = (Date.now() - d.getTime()) / 1000;
        if (s < 5) return 'just now';
        if (s < 60) return Math.floor(s) + 's ago';
        if (s < 3600) return Math.floor(s / 60) + 'm ago';
        if (s < 86400) return Math.floor(s / 3600) + 'h ago';
        return Math.floor(s / 86400) + 'd ago';
    }

    /**
     * Status badge class — keeps the visual code in lockstep with the
     * `only errors` filter (anything ≥ 400 lights up red).
     */
    function statusClass(status) {
        if (!status) return 'gst-badge--default';
        if (status >= 500) return 'gst-badge--danger';
        if (status >= 400) return 'gst-badge--warning';
        if (status >= 200 && status < 300) return 'gst-badge--success';
        return 'gst-badge--default';
    }

    function isError(row) {
        return (row.status || 0) >= 400;
    }

    // ── Filtering ────────────────────────────────────────────────

    /**
     * Cut-off in ms-since-epoch derived from the active pill. Returning 0
     * means "no cut-off" — used by the "all" pill. Keeping the math
     * monotone (vs. subtracting a Date) makes this trivially testable.
     */
    function windowCutoff() {
        var now = Date.now();
        switch (currentWindow) {
            case '1h':  return now - 60 * 60 * 1000;
            case '24h': return now - 24 * 60 * 60 * 1000;
            case '7d':  return now - 7 * 24 * 60 * 60 * 1000;
            case 'all':
            default:    return 0;
        }
    }

    function filteredRows() {
        var cutoff = windowCutoff();
        var errorOnly = !!onlyErrors.checked;
        var search = (searchOp.value || '').trim().toLowerCase();

        return allRows.filter(function (r) {
            if (cutoff > 0) {
                var t = new Date(r.at).getTime();
                if (isNaN(t) || t < cutoff) return false;
            }
            if (errorOnly && !isError(r)) return false;
            if (search) {
                var op = (r.operation || '').toLowerCase();
                if (op.indexOf(search) === -1) return false;
            }
            return true;
        });
    }

    // ── Render ───────────────────────────────────────────────────

    function copyToClipboard(text, btn) {
        var done = function () {
            var prev = btn.textContent;
            btn.textContent = STRINGS.copied || 'Copied';
            setTimeout(function () { btn.textContent = prev; }, 1200);
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done).catch(function () { fallback(); });
        } else {
            fallback();
        }
        function fallback() {
            // textarea-and-execCommand: still the only path that survives in
            // CMS shell iframes without `clipboard-write` permissions.
            var ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'absolute';
            ta.style.left = '-9999px';
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand('copy'); done(); } catch (_) {}
            document.body.removeChild(ta);
        }
    }

    function renderExpansion(row) {
        var tr = document.createElement('tr');
        tr.className = 'gst-rl-expansion';
        var td = document.createElement('td');
        td.colSpan = 8;

        var queryText = row.query || '';
        var variablesText = row.variables || '';
        try {
            // Variables come either as a JSON string or as an already-stringified
            // object — pretty-print whichever we got, but only if it parses.
            if (variablesText) variablesText = JSON.stringify(JSON.parse(variablesText), null, 2);
        } catch (_) { /* leave as-is */ }

        td.innerHTML =
            '<div class="gst-rl-expansion__inner">' +
                '<div class="gst-rl-section">' +
                    '<div class="gst-rl-section__head">' +
                        '<span>Query</span>' +
                        '<button type="button" class="gst-btn gst-btn--sm gst-rl-copy">' + escHtml(STRINGS.copy_query || 'Copy query') + '</button>' +
                    '</div>' +
                    '<pre class="gst-mono gst-rl-pre" data-role="query">' + escHtml(queryText) + '</pre>' +
                '</div>' +
                (variablesText ? (
                    '<div class="gst-rl-section">' +
                        '<div class="gst-rl-section__head"><span>Variables</span></div>' +
                        '<pre class="gst-mono gst-rl-pre">' + escHtml(variablesText) + '</pre>' +
                    '</div>'
                ) : '') +
                (row.userAgent ? (
                    '<div class="gst-rl-meta"><span>User-Agent:</span> <code>' + escHtml(row.userAgent) + '</code></div>'
                ) : '') +
            '</div>';

        var copyBtn = td.querySelector('.gst-rl-copy');
        if (copyBtn) {
            copyBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                copyToClipboard(queryText, copyBtn);
            });
        }
        tr.appendChild(td);
        return tr;
    }

    function renderGrid() {
        grid.innerHTML = '';
        var rows = filteredRows();
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="8" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No request logs in this window.') + '</p></td>';
            grid.appendChild(emptyTr);
            return;
        }

        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            tr.className = 'gst-rl-row';
            if (expandedId === row.id) tr.classList.add('gst-rl-row--expanded');

            tr.innerHTML =
                '<td title="' + escHtml(row.at) + '">' + escHtml(relTime(row.at)) + '</td>' +
                '<td>' + (row.operation ? '<code>' + escHtml(row.operation) + '</code>' : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td><span class="gst-badge ' + statusClass(row.status) + '">' + escHtml(row.status || '—') + '</span></td>' +
                '<td class="num">' + escHtml(row.durationMs != null ? row.durationMs + ' ms' : '—') + '</td>' +
                '<td class="num">' + escHtml(row.resultCount != null ? row.resultCount : '—') + '</td>' +
                '<td>' + (row.ranking ? '<span class="gst-badge gst-badge--default">' + escHtml(row.ranking) + '</span>' : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td>' + escHtml(row.callerIp || '—') + '</td>' +
                '<td class="gst-rl-chevron-col"><span class="gst-rl-chevron" aria-hidden="true">' + (expandedId === row.id ? '▾' : '▸') + '</span></td>';

            tr.addEventListener('click', function () {
                expandedId = (expandedId === row.id) ? null : row.id;
                renderGrid();
            });
            grid.appendChild(tr);

            if (expandedId === row.id) {
                grid.appendChild(renderExpansion(row));
            }
        });
    }

    function load() {
        setAlert('');
        ajax(BASE + '/RequestLogsApi/List?take=200')
            .then(function (rows) {
                allRows = Array.isArray(rows) ? rows : [];
                renderGrid();
            })
            .catch(function (err) {
                allRows = [];
                renderGrid();
                setAlert((STRINGS.load_failed || 'Could not load request logs.') + ' ' + err.message, true);
            });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    pillGroup.addEventListener('click', function (e) {
        var btn = e.target.closest('.gst-rl-pill');
        if (!btn) return;
        var w = btn.getAttribute('data-window');
        if (!w) return;
        currentWindow = w;
        var pills = pillGroup.querySelectorAll('.gst-rl-pill');
        for (var i = 0; i < pills.length; i++) {
            pills[i].classList.toggle('gst-rl-pill--active', pills[i] === btn);
        }
        renderGrid();
    });

    onlyErrors.addEventListener('change', renderGrid);

    var searchTimer = 0;
    searchOp.addEventListener('input', function () {
        // Debounce the input handler so typing in a long op-name doesn't
        // re-render dozens of times — the in-memory filter is cheap, but
        // every render rebuilds the DOM.
        if (searchTimer) clearTimeout(searchTimer);
        searchTimer = setTimeout(renderGrid, 80);
    });

    refreshBtn.addEventListener('click', load);

    load();
})();
