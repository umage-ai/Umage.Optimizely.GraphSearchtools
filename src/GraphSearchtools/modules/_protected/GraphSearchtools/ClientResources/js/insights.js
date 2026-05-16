/**
 * Insights dashboard — global view. Reads four endpoints:
 *
 *   GET InsightsApi/SearchKpis                     → kpis_title panel (always 30d)
 *   GET InsightsApi/TopPhrases?days=7|30           → top_title panel
 *   GET InsightsApi/ZeroResultPhrases?days=7|30    → zero_title panel
 *   GET InsightsApi/RecentActivity?take=20         → activity_title panel
 *
 * The 7d / 30d pill drives the top / zero panels; the KPI panel always reads
 * 30d (the sparkline needs the resolution); the activity panel ignores days
 * entirely (always newest-N).
 *
 * Per-profile mode is handled by the Profile › Insights tab, which reuses
 * the same endpoints with profileKey + locale set. See profile-insights.js
 * for that wrapping; this file is the standalone global surface.
 */
(function () {
    const API = window.GST_BASE_URL + '/InsightsApi';
    const STRINGS = (window.GST_STRINGS && window.GST_STRINGS.insights) || {};

    let days = 7;

    document.addEventListener('DOMContentLoaded', init);

    function init() {
        document.querySelectorAll('.gst-sl-pill[data-days]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                document.querySelectorAll('.gst-sl-pill[data-days]')
                    .forEach(function (b) { b.classList.remove('gst-sl-pill--active'); });
                btn.classList.add('gst-sl-pill--active');
                days = parseInt(btn.getAttribute('data-days'), 10) || 7;
                reloadWindowed();
            });
        });
        document.getElementById('gst-insights-refresh')
            .addEventListener('click', reloadAll);
        reloadAll();
    }

    function reloadAll() {
        reloadKpis();
        reloadWindowed();
        reloadActivity();
    }

    function reloadWindowed() {
        renderLoading('gst-insights-top', 5);
        renderLoading('gst-insights-zero', 4);
        GST.fetchJson(API + '/TopPhrases?days=' + days)
            .then(renderTop)
            .catch(function () { renderError('gst-insights-top', 5); });
        GST.fetchJson(API + '/ZeroResultPhrases?days=' + days)
            .then(renderZero)
            .catch(function () { renderError('gst-insights-zero', 4); });
    }

    function reloadKpis() {
        renderKpisLoading();
        GST.fetchJson(API + '/SearchKpis')
            .then(renderKpis)
            .catch(renderKpisError);
    }

    function reloadActivity() {
        renderLoading('gst-insights-activity', 6);
        GST.fetchJson(API + '/RecentActivity?take=20')
            .then(renderActivity)
            .catch(function () { renderError('gst-insights-activity', 6); });
    }

    // ── Renderers ──────────────────────────────────────────────────────

    function renderTop(rows) {
        const tbody = document.getElementById('gst-insights-top');
        if (!rows || rows.length === 0) {
            tbody.innerHTML = emptyRow(5, STRINGS.empty_phrases || 'No phrases logged in this window yet.');
            return;
        }
        tbody.innerHTML = rows.map(function (r) {
            return '<tr>' +
                '<td>' + esc(r.phrase) + '</td>' +
                '<td class="num">' + (r.count || 0) + '</td>' +
                '<td class="num">' + (r.zeroResults || 0) + '</td>' +
                '<td>' + esc(r.locale || '') + '</td>' +
                '<td>' + esc(r.profileKey || '') + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderZero(rows) {
        const tbody = document.getElementById('gst-insights-zero');
        if (!rows || rows.length === 0) {
            tbody.innerHTML = emptyRow(4, STRINGS.empty_zero || 'No zero-result phrases — every search found something.');
            return;
        }
        tbody.innerHTML = rows.map(function (r) {
            return '<tr>' +
                '<td>' + esc(r.phrase) + '</td>' +
                '<td class="num">' + (r.count || 0) + '</td>' +
                '<td>' + esc(r.locale || '') + '</td>' +
                '<td>' + esc(r.profileKey || '') + '</td>' +
                '</tr>';
        }).join('');
    }

    function renderKpis(k) {
        GST.renderKpiCard('gst-insights-kpis', k);
    }

    function renderActivity(rows) {
        const tbody = document.getElementById('gst-insights-activity');
        if (!rows || rows.length === 0) {
            tbody.innerHTML = emptyRow(6, STRINGS.empty_activity || 'No edits recorded yet.');
            return;
        }
        tbody.innerHTML = rows.map(function (r) {
            return '<tr>' +
                '<td>' + esc(formatWhen(r.at)) + '</td>' +
                '<td>' + esc(r.profileKey || '') + '</td>' +
                '<td>' + esc(r.kind || '') + '</td>' +
                '<td>' + esc(r.action || '') + '</td>' +
                '<td>' + esc(r.subject || '') + '</td>' +
                '<td>' + esc(r.actorName || '') + '</td>' +
                '</tr>';
        }).join('');
    }

    // ── Helpers ────────────────────────────────────────────────────────

    function renderLoading(id, cols) {
        const tbody = document.getElementById(id);
        if (!tbody) return;
        const msg = (window.GST_STRINGS && window.GST_STRINGS.shared && window.GST_STRINGS.shared.loading) || 'Loading...';
        tbody.innerHTML = '<tr><td colspan="' + cols + '" class="gst-muted">' + esc(msg) + '</td></tr>';
    }

    function renderKpisLoading() {
        GST.renderKpiCardLoading('gst-insights-kpis');
    }

    function renderError(id, cols) {
        const tbody = document.getElementById(id);
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="' + cols + '" class="gst-empty"><p>' +
            esc(STRINGS.load_failed || 'Could not load insights.') + '</p></td></tr>';
    }

    function renderKpisError() {
        GST.renderKpiCardError('gst-insights-kpis');
    }

    function emptyRow(cols, message) {
        return '<tr><td colspan="' + cols + '" class="gst-empty"><p>' + esc(message) + '</p></td></tr>';
    }

    function formatWhen(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        if (isNaN(d.getTime())) return iso;
        // Local-time, compact: yyyy-mm-dd hh:mm.
        const pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
               ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function esc(s) {
        if (s == null) return '';
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }
})();
