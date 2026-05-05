/**
 * Graph Search Tools — Content Searchability Audit (Phase 4).
 *
 * The page is a thin runner: click "Run audit" → POST to the API → render
 * stats + grouped issue tables. The audit is potentially expensive (walks
 * every published page, reflects every property), so it never runs on page
 * load — the editor opts in explicitly. Rows link to CMS edit-mode using
 * the same URL pattern Health uses (window.GST_CMS_URL + #context=epi.cms.contentdata).
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.contentAudit) || {};
    var SHARED = (window.GST_STRINGS && window.GST_STRINGS.shared) || {};

    var KIND_ORDER = ['MissingName', 'MissingMainBody', 'NoTags', 'OversizeSortField'];
    var KIND_LABEL = {
        MissingName: STRINGS.kind_missing_name || 'Missing Name',
        MissingMainBody: STRINGS.kind_missing_main_body || 'Missing MainBody',
        NoTags: STRINGS.kind_no_tags || 'No Tags',
        OversizeSortField: STRINGS.kind_oversize_sort || 'Oversize sortable field'
    };

    var runBtn = document.getElementById('csa-run');
    var runningHint = document.getElementById('csa-running');
    var scannedAtLabel = document.getElementById('csa-scanned-at');
    var statsBox = document.getElementById('csa-stats');
    var resultsBox = document.getElementById('csa-results');
    var emptyBox = document.getElementById('csa-empty');
    var alertBox = document.getElementById('gst-alert');

    function escHtml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function fmtTime(value) {
        if (!value) return '';
        try {
            var d = new Date(value);
            if (isNaN(d.getTime())) return '';
            return d.toLocaleString();
        } catch (_) { return ''; }
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

    /**
     * CMS edit-mode deep link for one issue. Mirrors the Health page so
     * editors get the same "open in CMS" UX everywhere in the addon.
     * Falls back to a no-op anchor when the row predates ContentLink ids
     * (defensive — shouldn't happen with the current service contract).
     */
    function editUrl(issue) {
        if (!issue || !issue.contentLink) return '#';
        var base = window.GST_CMS_URL || '';
        var lang = encodeURIComponent(issue.locale || '');
        return base + '?language=' + lang + '#context=epi.cms.contentdata:///' + issue.contentLink;
    }

    function renderStats(payload, byKind) {
        statsBox.hidden = false;
        document.getElementById('csa-stat-scanned').textContent = String(payload.itemsScanned || 0);

        var total = 0;
        KIND_ORDER.forEach(function (kind) {
            var n = (byKind[kind] || []).length;
            total += n;
            var el = document.getElementById('csa-stat-' + kind);
            if (el) el.textContent = String(n);
        });
        document.getElementById('csa-stat-total').textContent = String(total);

        if (payload.scannedAt) {
            var prefix = STRINGS.scanned_at || 'Scanned';
            scannedAtLabel.textContent = prefix + ' ' + fmtTime(payload.scannedAt);
        } else {
            scannedAtLabel.textContent = '';
        }
    }

    function renderTable(kind, issues) {
        var section = document.createElement('section');
        section.className = 'gst-csa-section';
        section.setAttribute('data-kind', kind);

        var header = document.createElement('div');
        header.className = 'gst-csa-section__head';
        header.innerHTML =
            '<h2 class="gst-csa-section__title">' + escHtml(KIND_LABEL[kind] || kind) + '</h2>' +
            '<span class="gst-badge gst-badge--default">' + issues.length + '</span>';
        section.appendChild(header);

        var table = document.createElement('table');
        table.className = 'gst-table gst-csa-table';
        table.innerHTML =
            '<thead><tr>' +
                '<th>' + escHtml(STRINGS.col_name || 'Name') + '</th>' +
                '<th>' + escHtml(STRINGS.col_type || 'Type') + '</th>' +
                '<th>' + escHtml(STRINGS.col_detail || 'Detail') + '</th>' +
                '<th class="gst-csa-table__edit">' + escHtml(STRINGS.col_edit || '') + '</th>' +
            '</tr></thead>' +
            '<tbody></tbody>';
        var tbody = table.querySelector('tbody');

        issues.forEach(function (issue) {
            var tr = document.createElement('tr');
            tr.className = 'gst-csa-row';
            var localeBadge = issue.locale
                ? '<span class="gst-badge gst-badge--default gst-csa-locale">' + escHtml(issue.locale) + '</span>'
                : '';
            tr.innerHTML =
                '<td>' + escHtml(issue.name || '') + ' ' + localeBadge + '</td>' +
                '<td>' + escHtml(issue.contentType || '') + '</td>' +
                '<td>' + escHtml(issue.detail || '') + '</td>' +
                '<td class="gst-csa-table__edit">' +
                    '<a class="gst-btn gst-btn--sm" target="_blank" rel="noopener" href="' + editUrl(issue) + '">' +
                        escHtml(STRINGS.edit || SHARED.edit || 'Edit') +
                    '</a>' +
                '</td>';

            // Click-row-to-jump (with the cell's anchor still working).
            tr.addEventListener('click', function (ev) {
                if (ev.target.tagName === 'A') return;
                var url = editUrl(issue);
                if (url && url !== '#') window.open(url, '_blank', 'noopener');
            });
            tbody.appendChild(tr);
        });

        section.appendChild(table);
        return section;
    }

    function renderResults(payload) {
        resultsBox.innerHTML = '';
        var issues = payload.items || [];
        var byKind = {};
        KIND_ORDER.forEach(function (k) { byKind[k] = []; });
        issues.forEach(function (i) {
            if (!byKind[i.kind]) byKind[i.kind] = [];
            byKind[i.kind].push(i);
        });

        renderStats(payload, byKind);

        var total = issues.length;
        if (total === 0) {
            emptyBox.hidden = false;
            return;
        }
        emptyBox.hidden = true;

        KIND_ORDER.forEach(function (kind) {
            var rows = byKind[kind];
            if (!rows || rows.length === 0) return;
            resultsBox.appendChild(renderTable(kind, rows));
        });
    }

    function setRunning(on) {
        runBtn.disabled = !!on;
        runningHint.hidden = !on;
    }

    function run() {
        setAlert('');
        setRunning(true);
        fetch(BASE + '/ContentSearchabilityAuditApi/Run', {
            method: 'POST',
            headers: {
                'X-Requested-With': 'XMLHttpRequest',
                'Content-Type': 'application/json'
            },
            credentials: 'same-origin',
            body: '{}'
        })
            .then(function (resp) {
                if (!resp.ok) {
                    return resp.text().then(function (t) {
                        var msg = STRINGS.run_failed || 'Audit failed.';
                        try { var p = t ? JSON.parse(t) : null; if (p && p.title) msg = p.title; } catch (_) { }
                        throw new Error(msg + ' (' + resp.status + ')');
                    });
                }
                return resp.json();
            })
            .then(renderResults)
            .catch(function (err) {
                setAlert((STRINGS.run_failed || 'Audit failed.') + ' ' + err.message, true);
            })
            .then(function () {
                setRunning(false);
            });
    }

    runBtn.addEventListener('click', run);
})();
