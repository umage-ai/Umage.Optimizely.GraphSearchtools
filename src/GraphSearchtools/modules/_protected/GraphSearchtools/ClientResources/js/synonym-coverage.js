/**
 * Graph Search Tools — Synonym Coverage.
 *
 * Read-only analyzer surfaced as two cards:
 *   1. Unused synonyms — entries from the saved synonym blobs that didn't
 *      match any logged query in the rolling window. Each row deep-links to
 *      the Synonyms editor with the language pre-selected and the entry
 *      pre-filtered.
 *   2. Suggested adds — top zero-result phrases that look like missing
 *      synonyms. When the analyzer found a close indexed-term match it
 *      annotates the row with that hint; otherwise the row is the bare
 *      phrase + hit count (fall-back mode).
 *
 * The page calls a single endpoint (GET /SynonymCoverageApi/Index) that
 * already does the join in C#. Keeps the JS focussed on rendering + linking.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.synonymCoverage) || {};

    // The Synonyms editor is reached via the framework menu URL. We pass the
    // selected language + entry through the URL hash so the editor can opt
    // into pre-fill without us hard-coding a query-string contract here.
    var SYNONYMS_URL = '/EPiServer/cms/graphsearchtools/synonyms';

    // ── DOM ──────────────────────────────────────────────────────
    var alertBox = document.getElementById('gst-alert');
    var refreshBtn = document.getElementById('sc-refresh');
    var generatedAt = document.getElementById('sc-generated-at');
    var unusedGrid = document.getElementById('sc-unused-grid');
    var suggestedGrid = document.getElementById('sc-suggested-grid');

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
     * Build a deep-link to the Synonyms editor with hints in the hash. The
     * Synonyms page can ignore these on first load — fragment routing is a
     * progressive enhancement, not a contract.
     */
    function synonymsLink(language, prefill) {
        var params = [];
        if (language && language !== 'Global') params.push('language=' + encodeURIComponent(language));
        if (prefill) params.push('prefill=' + encodeURIComponent(prefill));
        return SYNONYMS_URL + (params.length ? '#' + params.join('&') : '');
    }

    // ── Render ───────────────────────────────────────────────────

    function renderUnused(rows) {
        unusedGrid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="4" class="gst-empty"><p>'
                + escHtml(STRINGS.empty || 'No prune candidates — every synonym entry fired at least once.')
                + '</p></td>';
            unusedGrid.appendChild(emptyTr);
            return;
        }

        rows.forEach(function (row) {
            var tr = document.createElement('tr');
            tr.className = 'gst-sc-row';
            tr.innerHTML =
                '<td>' + escHtml(row.language || '—') + '</td>' +
                '<td><code class="gst-mono">' + escHtml(row.entry) + '</code></td>' +
                '<td><span class="gst-rl-dim">' + escHtml(row.reason) + '</span></td>' +
                '<td class="gst-sc-actions-col">' +
                    '<a class="gst-btn gst-btn--sm" href="' + escHtml(synonymsLink(row.language, row.entry)) + '">' +
                        escHtml(STRINGS.prune_in_synonyms || 'Prune in Synonyms') +
                    '</a>' +
                '</td>';
            unusedGrid.appendChild(tr);
        });
    }

    function renderSuggested(rows) {
        suggestedGrid.innerHTML = '';
        if (!rows || rows.length === 0) {
            var emptyTr = document.createElement('tr');
            emptyTr.innerHTML = '<td colspan="5" class="gst-empty"><p>'
                + escHtml(STRINGS.no_logs || 'No zero-result phrases in the window — nothing to suggest.')
                + '</p></td>';
            suggestedGrid.appendChild(emptyTr);
            return;
        }

        rows.forEach(function (row) {
            var closestCell = row.closestIndexedTerm
                ? '<code class="gst-mono">' + escHtml(row.closestIndexedTerm) + '</code>'
                : '<span class="gst-rl-dim">—</span>';
            var similarity = row.similarity != null ? row.similarity : '—';
            var locale = row.locale ? ' <span class="gst-rl-dim">(' + escHtml(row.locale) + ')</span>' : '';

            // The "Add" link pre-fills the suggested zero-result phrase. The
            // Synonyms page is responsible for choosing whether it lands as a
            // replacement (`phrase => indexed-term`) or an equivalent
            // (`phrase, indexed-term`) — we just hand it the raw text.
            var prefill = row.closestIndexedTerm
                ? row.phrase + ' => ' + row.closestIndexedTerm
                : row.phrase;

            var tr = document.createElement('tr');
            tr.className = 'gst-sc-row';
            tr.innerHTML =
                '<td>' + escHtml(row.phrase) + locale + '</td>' +
                '<td class="num">' + escHtml(row.hits) + '</td>' +
                '<td>' + closestCell + '</td>' +
                '<td class="num">' + escHtml(similarity) + '</td>' +
                '<td class="gst-sc-actions-col">' +
                    '<a class="gst-btn gst-btn--sm" href="' + escHtml(synonymsLink(row.locale, prefill)) + '">' +
                        escHtml(STRINGS.add_in_synonyms || 'Add in Synonyms') +
                    '</a>' +
                '</td>';
            suggestedGrid.appendChild(tr);
        });
    }

    function renderGeneratedAt(stamp) {
        if (!stamp) {
            generatedAt.textContent = '';
            return;
        }
        var text = STRINGS.generated_at || 'Generated';
        try {
            var d = new Date(stamp);
            if (!isNaN(d.getTime())) {
                generatedAt.textContent = text + ': ' + d.toLocaleString();
                return;
            }
        } catch (_) { /* fall through */ }
        generatedAt.textContent = text + ': ' + stamp;
    }

    function load() {
        setAlert('');
        ajax(BASE + '/SynonymCoverageApi/Index')
            .then(function (result) {
                if (!result) {
                    renderUnused([]);
                    renderSuggested([]);
                    renderGeneratedAt(null);
                    return;
                }
                renderGeneratedAt(result.generatedAt);
                renderUnused(result.unusedEntries || []);
                renderSuggested(result.suggestedAdds || []);
            })
            .catch(function (err) {
                renderUnused([]);
                renderSuggested([]);
                renderGeneratedAt(null);
                setAlert((STRINGS.load_failed || 'Could not load synonym coverage.') + ' ' + err.message, true);
            });
    }

    // ── Wire-up ──────────────────────────────────────────────────

    refreshBtn.addEventListener('click', load);

    load();
})();
