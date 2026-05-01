/**
 * Graph Search Tools — Autocomplete Tester.
 *
 * Type into the box, get suggestions back from Graph's `autocomplete`
 * field on the Name selector. Locale picker filters by language; debounced
 * 200ms to avoid hammering the gateway on every keystroke.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.autocomplete) || {};

    var alertBox = document.getElementById('gst-alert');
    var input = document.getElementById('ac-input');
    var localeSelect = document.getElementById('ac-locale');
    var limitInput = document.getElementById('ac-limit');
    var resultsList = document.getElementById('ac-results');
    var meta = document.getElementById('ac-meta');

    var debounce = null;
    var lastRequestId = 0;

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
            .catch(function () { /* sites are optional; keep "Any locale" */ });
    }

    function clearResults() {
        resultsList.innerHTML = '';
        meta.textContent = '';
    }

    function renderResults(suggestions, value, durationMs) {
        resultsList.innerHTML = '';
        if (!suggestions.length) {
            var empty = document.createElement('li');
            empty.className = 'gst-ac-empty';
            empty.textContent = STRINGS.no_results || 'No suggestions.';
            resultsList.appendChild(empty);
        } else {
            suggestions.forEach(function (s) {
                var li = document.createElement('li');
                li.className = 'gst-ac-result';
                li.textContent = s;
                resultsList.appendChild(li);
            });
        }
        meta.textContent = (STRINGS.returned_for || 'Returned %1 in %2 ms')
            .replace('%1', suggestions.length)
            .replace('%2', durationMs);
    }

    function fire() {
        var value = input.value.trim();
        if (!value) { clearResults(); return; }
        var locale = localeSelect.value;
        var limit = Math.max(1, Math.min(25, parseInt(limitInput.value, 10) || 10));
        var qs = '?value=' + encodeURIComponent(value) + '&limit=' + limit;
        if (locale) qs += '&locale=' + encodeURIComponent(locale);

        var requestId = ++lastRequestId;
        var t0 = performance.now();
        setAlert(null);
        fetch(BASE + '/AutocompleteApi/Suggest' + qs, {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (resp) {
                if (!resp.ok) {
                    return resp.text().then(function (t) {
                        var msg = STRINGS.request_failed || 'Autocomplete failed';
                        try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                        throw new Error(msg + ' (' + resp.status + ')');
                    });
                }
                return resp.json();
            })
            .then(function (suggestions) {
                if (requestId !== lastRequestId) return; // stale
                renderResults(suggestions || [], value, Math.round(performance.now() - t0));
            })
            .catch(function (err) {
                if (requestId !== lastRequestId) return;
                setAlert(err.message, true);
                clearResults();
            });
    }

    input.addEventListener('input', function () {
        clearTimeout(debounce);
        debounce = setTimeout(fire, 200);
    });
    localeSelect.addEventListener('change', fire);
    limitInput.addEventListener('change', fire);

    loadLocales();
})();
