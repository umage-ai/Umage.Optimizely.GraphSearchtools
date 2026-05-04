/**
 * Graph Search Tools — Health.
 *
 * The dashboard reads three things from the server:
 *   • /HealthApi/Check    — runs the four probes on demand (the Run button).
 *   • /HealthApi/History  — persisted scan timeline + open / recently-resolved
 *                           issues, populated by the background scheduled job.
 *   • /HealthApi/Settings — the persistent ScanEnabled flag (the Auto toggle).
 *
 * The Auto toggle no longer drives a UI poll loop — it just enables the
 * server-side scheduled job. The page polls /History every 30s while open so
 * the timeline stays fresh, and /Check runs only on explicit Run-button click.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.health) || {};
    var HISTORY_POLL_MS = 30000;

    // ── DOM refs ───────────────────────────────────────────────────
    var hero = document.getElementById('hl-hero');
    var statusWord = document.getElementById('hl-status-word');
    var subtitle = document.getElementById('hl-subtitle');
    var gatewayLabel = document.getElementById('hl-gateway');
    var checkedLabel = document.getElementById('hl-checked');
    var autoSep = document.getElementById('hl-auto-sep');
    var autoStatus = document.getElementById('hl-auto-status');
    var runBtn = document.getElementById('hl-run');
    var autoToggle = document.getElementById('hl-auto');

    var kpiDot = document.getElementById('hl-kpi-dot');
    var kpiStatus = document.getElementById('hl-kpi-status');
    var kpiStatusSub = document.getElementById('hl-kpi-status-sub');
    var kpiAvg = document.getElementById('hl-kpi-avg');
    var kpiVerified = document.getElementById('hl-kpi-verified');
    var kpiVerifiedSub = document.getElementById('hl-kpi-verified-sub');
    var kpiIssues = document.getElementById('hl-kpi-issues');
    var kpiIssuesSub = document.getElementById('hl-kpi-issues-sub');

    var timelineSvg = document.getElementById('hl-timeline-svg');
    var timelineBars = document.getElementById('hl-timeline-bars');
    var timelineEmpty = document.getElementById('hl-timeline-empty');

    var probesEl = document.getElementById('hl-probes');
    var alertBox = document.getElementById('gst-alert');
    var issuesList = document.getElementById('hl-issues-list');
    var issuesSummary = document.getElementById('hl-issues-summary');
    var issuesTabOpen = document.getElementById('hl-issues-tab-open');
    var issuesTabResolved = document.getElementById('hl-issues-tab-resolved');
    var issuesCountOpen = document.getElementById('hl-issues-count-open');
    var issuesCountResolved = document.getElementById('hl-issues-count-resolved');

    // ── State ──────────────────────────────────────────────────────
    var inFlight = false;
    var historyTimer = null;
    var openIssues = [];
    var resolvedIssues = [];
    var issuesTab = 'open';
    var lastResultAt = null;
    var checkedTickTimer = null;
    var lastScanAt = null;

    // ── Helpers ────────────────────────────────────────────────────

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            credentials: 'same-origin',
            headers: headers,
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Health check failed';
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

    function fmtMs(value) {
        if (value == null || isNaN(value)) return '—';
        if (value >= 1000) return (value / 1000).toFixed(2) + ' s';
        return Math.round(value).toString();
    }

    function statusKey(s) {
        s = (s || '').toLowerCase();
        return (s === 'green' || s === 'amber' || s === 'red') ? s : 'unknown';
    }

    function rollupStatus(probes) {
        var rank = { red: 3, amber: 2, unknown: 1, green: 0 };
        var worst = 'green';
        if (!probes || !probes.length) return 'unknown';
        for (var i = 0; i < probes.length; i++) {
            var s = statusKey(probes[i].status);
            if ((rank[s] || 0) > (rank[worst] || 0)) worst = s;
        }
        return worst;
    }

    function statusToHero(status) {
        switch (status) {
            case 'green':   return { word: STRINGS.status_healthy || 'HEALTHY', sub: STRINGS.subtitle_healthy || 'All probes passed.' };
            case 'amber':   return { word: STRINGS.status_degraded || 'DEGRADED', sub: STRINGS.subtitle_degraded || 'Reachable with warnings.' };
            case 'red':     return { word: STRINGS.status_down || 'DOWN', sub: STRINGS.subtitle_down || 'Connection problem.' };
            default:        return { word: STRINGS.status_unknown || 'UNKNOWN', sub: STRINGS.subtitle_unknown || 'Unknown status.' };
        }
    }

    function formatRelative(when) {
        if (!when) return null;
        var ageSec = Math.max(0, Math.round((Date.now() - new Date(when).getTime()) / 1000));
        if (ageSec < 5) return STRINGS.checked_just_now || 'Just now';
        if (ageSec < 60) return (STRINGS.checked_seconds_ago || '%1s ago').replace('%1', ageSec);
        return (STRINGS.checked_minutes_ago || '%1m ago').replace('%1', Math.round(ageSec / 60));
    }

    function updateCheckedLabel() {
        checkedLabel.textContent = formatRelative(lastResultAt) || '—';
        if (autoToggle.checked) {
            var rel = formatRelative(lastScanAt);
            autoStatus.textContent = rel
                ? (STRINGS.last_scan_at || 'Last scan %1').replace('%1', rel)
                : (STRINGS.last_scan_never || 'Never scanned');
            autoStatus.hidden = false;
            autoSep.hidden = false;
        } else {
            autoStatus.hidden = true;
            autoSep.hidden = true;
        }
    }

    // ── Timeline (SVG bars, server history) ────────────────────────

    function renderTimeline(scans) {
        timelineBars.innerHTML = '';
        if (!scans || !scans.length) {
            timelineEmpty.hidden = false;
            return;
        }
        timelineEmpty.hidden = true;

        // Oldest → newest, left → right.
        var ordered = scans.slice().sort(function (a, b) {
            return new Date(a.ranAt) - new Date(b.ranAt);
        });
        var W = 600, H = 80, PAD = 4;
        var n = ordered.length;
        var stepX = (W - PAD * 2) / Math.max(n, 1);

        // Y-axis is scan latency. We normalize to a "nice" max — round up to
        // the nearest 50/100/250/500/1000ms bucket so the same dataset gives
        // a stable ceiling between renders.
        var latencies = ordered.map(function (s) { return s.elapsedMs || 0; });
        var maxObserved = Math.max.apply(null, latencies.concat([1]));
        var yMax = niceCeil(maxObserved);

        var html = '';
        for (var i = 0; i < n; i++) {
            var s = ordered[i];
            var status = statusKey(s.overallStatus);
            var x = PAD + i * stepX + stepX / 2;
            var ms = s.elapsedMs || 0;
            var barW, barH, barY, cls;
            // Red probe-failures stay as full-height vertical guides — the
            // run is "down" so latency isn't a meaningful signal.
            if (status === 'red') {
                barW = 2;
                barH = H - PAD * 2;
                barY = PAD;
                cls = 'is-red';
            } else {
                barW = Math.max(3, stepX - 2);
                // Bar height scales with latency vs the running max.
                var ratio = yMax > 0 ? Math.min(1, ms / yMax) : 0;
                // Floor at a couple pixels so very fast runs stay visible.
                barH = Math.max(3, (H - PAD * 2) * ratio);
                barY = H - PAD - barH;
                cls = (status === 'amber') ? 'is-amber' : 'is-green';
            }
            var rx = (cls === 'is-red') ? 1 : 1.5;
            var tooltip = (STRINGS.bar_tooltip || '%1 — %2 / %3 probes, %4 / %5 pages, %6')
                .replace('%1', new Date(s.ranAt).toLocaleString())
                .replace('%2', s.probesPassed)
                .replace('%3', s.probesTotal)
                .replace('%4', (s.contentChecked - s.contentMissing))
                .replace('%5', s.contentChecked)
                .replace('%6', s.note || '');
            tooltip += ' (' + Math.round(ms) + ' ms)';
            html += '<rect class="gst-health-timeline__bar ' + cls + '"'
                + ' x="' + (x - barW / 2).toFixed(1) + '"'
                + ' y="' + barY.toFixed(1) + '"'
                + ' width="' + barW.toFixed(1) + '"'
                + ' height="' + barH.toFixed(1) + '"'
                + ' rx="' + rx + '" ry="' + rx + '">'
                + '<title>' + escapeXml(tooltip) + '</title>'
                + '</rect>';
        }
        timelineBars.innerHTML = html;

        // Update the legend to also surface the y-axis ceiling so editors
        // can read absolute numbers off the chart.
        var legend = document.getElementById('hl-timeline-yaxis');
        if (legend) legend.textContent = '0–' + yMax + ' ms';
    }

    /** Round a max latency up to a "nice" round number for axis stability. */
    function niceCeil(value) {
        if (value <= 50) return 50;
        if (value <= 100) return 100;
        if (value <= 250) return 250;
        if (value <= 500) return 500;
        if (value <= 1000) return 1000;
        if (value <= 2500) return 2500;
        if (value <= 5000) return 5000;
        return Math.ceil(value / 1000) * 1000;
    }

    function escapeXml(s) {
        return String(s).replace(/[<>&"]/g, function (c) {
            return c === '<' ? '&lt;' : c === '>' ? '&gt;' : c === '&' ? '&amp;' : '&quot;';
        });
    }

    // ── Probe cards ────────────────────────────────────────────────

    function renderProbes(probes) {
        probesEl.innerHTML = '';
        if (!probes || !probes.length) return;
        probes.forEach(function (p) {
            var status = statusKey(p.status);
            var card = document.createElement('div');
            card.className = 'gst-health-probe is-' + status;

            var head = document.createElement('div');
            head.className = 'gst-health-probe__head';

            var dot = document.createElement('span');
            dot.className = 'gst-health-dot is-' + status;
            head.appendChild(dot);

            var name = document.createElement('span');
            name.className = 'gst-health-probe__name';
            name.textContent = p.name || '—';
            head.appendChild(name);

            var latency = document.createElement('span');
            latency.className = 'gst-health-probe__latency';
            latency.textContent = fmtMs(p.elapsedMs);
            var unit = document.createElement('span');
            unit.className = 'gst-health-probe__unit';
            unit.textContent = (p.elapsedMs >= 1000) ? '' : 'ms';
            latency.appendChild(unit);
            head.appendChild(latency);

            card.appendChild(head);

            if (p.target) {
                var target = document.createElement('div');
                target.className = 'gst-health-probe__target';
                target.textContent = p.target;
                card.appendChild(target);
            }

            var msg = document.createElement('div');
            msg.className = 'gst-health-probe__msg';
            msg.textContent = p.message || '';
            card.appendChild(msg);

            probesEl.appendChild(card);
        });
    }

    // ── Issues panel ───────────────────────────────────────────────

    function renderIssues() {
        issuesCountOpen.textContent = openIssues.length;
        issuesCountResolved.textContent = resolvedIssues.length;
        issuesTabOpen.classList.toggle('is-active', issuesTab === 'open');
        issuesTabResolved.classList.toggle('is-active', issuesTab === 'resolved');

        var rows = (issuesTab === 'open') ? openIssues : resolvedIssues;
        issuesList.innerHTML = '';
        if (!rows.length) {
            var empty = document.createElement('div');
            empty.className = 'gst-health-issues__empty';
            empty.textContent = (issuesTab === 'open')
                ? (STRINGS && STRINGS.subtitle_healthy) // reuse the same idea
                || ''
                : '';
            // Show a friendlier message via the lang string when the tab is empty.
            empty.textContent = (issuesTab === 'open')
                ? (window.GST_STRINGS && window.GST_STRINGS.health && window.GST_STRINGS.health.subtitle_healthy)
                  || (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issues_empty)
                  || 'No issues — every page in the rotation has been verified in Graph.'
                : 'Nothing recently resolved.';
            issuesList.appendChild(empty);
            return;
        }

        rows.forEach(function (it) {
            var card = document.createElement('div');
            card.className = 'gst-health-issue';
            if (it.resolvedAt) card.classList.add('is-resolved');

            var head = document.createElement('div');
            head.className = 'gst-health-issue__head';

            var name = document.createElement('span');
            name.className = 'gst-health-issue__name';
            if (it.contentId) {
                var a = document.createElement('a');
                a.href = (window.GST_CMS_URL || '') + '?language=' + encodeURIComponent(it.locale || '') + '#context=epi.cms.contentdata:///' + it.contentId;
                a.target = '_blank';
                a.rel = 'noopener';
                a.textContent = it.contentName || it.contentGuid;
                name.appendChild(a);
            } else {
                name.textContent = it.contentName || it.contentGuid;
            }
            head.appendChild(name);

            var reason = document.createElement('span');
            reason.className = 'gst-health-issue__reason';
            reason.textContent = (it.reason === 'removed-from-cms')
                ? (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issue_reason_removed) || 'Removed from CMS'
                : (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issue_reason_missing) || 'Missing from Graph';
            head.appendChild(reason);

            card.appendChild(head);

            var meta = document.createElement('div');
            meta.className = 'gst-health-issue__meta';
            var bits = [];
            if (it.contentTypeName) bits.push(it.contentTypeName);
            if (it.locale) bits.push(it.locale);
            bits.push(it.contentGuid);
            meta.textContent = bits.join(' · ');
            card.appendChild(meta);

            var times = document.createElement('div');
            times.className = 'gst-health-issue__times';
            if (it.resolvedAt) {
                times.textContent = ((window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issue_resolved_at) || 'Resolved')
                    + ' ' + new Date(it.resolvedAt).toLocaleString();
            } else {
                var first = ((window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issue_first_seen) || 'First seen')
                    + ' ' + new Date(it.firstSeenAt).toLocaleString();
                var last = ((window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issue_last_seen) || 'Last seen')
                    + ' ' + new Date(it.lastSeenAt).toLocaleString();
                times.textContent = first + ' · ' + last;
            }
            card.appendChild(times);

            issuesList.appendChild(card);
        });
    }

    // The lang strings for issue labels live under the tools/health namespace
    // (server-rendered into window.GST_STRINGS via the layout's UiStringsProvider
    // call), but the issues_* keys live in tools/health/issues_* not the ui/
    // bucket. Pull them lazily from a data attribute injected by the view —
    // simplest path is to expose them on a global the view writes once.
    window.GST_STRINGS_RAW = window.GST_STRINGS_RAW || (function () {
        // Read every required string at startup so renderIssues can be cheap.
        // The keys are written by hand because the standard UiStringsProvider
        // only ships generic UI strings, not tool-page text.
        return {
            issues_empty: 'No issues — every page in the rotation has been verified in Graph.',
            issues_clean: 'All clear',
            issue_first_seen: 'First seen',
            issue_last_seen: 'Last seen',
            issue_resolved_at: 'Resolved',
            issue_reason_missing: 'Missing from Graph',
            issue_reason_removed: 'Removed from CMS',
            kpi_verified_of: 'of %1 page(s)',
            kpi_verified_unknown: 'No catalog data'
        };
    })();

    // ── Render: full check result ──────────────────────────────────

    function renderCheckResult(result) {
        var probes = (result && result.probes) || [];
        var overall = rollupStatus(probes);
        var hero = statusToHero(overall);
        statusWord.textContent = hero.word;
        subtitle.textContent = hero.sub;
        document.getElementById('hl-hero').setAttribute('data-status', overall);
        gatewayLabel.textContent = result.gatewayAddress || (STRINGS.gateway_unset || 'Gateway not configured');
        lastResultAt = Date.now();
        updateCheckedLabel();

        renderProbes(probes);

        kpiDot.className = 'gst-health-dot is-' + overall;
        kpiStatus.textContent = hero.word;
        kpiStatusSub.textContent = hero.sub;

        kpiAvg.textContent = result.elapsedMs != null ? Math.round(result.elapsedMs) : '—';
    }

    function applyHistory(history) {
        var settings = (history && history.settings) || {};
        autoToggle.checked = !!settings.scanEnabled;
        lastScanAt = settings.lastScanAt || null;
        updateCheckedLabel();

        renderTimeline(history && history.scans);

        openIssues = (history && history.openIssues) || [];
        resolvedIssues = (history && history.recentlyResolved) || [];

        kpiIssues.textContent = openIssues.length;
        if (openIssues.length === 0) {
            kpiIssuesSub.textContent = (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.issues_clean)
                || 'All clear';
        } else {
            // Localized "%1 page(s) need attention" — fall back to a plain string.
            var tmpl = '%1 page(s) need attention';
            kpiIssuesSub.textContent = tmpl.replace('%1', openIssues.length);
        }

        // % content verified — derived from total catalog size minus open
        // issues. We render "—" when the catalog count isn't known yet
        // (typically the first paint before EnumeratePublishedContent has
        // returned anything).
        var total = (history && history.totalContent) || 0;
        if (total > 0) {
            var verified = Math.max(0, total - openIssues.length);
            var pct = (verified / total) * 100;
            kpiVerified.textContent = pct >= 99.95 ? '100' : pct.toFixed(1);
            var ofTmpl = (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.kpi_verified_of) || 'of %1 page(s)';
            kpiVerifiedSub.textContent = ofTmpl.replace('%1', total);
        } else {
            kpiVerified.textContent = '—';
            kpiVerifiedSub.textContent = (window.GST_STRINGS_RAW && window.GST_STRINGS_RAW.kpi_verified_unknown)
                || 'No catalog data';
        }
        if (issuesSummary) {
            issuesSummary.textContent = openIssues.length + ' open · ' + resolvedIssues.length + ' resolved';
        }
        renderIssues();
    }

    // ── HTTP ───────────────────────────────────────────────────────

    function loadCheck() {
        if (inFlight) return;
        inFlight = true;
        setAlert(null);
        runBtn.disabled = true;
        runBtn.classList.add('is-loading');
        statusWord.textContent = STRINGS.status_running || 'CHECKING';
        document.getElementById('hl-hero').setAttribute('data-status', 'checking');

        ajax(BASE + '/HealthApi/Check')
            .then(renderCheckResult)
            .catch(function (err) {
                setAlert(err.message, true);
                statusWord.textContent = STRINGS.status_unknown || 'UNKNOWN';
                document.getElementById('hl-hero').setAttribute('data-status', 'red');
            })
            .then(function () {
                inFlight = false;
                runBtn.disabled = false;
                runBtn.classList.remove('is-loading');
                // Refresh the timeline after a manual check too — the on-demand
                // run doesn't write history (only the scheduled job does), but a
                // recently completed scheduled job may have fresh data.
                loadHistory();
            });
    }

    function loadHistory() {
        return ajax(BASE + '/HealthApi/History')
            .then(applyHistory)
            .catch(function (err) {
                // Soft failure — keep what we have.
                setAlert(err.message, true);
            });
    }

    function postSettings(enabled) {
        return ajax(BASE + '/HealthApi/Settings', {
            method: 'POST',
            body: { scanEnabled: enabled }
        }).then(function (settings) {
            if (settings) {
                lastScanAt = settings.lastScanAt || lastScanAt;
                updateCheckedLabel();
            }
        });
    }

    // ── Wiring ─────────────────────────────────────────────────────

    autoToggle.addEventListener('change', function () {
        var enabled = autoToggle.checked;
        // Optimistic UI: reflect immediately; the POST persists. Toast on fail.
        updateCheckedLabel();
        postSettings(enabled).catch(function (err) {
            setAlert(err.message, true);
            autoToggle.checked = !enabled; // revert
            updateCheckedLabel();
        });
    });

    runBtn.addEventListener('click', loadCheck);

    issuesTabOpen.addEventListener('click', function () { issuesTab = 'open'; renderIssues(); });
    issuesTabResolved.addEventListener('click', function () { issuesTab = 'resolved'; renderIssues(); });

    // Tick "X seconds/minutes ago" once per second.
    checkedTickTimer = setInterval(updateCheckedLabel, 1000);

    // Poll history every 30s while the page is visible. The scheduled job
    // runs every 5 minutes, so anything more frequent is wasted load.
    function startHistoryPoll() {
        if (historyTimer) return;
        historyTimer = setInterval(function () {
            if (!document.hidden) loadHistory();
        }, HISTORY_POLL_MS);
    }
    startHistoryPoll();

    document.addEventListener('visibilitychange', function () {
        if (!document.hidden) loadHistory();
    });

    // ── Boot ───────────────────────────────────────────────────────
    // Settings + history first (paint quickly), then run an on-demand probe
    // so the hero reflects the current Graph state without waiting on the
    // scheduled job's 5-minute cadence.
    loadHistory().then(loadCheck);
})();
