/**
 * Graph Search Tools — Pinned Result Coverage (Phase 4 §6).
 *
 * Read-only audit view. The page issues a single GET to
 * /PinnedCoverageApi/Audit, then renders three surfaces from one payload:
 *   - Stats row (issues by kind).
 *   - Issues table with a kind-coloured badge + "Fix in profile" deep link.
 *   - Overlaps card with the duplicate-phrase rows.
 *
 * Re-running the audit is the only mutation; everything else is presentational.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinnedCoverage) || {};
    var SHARED = (window.GST_STRINGS && window.GST_STRINGS.shared) || {};
    /**
     * Profile detail URL — anchored on `#pinned` so we land directly on the
     * Pinned tab. Hosts that bookmark `/pinned` are 301'd to /profiles, so
     * this is the canonical fix-up location post-Phase-2.5. The detail
     * surface is served as `?key=...` on the index URL so the CMS shell can
     * resolve the section's product-id from the registered menu URL.
     */
    var PROFILE_URL_BASE = '/EPiServer/cms/graphsearchtools/profiles?key=';

    var GST = window.GST = window.GST || {};
    GST.pinnedCoverage = GST.pinnedCoverage || {};

    var alertBox = document.getElementById('gst-alert');
    var runBtn = document.getElementById('pc-run');
    var statsRow = document.getElementById('pc-stats');
    var grid = document.getElementById('pc-grid');
    var overlaps = document.getElementById('pc-overlaps');
    var generatedEl = document.getElementById('pc-generated');

    /**
     * Fixed list of issue kinds in render order. Mirrors the server's
     * KindSortOrder so the stat tiles read top-down most-severe to least.
     */
    var KINDS = ['Deleted', 'Unpublished', 'Expired', 'LowCtr', 'NoActivity'];

    var KIND_TO_LABEL = {
        Unpublished: 'issue_kind_unpublished',
        Deleted: 'issue_kind_deleted',
        Expired: 'issue_kind_expired',
        LowCtr: 'issue_kind_low_ctr',
        NoActivity: 'issue_kind_no_activity'
    };

    var KIND_TO_BADGE = {
        Unpublished: 'gst-badge--warning',
        Deleted: 'gst-badge--danger',
        Expired: 'gst-badge--warning',
        LowCtr: 'gst-badge--warning',
        NoActivity: 'gst-badge--default'
    };

    function ajax(url, opts) {
        opts = opts || {};
        return fetch(url, {
            method: opts.method || 'GET',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            credentials: 'same-origin'
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.load_failed || 'Audit failed';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            return resp.status === 204 ? null : resp.json();
        });
    }

    function setAlert(message, isError) {
        if (!alertBox) return;
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

    function fmtTime(value) {
        if (!value) return '—';
        try {
            var d = new Date(value);
            if (isNaN(d.getTime())) return '—';
            return d.toLocaleString();
        } catch (_) { return '—'; }
    }

    function labelFor(kind) {
        var key = KIND_TO_LABEL[kind];
        return (key && STRINGS[key]) || kind;
    }

    function badgeClass(kind) {
        return KIND_TO_BADGE[kind] || 'gst-badge--default';
    }

    function renderStats(issues) {
        if (!statsRow) return;
        var counts = {};
        KINDS.forEach(function (k) { counts[k] = 0; });
        (issues || []).forEach(function (i) {
            if (counts.hasOwnProperty(i.kind)) counts[i.kind] += 1;
        });

        statsRow.innerHTML = KINDS.map(function (kind) {
            return '' +
                '<div class="gst-stat">' +
                    '<div class="gst-stat__value">' + counts[kind] + '</div>' +
                    '<div class="gst-stat__label">' + escHtml(labelFor(kind)) + '</div>' +
                '</div>';
        }).join('');
    }

    function fixCell(issue) {
        // ProfileKey is null when the collection key doesn't match any
        // registered profile's PinnedKey formula — those rows are read-only.
        if (!issue.profileKey) {
            return '<span class="gst-muted">' + escHtml(STRINGS.no_profile || 'No profile') + '</span>';
        }
        var href = PROFILE_URL_BASE + encodeURIComponent(issue.profileKey) + '#pinned';
        return '<a class="gst-btn gst-btn--sm" href="' + href + '">' + escHtml(STRINGS.fix_in_profile || 'Fix') + '</a>';
    }

    function renderGrid(issues) {
        grid.innerHTML = '';
        if (!issues || issues.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="6" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No pinned issues found.') + '</p></td>';
            grid.appendChild(emptyTr);
            return;
        }
        issues.forEach(function (issue) {
            var tr = document.createElement('tr');
            var targetCell = issue.targetName
                ? escHtml(issue.targetName) + ' <code class="gst-pc-target-id">' + escHtml(issue.targetId || '') + '</code>'
                : '<code class="gst-pc-target-id">' + escHtml(issue.targetId || '—') + '</code>';
            tr.innerHTML =
                '<td><span class="gst-badge ' + badgeClass(issue.kind) + '">' + escHtml(labelFor(issue.kind)) + '</span></td>' +
                '<td>' + escHtml(issue.phrase || '') + '</td>' +
                '<td>' + targetCell + '</td>' +
                '<td><code>' + escHtml(issue.collectionKey || '') + '</code></td>' +
                '<td>' + escHtml(issue.detail || '') + '</td>' +
                '<td class="gst-pc-fix-col">' + fixCell(issue) + '</td>';
            grid.appendChild(tr);
        });
    }

    function renderOverlaps(rows) {
        overlaps.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="2" class="gst-empty"><p>' + escHtml(STRINGS.empty || 'No overlaps detected.') + '</p></td>';
            overlaps.appendChild(emptyTr);
            return;
        }
        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            var collectionsHtml = (row.collections || [])
                .map(function (c) { return '<code class="gst-pc-coll">' + escHtml(c) + '</code>'; })
                .join(' ');
            tr.innerHTML =
                '<td>' + escHtml(row.phrase || '') + '</td>' +
                '<td>' + collectionsHtml + '</td>';
            overlaps.appendChild(tr);
        });
    }

    function setGenerated(stamp) {
        if (!generatedEl) return;
        if (!stamp) { generatedEl.textContent = ''; return; }
        var label = STRINGS.generated_at || 'Generated';
        generatedEl.textContent = label + ': ' + fmtTime(stamp);
    }

    function setLoading(on) {
        if (!runBtn) return;
        runBtn.disabled = !!on;
        runBtn.textContent = on
            ? (SHARED.loading || 'Loading...')
            : (STRINGS.run_audit || 'Run audit');
    }

    function load() {
        setAlert('');
        setLoading(true);
        ajax(BASE + '/PinnedCoverageApi/Audit')
            .then(function (data) {
                data = data || {};
                renderStats(data.issues);
                renderGrid(data.issues);
                renderOverlaps(data.overlaps);
                setGenerated(data.generatedAt);
            })
            .catch(function (err) {
                setAlert((STRINGS.load_failed || 'Could not load coverage audit.') + ' ' + err.message, true);
            })
            .then(function () { setLoading(false); });
    }

    // Lazy init: this script is included on the Pinned page (Pins tab is the
    // landing surface), but the audit fetch should only happen when the
    // marketer actually opens the Audit tab. The merged Pinned view calls
    // window.GST_PC_INIT on first tab switch.
    function init() {
        if (init._done) return;
        init._done = true;
        if (runBtn) runBtn.addEventListener('click', load);
        load();
    }
    window.GST_PC_INIT = init;

    // Expose for tests / probes.
    GST.pinnedCoverage.reload = load;
})();
