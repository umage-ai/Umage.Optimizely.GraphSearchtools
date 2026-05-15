/**
 * Graph Search Tools — Search Logs UI.
 *
 * Phase 4 Wave 5 analytics surface. Renders four cards driven off the same
 * time-window pill: top phrases, zero-result phrases, low-CTR phrases, and a
 * live-tail of recent raw events. The phrase-level cards include deep-links
 * to the synonym-mining surface (Synonyms tool) and pinned tuning surface
 * (Profiles tool) so editors can act on what they see.
 *
 * The "live tail" feel is approximate — we re-poll every 30s while the page is
 * visible, with no SSE/websocket. That keeps the contract narrow (just the
 * read-only API) and avoids a foreground-tab cost when nobody is watching.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.searchLogs) || {};

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var refreshBtn = document.getElementById('sl-refresh');
    var pillGroup = document.querySelector('.gst-sl-pillgroup');
    var emptyCard = document.getElementById('sl-empty');

    var grids = {
        top: document.getElementById('sl-top'),
        zero: document.getElementById('sl-zero'),
        lowctr: document.getElementById('sl-lowctr'),
        raw: document.getElementById('sl-raw')
    };

    // ── State ────────────────────────────────────────────────────
    var currentWindow = '24h';
    var rawTimer = 0;
    var RAW_POLL_MS = 30000;
    // Keep the last total-row counts so we can decide when to surface the empty
    // state (no telemetry at all anywhere) vs. just an empty card (e.g. no
    // zero-result phrases this window — which is good news).
    var lastTotals = { top: 0, zero: 0, lowctr: 0, raw: 0 };

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
     * Pick a since-cutoff that matches the active window pill. Server clamps
     * future timestamps to "now" so a fast-clicking editor can't request an
     * empty future window.
     */
    function windowSince() {
        var now = Date.now();
        var ms;
        switch (currentWindow) {
            case '1h':  ms = 60 * 60 * 1000; break;
            case '24h': ms = 24 * 60 * 60 * 1000; break;
            case '7d':  ms = 7 * 24 * 60 * 60 * 1000; break;
            case '30d': ms = 30 * 24 * 60 * 60 * 1000; break;
            default:    ms = 24 * 60 * 60 * 1000;
        }
        return new Date(now - ms).toISOString();
    }

    function relTime(value) {
        if (!value) return '—';
        var d = new Date(value);
        if (isNaN(d.getTime())) return '—';
        var s = (Date.now() - d.getTime()) / 1000;
        if (s < 5) return STRINGS.just_now || 'just now';
        if (s < 60) return Math.floor(s) + 's ago';
        if (s < 3600) return Math.floor(s / 60) + 'm ago';
        if (s < 86400) return Math.floor(s / 3600) + 'h ago';
        return Math.floor(s / 86400) + 'd ago';
    }

    function fmtPct(n) {
        if (typeof n !== 'number' || isNaN(n)) return '—';
        return Math.round(n * 100) + '%';
    }

    function statusBadge(row) {
        // The aggregate-first ingest models search and click as two distinct
        // events, not one merged row, so the badge maps cleanly off `kind`.
        if (row.kind === 'click') {
            if (row.clickRank && row.clickRank >= 1 && row.clickRank <= 3) {
                return '<span class="gst-badge gst-badge--success">' + escHtml(STRINGS.status_clicked || 'click') + '</span>';
            }
            return '<span class="gst-badge gst-badge--default">' + escHtml(STRINGS.status_no_click || 'click rank ' + (row.clickRank || '—')) + '</span>';
        }
        if (row.resultCount === 0) {
            return '<span class="gst-badge gst-badge--warning">' + escHtml(STRINGS.status_zero || '0 hits') + '</span>';
        }
        return '<span class="gst-badge gst-badge--default">' + escHtml(STRINGS.status_search || 'search') + '</span>';
    }

    // ── Render helpers ───────────────────────────────────────────

    function renderEmptyRow(tbody, colspan, message) {
        tbody.innerHTML = '<tr><td colspan="' + colspan + '" class="gst-empty"><p>' + escHtml(message) + '</p></td></tr>';
    }

    function renderTop(rows) {
        if (!rows || rows.length === 0) {
            renderEmptyRow(grids.top, 4, STRINGS.no_top || STRINGS.empty_card || 'No data in this window.');
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            html += '<tr>' +
                '<td><code>' + escHtml(r.phrase) + '</code></td>' +
                '<td class="num">' + escHtml(r.hits) + '</td>' +
                '<td>' + (r.locale ? escHtml(r.locale) : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td>' + (r.profileKey ? '<code>' + escHtml(r.profileKey) + '</code>' : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '</tr>';
        }
        grids.top.innerHTML = html;
    }

    function renderZero(rows) {
        if (!rows || rows.length === 0) {
            renderEmptyRow(grids.zero, 4, STRINGS.no_zero || STRINGS.empty_card || 'No zero-result phrases — nice.');
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            // Deep-link to Synonyms with the phrase pre-filled — the Synonyms
            // tool ignores the query-param today, but the link is the
            // affordance editors expect ("act on this row").
            var synUrl = '/EPiServer/cms/graphsearchtools/synonyms?phrase=' + encodeURIComponent(r.phrase);
            html += '<tr>' +
                '<td><code>' + escHtml(r.phrase) + '</code></td>' +
                '<td class="num">' + escHtml(r.hits) + '</td>' +
                '<td>' + (r.locale ? escHtml(r.locale) : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td class="gst-sl-action-col"><a class="gst-sl-link" href="' + escHtml(synUrl) + '">' + escHtml(STRINGS.add_as_synonym || 'add as synonym?') + '</a></td>' +
                '</tr>';
        }
        grids.zero.innerHTML = html;
    }

    function renderLowCtr(rows) {
        if (!rows || rows.length === 0) {
            renderEmptyRow(grids.lowctr, 4, STRINGS.no_lowctr || STRINGS.empty_card || 'No low-CTR phrases — pinning is working.');
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            // Deep-link to the per-profile detail page when we have a key,
            // otherwise drop the editor on the index. The detail surface is
            // served as `?key=...` on the index URL so the CMS shell can
            // resolve the section's product-id from the registered menu URL.
            var profUrl = '/EPiServer/cms/graphsearchtools/profiles' +
                (r.profileKey ? ('?key=' + encodeURIComponent(r.profileKey)) : '');
            html += '<tr>' +
                '<td><code>' + escHtml(r.phrase) + '</code></td>' +
                '<td class="num" title="CTR ' + escHtml(fmtPct(r.ctr)) + '">' + escHtml(r.hits) + '</td>' +
                '<td>' + (r.profileKey ? '<code>' + escHtml(r.profileKey) + '</code>' : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td class="gst-sl-action-col"><a class="gst-sl-link" href="' + escHtml(profUrl) + '">' + escHtml(STRINGS.tune_pinned || 'tune pinned?') + '</a></td>' +
                '</tr>';
        }
        grids.lowctr.innerHTML = html;
    }

    function renderRaw(rows) {
        if (!rows || rows.length === 0) {
            renderEmptyRow(grids.raw, 6, STRINGS.no_raw || STRINGS.empty_card || 'No recent events in this window.');
            return;
        }
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            // Per-event detail: result count for searches, click rank for clicks.
            var detail = r.kind === 'click'
                ? (r.clickRank ? '#' + r.clickRank : '—')
                : (r.resultCount != null ? r.resultCount + ' hits' : '—');
            html += '<tr>' +
                '<td title="' + escHtml(r.at) + '">' + escHtml(relTime(r.at)) + '</td>' +
                '<td><code>' + escHtml(r.phrase || '—') + '</code></td>' +
                '<td>' + (r.locale ? escHtml(r.locale) : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td>' + (r.profileKey ? '<code>' + escHtml(r.profileKey) + '</code>' : '<span class="gst-rl-dim">—</span>') + '</td>' +
                '<td class="num">' + escHtml(detail) + '</td>' +
                '<td>' + statusBadge(r) + '</td>' +
                '</tr>';
        }
        grids.raw.innerHTML = html;
    }

    // ── Fetch ────────────────────────────────────────────────────

    function buildUrl(action, since, take) {
        var url = BASE + '/SearchLogsApi/' + action + '?since=' + encodeURIComponent(since);
        if (take) url += '&take=' + encodeURIComponent(take);
        return url;
    }

    /**
     * Empty-state policy: surface the "no telemetry yet" card only when every
     * card returned zero rows. A zero-result-phrases card with zero rows on
     * a healthy site is *good news*, not an empty state.
     */
    function refreshEmptyState() {
        var noneAtAll = !lastTotals.top && !lastTotals.zero && !lastTotals.lowctr && !lastTotals.raw;
        emptyCard.hidden = !noneAtAll;
    }

    function loadAll() {
        setAlert('');
        var since = windowSince();

        var top = ajax(buildUrl('Top', since, 10))
            .then(function (rows) { rows = rows || []; lastTotals.top = rows.length; renderTop(rows); })
            .catch(function (err) { lastTotals.top = 0; renderEmptyRow(grids.top, 4, err.message); throw err; });

        var zero = ajax(buildUrl('ZeroResults', since, 10))
            .then(function (rows) { rows = rows || []; lastTotals.zero = rows.length; renderZero(rows); })
            .catch(function (err) { lastTotals.zero = 0; renderEmptyRow(grids.zero, 4, err.message); throw err; });

        var lowctr = ajax(buildUrl('LowCtr', since, 10))
            .then(function (rows) { rows = rows || []; lastTotals.lowctr = rows.length; renderLowCtr(rows); })
            .catch(function (err) { lastTotals.lowctr = 0; renderEmptyRow(grids.lowctr, 4, err.message); throw err; });

        var raw = ajax(buildUrl('Raw', since, 50))
            .then(function (rows) { rows = rows || []; lastTotals.raw = rows.length; renderRaw(rows); })
            .catch(function (err) { lastTotals.raw = 0; renderEmptyRow(grids.raw, 7, err.message); throw err; });

        Promise.all([top, zero, lowctr, raw].map(function (p) { return p.catch(function () {}); }))
            .then(refreshEmptyState);

        Promise.all([top, zero, lowctr, raw]).catch(function (err) {
            setAlert((STRINGS.load_failed || 'Could not load search logs.') + ' ' + err.message, true);
        });
    }

    /**
     * Light poll: only the Raw card updates between full refreshes — the
     * three aggregate cards aren't worth re-paying-for on a 30s tick.
     */
    function pollRaw() {
        var since = windowSince();
        ajax(buildUrl('Raw', since, 50))
            .then(function (rows) {
                rows = rows || [];
                lastTotals.raw = rows.length;
                renderRaw(rows);
                refreshEmptyState();
            })
            .catch(function () { /* swallow — the next loadAll() will surface the error */ });
    }

    function startRawPolling() {
        if (rawTimer) clearInterval(rawTimer);
        rawTimer = setInterval(function () {
            // Pause polling when the tab is hidden so we don't waste server
            // round-trips on a backgrounded shell.
            if (document.visibilityState === 'hidden') return;
            pollRaw();
        }, RAW_POLL_MS);
    }

    // ── Wire-up ──────────────────────────────────────────────────

    pillGroup.addEventListener('click', function (e) {
        var btn = e.target.closest('.gst-sl-pill');
        if (!btn) return;
        var w = btn.getAttribute('data-window');
        if (!w || w === currentWindow) return;
        currentWindow = w;
        var pills = pillGroup.querySelectorAll('.gst-sl-pill');
        for (var i = 0; i < pills.length; i++) {
            pills[i].classList.toggle('gst-sl-pill--active', pills[i] === btn);
        }
        loadAll();
    });

    refreshBtn.addEventListener('click', loadAll);

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') pollRaw();
    });

    loadAll();
    startRawPolling();
})();
