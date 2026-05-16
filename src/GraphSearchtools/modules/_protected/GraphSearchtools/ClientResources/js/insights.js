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
        const host = document.getElementById('gst-insights-kpis');
        if (!host) return;
        const days = (k && k.windowDays) || 30;

        // Three tiles, each rendered as DOM nodes so we can attach SVG
        // sparklines after the value markup. Innerhtml-then-append would
        // also work but DOM nodes keep IDs stable for testing.
        host.innerHTML = '';
        const tiles = [
            {
                key: 'searches',
                label: STRINGS.kpi_searches || 'Searches',
                value: formatInt((k && k.totalSearches) || 0),
                series: (k && k.sparkSearches) || [],
                fmt: function (v) { return formatInt(v) + ' searches'; }
            },
            {
                key: 'ctr',
                label: STRINGS.kpi_ctr || 'Click-through rate',
                value: formatPct((k && k.ctrPct) || 0),
                series: (k && k.sparkCtr) || [],
                fmt: function (v) { return formatPct(v); },
                // CTR scale is [0,100] regardless of the data — fixed max
                // means a quiet day doesn't look like a 100%-day visually.
                max: 100
            },
            {
                key: 'zero',
                label: STRINGS.kpi_zero || 'Zero-result searches',
                value: formatInt((k && k.totalZero) || 0),
                series: (k && k.sparkZero) || [],
                fmt: function (v) { return formatInt(v) + ' zero-result'; }
            }
        ];

        tiles.forEach(function (t) {
            const tile = document.createElement('div');
            tile.className = 'gst-kpi';
            tile.setAttribute('data-kpi', t.key);
            tile.innerHTML =
                '<div class="gst-kpi__label">' + esc(t.label) +
                ' <span class="gst-muted">(' + esc(formatTemplate(STRINGS.kpi_window, days, 'last %1 days')) + ')</span></div>' +
                '<div class="gst-kpi__value">' + esc(t.value) + '</div>' +
                '<div class="gst-kpi__spark"></div>';
            host.appendChild(tile);

            const sparkHost = tile.querySelector('.gst-kpi__spark');
            GST.sparkline(sparkHost, t.series, {
                label: t.label,
                max: t.max,
                formatTooltip: function (v, i) {
                    return formatTemplate(STRINGS.kpi_tooltip, [daysAgoLabel(days, i), t.fmt(v)], '%1: %2');
                }
            });
        });
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
        const host = document.getElementById('gst-insights-kpis');
        if (host) {
            const msg = (window.GST_STRINGS && window.GST_STRINGS.shared && window.GST_STRINGS.shared.loading) || 'Loading...';
            // Three placeholder tiles so the layout doesn't reflow when
            // real data arrives.
            host.innerHTML =
                '<div class="gst-kpi"><div class="gst-kpi__label">' + esc(msg) + '</div></div>' +
                '<div class="gst-kpi"><div class="gst-kpi__label">' + esc(msg) + '</div></div>' +
                '<div class="gst-kpi"><div class="gst-kpi__label">' + esc(msg) + '</div></div>';
        }
    }

    function renderError(id, cols) {
        const tbody = document.getElementById(id);
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="' + cols + '" class="gst-empty"><p>' +
            esc(STRINGS.load_failed || 'Could not load insights.') + '</p></td></tr>';
    }

    function renderKpisError() {
        const host = document.getElementById('gst-insights-kpis');
        if (host) {
            host.innerHTML = '<div class="gst-kpi"><div class="gst-kpi__label">' +
                esc(STRINGS.load_failed || 'Could not load insights.') + '</div></div>';
        }
    }

    function formatInt(n) {
        if (n == null || isNaN(n)) return '0';
        // Locale-aware thousands separator. Falls back to the raw number
        // for ancient browsers / non-Intl envs.
        try { return Number(n).toLocaleString(); }
        catch (e) { return String(n); }
    }

    function formatPct(p) {
        if (p == null || isNaN(p)) return '0%';
        // One decimal so a CTR of 12.7 doesn't get rounded to "13%" —
        // the lost precision misreads on a marketer's dashboard.
        return (Math.round(p * 10) / 10).toFixed(1) + '%';
    }

    function daysAgoLabel(windowDays, index) {
        // index 0 is the oldest day in the spark series; index windowDays-1
        // is today. Convert to "today" / "1d ago" / "Nd ago".
        const ago = windowDays - 1 - index;
        if (ago === 0) return (window.GST_STRINGS && GST_STRINGS.shared && GST_STRINGS.shared.today) || 'today';
        return formatTemplate(STRINGS.kpi_days_ago, ago, '%1d ago');
    }

    function emptyRow(cols, message) {
        return '<tr><td colspan="' + cols + '" class="gst-empty"><p>' + esc(message) + '</p></td></tr>';
    }

    function formatTemplate(tmpl, value, fallback) {
        const t = tmpl || fallback || '%1';
        if (Array.isArray(value)) {
            return t.replace(/%(\d+)/g, function (_, n) {
                const idx = parseInt(n, 10) - 1;
                return idx >= 0 && idx < value.length ? String(value[idx]) : '';
            });
        }
        return t.replace('%1', String(value));
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
