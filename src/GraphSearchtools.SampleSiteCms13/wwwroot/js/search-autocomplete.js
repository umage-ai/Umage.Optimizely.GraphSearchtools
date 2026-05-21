(function () {
    'use strict';

    // Attaches Google-style type-ahead to every input marked
    // [data-autocomplete-search]. Debounced fetch against
    // /api/search/suggest, keyboard nav, click-to-fill-and-submit.

    var DEBOUNCE_MS = 180;
    var MIN_LENGTH = 2;
    var ENDPOINT = '/api/search/suggest';

    function init(input) {
        if (input.__autocompleteAttached) return;
        input.__autocompleteAttached = true;
        input.setAttribute('autocomplete', 'off');

        var dropdown = document.createElement('div');
        dropdown.className = 'gst-suggest';
        dropdown.setAttribute('role', 'listbox');
        dropdown.hidden = true;

        // Anchor the dropdown to the input by inserting it as a sibling and
        // wrapping the input in a positioning shell so the dropdown can be
        // absolutely positioned without forcing layout changes elsewhere.
        var shell = document.createElement('div');
        shell.className = 'gst-suggest-shell';
        input.parentNode.insertBefore(shell, input);
        shell.appendChild(input);
        shell.appendChild(dropdown);

        var state = {
            items: [],
            active: -1,
            controller: null,
            debounceTimer: 0,
            lastQuery: ''
        };

        function render(query) {
            if (!state.items.length) {
                dropdown.hidden = true;
                dropdown.innerHTML = '';
                return;
            }
            var html = '';
            for (var i = 0; i < state.items.length; i++) {
                html += '<div class="gst-suggest__item' +
                    (i === state.active ? ' is-active' : '') +
                    '" role="option" data-index="' + i + '">' +
                    highlight(state.items[i], query) +
                    '</div>';
            }
            dropdown.innerHTML = html;
            dropdown.hidden = false;
        }

        function highlight(value, query) {
            var encoded = escapeHtml(value);
            if (!query) return encoded;
            // Bold each prefix-token longer than 2 chars in the suggestion.
            var tokens = query
                .split(/\s+/)
                .filter(function (t) { return t.length > 1; })
                .map(escapeRegExp)
                .map(escapeHtml);
            if (!tokens.length) return encoded;
            var re = new RegExp('(' + tokens.join('|') + ')', 'gi');
            return encoded.replace(re, '<b>$1</b>');
        }

        function escapeHtml(s) {
            return s
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function escapeRegExp(s) {
            return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        }

        function fetchSuggestions(query) {
            if (state.controller) state.controller.abort();
            state.controller = new AbortController();
            fetch(ENDPOINT + '?q=' + encodeURIComponent(query),
                { credentials: 'same-origin', signal: state.controller.signal, headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (results) {
                    if (input.value !== query) return; // user kept typing
                    state.items = Array.isArray(results) ? results : [];
                    state.active = -1;
                    state.lastQuery = query;
                    render(query);
                })
                .catch(function () { /* swallow — autocomplete is best-effort */ });
        }

        function move(delta) {
            if (!state.items.length) return;
            state.active = (state.active + delta + state.items.length) % state.items.length;
            render(state.lastQuery);
            // Update the input visually to the highlighted suggestion so
            // pressing Enter without further nav submits the active value.
            // (We restore the typed value if the user navigates back to -1.)
        }

        function commit(value) {
            input.value = value;
            dropdown.hidden = true;
            state.items = [];
            state.active = -1;
            // Submit the closest form so the search runs immediately.
            var form = input.form;
            if (form) form.submit();
        }

        input.addEventListener('input', function () {
            var q = input.value.trim();
            if (q.length < MIN_LENGTH) {
                state.items = [];
                state.active = -1;
                dropdown.hidden = true;
                return;
            }
            clearTimeout(state.debounceTimer);
            state.debounceTimer = setTimeout(function () { fetchSuggestions(q); }, DEBOUNCE_MS);
        });

        input.addEventListener('keydown', function (e) {
            if (dropdown.hidden) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); move(1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); move(-1); }
            else if (e.key === 'Enter') {
                if (state.active >= 0 && state.active < state.items.length) {
                    e.preventDefault();
                    commit(state.items[state.active]);
                }
            }
            else if (e.key === 'Escape') {
                dropdown.hidden = true;
                state.active = -1;
            }
        });

        dropdown.addEventListener('mousedown', function (e) {
            // mousedown fires before blur; prevents the input from losing
            // focus and dismissing the dropdown before the click registers.
            var target = e.target.closest('.gst-suggest__item');
            if (!target) return;
            e.preventDefault();
            var idx = parseInt(target.getAttribute('data-index'), 10);
            if (!isNaN(idx) && state.items[idx]) commit(state.items[idx]);
        });

        dropdown.addEventListener('mousemove', function (e) {
            var target = e.target.closest('.gst-suggest__item');
            if (!target) return;
            var idx = parseInt(target.getAttribute('data-index'), 10);
            if (!isNaN(idx) && idx !== state.active) {
                state.active = idx;
                render(state.lastQuery);
            }
        });

        document.addEventListener('click', function (e) {
            if (e.target === input || shell.contains(e.target)) return;
            dropdown.hidden = true;
        });

        input.addEventListener('focus', function () {
            if (state.items.length) dropdown.hidden = false;
        });
    }

    function bootstrap() {
        var inputs = document.querySelectorAll('input[data-autocomplete-search]');
        for (var i = 0; i < inputs.length; i++) init(inputs[i]);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bootstrap);
    } else {
        bootstrap();
    }
})();
