/**
 * Graph Search Tools — Autocomplete Tester.
 *
 * Schema-driven: on load we introspect Graph for all root content types that
 * expose `autocomplete` plus their scalar string fields. Type and field are
 * filterable comboboxes (text input + dropdown panel + keyboard nav) because
 * a real schema returns 100+ types, more than a `<select>` can comfortably
 * scroll. Every keystroke (debounced 200ms) hits Suggest with
 * type+field+value+locale+limit.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.autocomplete) || {};

    var alertBox = document.getElementById('gst-alert');
    var input = document.getElementById('ac-input');
    var typeInput = document.getElementById('ac-type');
    var fieldInput = document.getElementById('ac-field');
    var localeSelect = document.getElementById('ac-locale');
    var limitInput = document.getElementById('ac-limit');
    var resultsList = document.getElementById('ac-results');
    var meta = document.getElementById('ac-meta');

    var schema = []; // [{ typeName, fields: [string] }]
    var typeCombo = null;
    var fieldCombo = null;
    var debounce = null;
    var lastRequestId = 0;

    // ── HTTP helper ───────────────────────────────────────────────

    function ajax(url) {
        return fetch(url, {
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Request failed';
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

    // ── Combobox: text input + filterable dropdown + keyboard nav ──

    /**
     * Wires combobox behaviour onto a plain text input. Caller supplies
     * `getOptions()` (returns the current candidate list) and `onSelect(value)`.
     * Returns a small handle for setValue / refresh.
     */
    function combobox(inputEl, getOptions, onSelect) {
        var dropdown = document.createElement('div');
        dropdown.className = 'gst-combobox-dropdown';
        dropdown.hidden = true;
        inputEl.parentElement.appendChild(dropdown);

        var visible = [];
        var highlightIndex = -1;
        var lastSelected = '';

        function render() {
            var query = inputEl.value.trim().toLowerCase();
            var all = getOptions() || [];
            // When the input matches the last selection exactly, show the full
            // list — otherwise the user can never see siblings without clearing.
            var showAll = !query || query === lastSelected.toLowerCase();
            visible = showAll
                ? all.slice(0)
                : all.filter(function (o) { return o.toLowerCase().indexOf(query) !== -1; });
            dropdown.innerHTML = '';
            if (!visible.length) {
                dropdown.hidden = true;
                return;
            }
            visible.forEach(function (opt, i) {
                var div = document.createElement('div');
                div.className = 'gst-combobox-item' + (i === highlightIndex ? ' is-highlighted' : '');
                div.textContent = opt;
                div.addEventListener('mousedown', function (e) {
                    // mousedown fires before blur so the click registers before
                    // the dropdown is hidden by the blur handler.
                    e.preventDefault();
                    pick(opt);
                });
                dropdown.appendChild(div);
            });
            dropdown.hidden = false;
        }

        function pick(value) {
            inputEl.value = value;
            lastSelected = value;
            dropdown.hidden = true;
            highlightIndex = -1;
            if (onSelect) onSelect(value);
        }

        inputEl.addEventListener('focus', function () { highlightIndex = -1; render(); });
        inputEl.addEventListener('input', function () { highlightIndex = -1; render(); });
        inputEl.addEventListener('blur', function () {
            // Slight delay so item mousedown still wins.
            setTimeout(function () { dropdown.hidden = true; }, 150);
        });
        inputEl.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (dropdown.hidden) { render(); }
                if (visible.length === 0) return;
                highlightIndex = Math.min(highlightIndex + 1, visible.length - 1);
                if (highlightIndex < 0) highlightIndex = 0;
                render();
                ensureHighlightVisible(dropdown, highlightIndex);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (visible.length === 0) return;
                highlightIndex = Math.max(highlightIndex - 1, 0);
                render();
                ensureHighlightVisible(dropdown, highlightIndex);
            } else if (e.key === 'Enter') {
                if (highlightIndex >= 0 && highlightIndex < visible.length) {
                    e.preventDefault();
                    pick(visible[highlightIndex]);
                } else if (visible.length === 1) {
                    e.preventDefault();
                    pick(visible[0]);
                }
            } else if (e.key === 'Escape') {
                dropdown.hidden = true;
                highlightIndex = -1;
            }
        });

        return {
            setValue: function (v) {
                inputEl.value = v || '';
                lastSelected = v || '';
            },
            getValue: function () { return inputEl.value; },
            refresh: render
        };
    }

    function ensureHighlightVisible(dropdown, index) {
        var item = dropdown.children[index];
        if (item && item.scrollIntoView) {
            item.scrollIntoView({ block: 'nearest' });
        }
    }

    // ── Schema + locale loading ───────────────────────────────────

    function loadSchema() {
        return ajax(BASE + '/AutocompleteApi/Schema')
            .then(function (s) { schema = s || []; })
            .catch(function (err) {
                schema = [];
                setAlert((STRINGS.schema_failed || 'Could not load schema') + ': ' + err.message, true);
            });
    }

    function loadLocales() {
        // Pulls the Locales enum from the live Graph schema (see SitesApi/Locales)
        // so the picker matches what Graph can serve, not what the host CMS
        // happens to declare.
        return ajax(BASE + '/SitesApi/Locales')
            .then(function (locales) {
                localeSelect.innerHTML = '';
                var optAll = document.createElement('option');
                optAll.value = '';
                optAll.textContent = STRINGS.all_locales || 'ALL';
                localeSelect.appendChild(optAll);
                (locales || []).forEach(function (code) {
                    var opt = document.createElement('option');
                    opt.value = code;
                    opt.textContent = code;
                    localeSelect.appendChild(opt);
                });
            })
            .catch(function () { /* locale picker is optional */ });
    }

    function typeOptions() {
        return schema.map(function (s) { return s.typeName; });
    }

    function fieldOptions() {
        var t = typeInput.value.trim();
        var d = schema.find(function (s) { return s.typeName === t; });
        return d ? (d.fields || []) : [];
    }

    // Preferred defaults, in priority order. Whichever appears first in the
    // schema wins; if none match we fall back to the first available item so
    // the picker is never empty.
    var DEFAULT_TYPE_PREFERENCE  = ['_Page', 'Content'];
    var DEFAULT_FIELD_PREFERENCE = ['DisplayName', 'RouteSegment'];

    function firstMatch(preferred, available) {
        for (var i = 0; i < preferred.length; i++) {
            if (available.indexOf(preferred[i]) !== -1) return preferred[i];
        }
        return available.length ? available[0] : '';
    }

    function pickDefaults() {
        if (!schema.length) return;
        var typeNames = schema.map(function (s) { return s.typeName; });
        var defaultType = firstMatch(DEFAULT_TYPE_PREFERENCE, typeNames);
        if (!defaultType) return;
        typeCombo.setValue(defaultType);
        var d = schema.find(function (s) { return s.typeName === defaultType; });
        var defaultField = firstMatch(DEFAULT_FIELD_PREFERENCE, (d && d.fields) || []);
        if (defaultField) fieldCombo.setValue(defaultField);
    }

    // ── Results render & query fire ───────────────────────────────

    function clearResults() {
        resultsList.innerHTML = '';
        meta.textContent = '';
    }

    function renderResults(suggestions, durationMs) {
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
        var type = typeInput.value.trim();
        var field = fieldInput.value.trim();
        if (!type || !field) {
            setAlert(STRINGS.error_pick_type_and_field || 'Pick a type and a field first.', true);
            return;
        }
        var locale = localeSelect.value;
        var limit = Math.max(1, Math.min(25, parseInt(limitInput.value, 10) || 10));
        var qs = '?type=' + encodeURIComponent(type)
            + '&field=' + encodeURIComponent(field)
            + '&value=' + encodeURIComponent(value)
            + '&limit=' + limit;
        if (locale) qs += '&locale=' + encodeURIComponent(locale);

        var requestId = ++lastRequestId;
        var t0 = performance.now();
        setAlert(null);
        ajax(BASE + '/AutocompleteApi/Suggest' + qs)
            .then(function (suggestions) {
                if (requestId !== lastRequestId) return;
                renderResults(suggestions || [], Math.round(performance.now() - t0));
            })
            .catch(function (err) {
                if (requestId !== lastRequestId) return;
                setAlert(err.message, true);
                clearResults();
            });
    }

    // ── Bind events ───────────────────────────────────────────────

    typeCombo = combobox(typeInput, typeOptions, function () {
        // Type changed — reset the field to a sensible default for this type
        // (same priority as the initial pick) and re-fire if the value has content.
        var d = schema.find(function (s) { return s.typeName === typeInput.value.trim(); });
        var preferredField = firstMatch(DEFAULT_FIELD_PREFERENCE, (d && d.fields) || []);
        fieldCombo.setValue(preferredField);
        fire();
    });

    fieldCombo = combobox(fieldInput, fieldOptions, function () { fire(); });

    input.addEventListener('input', function () {
        clearTimeout(debounce);
        debounce = setTimeout(fire, 200);
    });
    localeSelect.addEventListener('change', fire);
    limitInput.addEventListener('change', fire);

    Promise.all([loadSchema(), loadLocales()]).then(function () {
        pickDefaults();
    });
})();
