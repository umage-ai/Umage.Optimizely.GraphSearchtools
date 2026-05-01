/**
 * Graph Search Tools — Search Console.
 *
 * Compose a query + ranking knobs, run it against Graph, render the hits with
 * scores and full-text snippets. The exact GraphQL document we sent is
 * available behind a "Show query" toggle so the user can copy it.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.searchconsole) || {};

    var alertBox = document.getElementById('gst-alert');
    var queryInput = document.getElementById('sc-query');
    var localeSelect = document.getElementById('sc-locale');
    var rankingSelect = document.getElementById('sc-ranking');
    var weightInput = document.getElementById('sc-weight');
    var weightOut = document.getElementById('sc-weight-out');
    var minScoreInput = document.getElementById('sc-min-score');
    var limitInput = document.getElementById('sc-limit');
    var runButton = document.getElementById('sc-run');
    var grid = document.getElementById('sc-grid');
    var meta = document.getElementById('sc-meta');
    var showQueryBtn = document.getElementById('sc-show-query');
    var queryText = document.getElementById('sc-query-text');

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

    function loadLocales() {
        return fetch(BASE + '/SitesApi/List', {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (resp) { return resp.ok ? resp.json() : []; })
            .then(function (sites) {
                localeSelect.innerHTML = '';
                var optAll = document.createElement('option');
                optAll.value = '';
                optAll.textContent = STRINGS.any_locale || 'Any locale';
                localeSelect.appendChild(optAll);
                (sites || []).forEach(function (site) {
                    var opt = document.createElement('option');
                    opt.value = site.languageCode;
                    opt.textContent = site.title + ' (' + site.languageCode + ')';
                    localeSelect.appendChild(opt);
                });
            })
            .catch(function () { /* sites optional */ });
    }

    function snapshotState() {
        return {
            query: queryInput.value.trim(),
            locale: localeSelect.value || null,
            ranking: rankingSelect.value || 'RELEVANCE',
            semanticWeight: parseFloat(weightInput.value) || 0.2,
            minimumScore: minScoreInput.value === '' ? null : parseFloat(minScoreInput.value),
            limit: Math.max(1, Math.min(100, parseInt(limitInput.value, 10) || 25))
        };
    }

    function run() {
        var state = snapshotState();
        if (!state.query) {
            setAlert(STRINGS.error_query_required || 'Enter a query to run.', true);
            return;
        }
        setAlert(null);
        runButton.disabled = true;
        meta.textContent = STRINGS.running || 'Running…';
        showQueryBtn.hidden = true;
        queryText.hidden = true;

        fetch(BASE + '/SearchConsoleApi/Run', {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: JSON.stringify(state)
        })
            .then(function (resp) {
                if (!resp.ok) {
                    return resp.text().then(function (t) {
                        var msg = STRINGS.request_failed || 'Search failed';
                        try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                        throw new Error(msg + ' (' + resp.status + ')');
                    });
                }
                return resp.json();
            })
            .then(renderResult)
            .catch(function (err) { setAlert(err.message, true); meta.textContent = ''; })
            .then(function () { runButton.disabled = false; });
    }

    function renderResult(result) {
        grid.innerHTML = '';
        var hits = (result && result.hits) || [];
        meta.textContent = (STRINGS.returned_for || 'Returned %1 of %2 in %3 ms')
            .replace('%1', hits.length)
            .replace('%2', result.totalCount)
            .replace('%3', result.durationMs);
        if (result && result.graphQuery) {
            queryText.textContent = result.graphQuery.trim();
            showQueryBtn.hidden = false;
        }

        if (!hits.length) {
            var empty = document.createElement('tr');
            var cell = document.createElement('td');
            cell.colSpan = 6;
            cell.className = 'gst-sc-empty';
            cell.textContent = STRINGS.no_results || 'No matching content.';
            empty.appendChild(cell);
            grid.appendChild(empty);
            return;
        }

        hits.forEach(function (hit, index) {
            var tr = document.createElement('tr');

            var rankCell = document.createElement('td');
            rankCell.className = 'gst-sc-col-rank';
            rankCell.textContent = String(index + 1);
            tr.appendChild(rankCell);

            var nameCell = document.createElement('td');
            if (hit.contentId) {
                var link = document.createElement('a');
                link.href = (window.GST_CMS_URL || '') + '?language=' + encodeURIComponent(hit.language || '') + '#context=epi.cms.contentdata:///' + hit.contentId;
                link.target = '_blank';
                link.rel = 'noopener';
                link.textContent = hit.name || '(unnamed)';
                nameCell.appendChild(link);
            } else {
                nameCell.textContent = hit.name || '(unnamed)';
            }
            tr.appendChild(nameCell);

            var typeCell = document.createElement('td');
            typeCell.className = 'gst-sc-col-type';
            typeCell.textContent = hit.contentType || '';
            tr.appendChild(typeCell);

            var langCell = document.createElement('td');
            langCell.className = 'gst-sc-col-lang';
            langCell.textContent = hit.language || '';
            tr.appendChild(langCell);

            var scoreCell = document.createElement('td');
            scoreCell.className = 'gst-sc-col-score';
            scoreCell.textContent = (hit.score == null) ? '' : Number(hit.score).toFixed(3);
            tr.appendChild(scoreCell);

            var snipCell = document.createElement('td');
            snipCell.className = 'gst-sc-col-snippet';
            snipCell.textContent = hit.fullTextSnippet || '';
            tr.appendChild(snipCell);

            grid.appendChild(tr);
        });
    }

    weightInput.addEventListener('input', function () {
        weightOut.textContent = Number(weightInput.value).toFixed(2);
    });
    runButton.addEventListener('click', run);
    queryInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); run(); }
    });
    showQueryBtn.addEventListener('click', function () {
        queryText.hidden = !queryText.hidden;
        showQueryBtn.textContent = queryText.hidden
            ? (STRINGS.show_query || 'Show query')
            : (STRINGS.hide_query || 'Hide query');
    });

    function applyHashIfPresent() {
        // Saved Queries deep-links arrive as #q=...&locale=...&ranking=...&weight=...&min=...&limit=...
        var hash = (window.location.hash || '').replace(/^#/, '');
        if (!hash) return false;
        var params = new URLSearchParams(hash);
        if (!params.has('q')) return false;
        queryInput.value = params.get('q') || '';
        rankingSelect.value = params.get('ranking') || 'RELEVANCE';
        var w = parseFloat(params.get('weight'));
        if (!Number.isNaN(w)) {
            weightInput.value = w;
            weightOut.textContent = w.toFixed(2);
        }
        var min = params.get('min');
        minScoreInput.value = (min === null || min === '') ? '' : min;
        var lim = parseInt(params.get('limit'), 10);
        if (!Number.isNaN(lim)) limitInput.value = lim;
        return { locale: params.get('locale') || '' };
    }

    loadLocales().then(function () {
        var apply = applyHashIfPresent();
        if (apply) {
            // Locale list isn't ready until loadLocales resolves; set after.
            if (apply.locale) localeSelect.value = apply.locale;
            run();
        }
    });
})();
