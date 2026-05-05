/**
 * Graph Search Tools — Synonyms editor.
 *
 * One synonym blob per language plus a "Global" blob (no language). The list
 * format is one rule per line; replacement vs equivalent semantics are
 * encoded in the rule text by Optimizely Graph itself.
 *
 * Phase 2.5 §4.2: the same editor mounts into two contexts.
 *   - Top-level Synonyms page — scope switcher (Global ⇄ Per profile) above.
 *   - Profile detail Synonyms tab — scope is fixed to the current profile.
 *
 * Public surface:
 *   GST.synonyms.editor({ container, scope, language?, profileKey?, locale? })
 *      → returns { destroy, reload }; mounts into `container`.
 *
 *   GST.synonyms.detailMount({ container, profileKey, locales })
 *      → convenience wrapper for the profile detail tab; renders a locale
 *        picker and re-mounts the editor when the locale changes.
 *
 * Top-level page wires its own scope switcher + URL query string state and
 * calls `editor()` itself.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.synonyms) || {};
    var SYN_SLOT = 'one';
    var SYN_SOURCE = '';

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = STRINGS.request_failed || 'Request failed';
                    try {
                        var parsed = t ? JSON.parse(t) : null;
                        if (parsed && parsed.message) msg = parsed.message;
                    } catch (_) { /* not JSON */ }
                    throw new Error(msg + ' (' + resp.status + ')');
                });
            }
            if (resp.status === 204) return null;
            return resp.json();
        });
    }

    /**
     * Build the per-language editor inside the supplied container. Returns an
     * API object: `{ reload(opts), destroy() }`.
     *
     * opts:
     *   container  — DOM node to render into (required).
     *   scope      — 'global' | 'profile' (required).
     *   language   — locale code; only meaningful for 'global'. Empty/undefined
     *                means "global synonyms (no language)".
     *   profileKey — profile key; required for 'profile'.
     *   locale     — BCP-47 locale; required for 'profile' (the synonym slot
     *                is per-language even within a profile).
     *   onAlert    — optional (message, isError) callback for status messages.
     */
    function editor(opts) {
        opts = opts || {};
        if (!opts.container) throw new Error('synonyms.editor: container is required');
        var container = opts.container;
        var scope = opts.scope || 'global';
        var language = opts.language || '';
        var profileKey = opts.profileKey || '';
        var locale = opts.locale || '';
        var onAlert = typeof opts.onAlert === 'function' ? opts.onAlert : null;

        // Render the inner DOM. We rebuild on every reload so the editor stays
        // stateless w.r.t. its container — caller can swap us between scopes.
        var dom = renderEditorDom(container);
        var rows = [];
        var dirty = false;

        function setAlert(msg, isError) {
            if (onAlert) {
                onAlert(msg, isError);
                return;
            }
            // Inline alert fallback when the host page doesn't provide one.
            if (!msg) {
                dom.alert.hidden = true;
                dom.alert.textContent = '';
                dom.alert.classList.remove('gst-alert--danger');
                return;
            }
            dom.alert.hidden = false;
            dom.alert.textContent = msg;
            dom.alert.classList.toggle('gst-alert--danger', !!isError);
        }

        function updateSaveButton() { dom.save.disabled = !dirty; }

        function buildScopeQuery() {
            var qs = [];
            if (scope === 'profile') {
                qs.push('profileKey=' + encodeURIComponent(profileKey));
                if (locale) qs.push('languageRouting=' + encodeURIComponent(locale));
            } else {
                if (language) qs.push('languageRouting=' + encodeURIComponent(language));
                qs.push('slot=' + encodeURIComponent(SYN_SLOT));
            }
            return qs.length ? '?' + qs.join('&') : '';
        }

        function buildScopeBody(content) {
            if (scope === 'profile') {
                return {
                    content: content,
                    languageRouting: locale || null,
                    sourceRouting: SYN_SOURCE || null
                };
            }
            return {
                content: content,
                languageRouting: language || null,
                sourceRouting: SYN_SOURCE || null,
                slot: SYN_SLOT
            };
        }

        function load() {
            rows = [];
            dirty = false;
            updateSaveButton();
            var qs = buildScopeQuery();
            return ajax(BASE + '/SynonymsApi/Get' + qs)
                .then(function (result) {
                    var content = result ? result.content : '';
                    if (content) {
                        if (content.charAt(0) === '"' && content.charAt(content.length - 1) === '"') {
                            try { content = JSON.parse(content); } catch (_) { /* leave as-is */ }
                        }
                        content.split(/\r?\n/).forEach(function (line) {
                            var rule = line.trim();
                            if (!rule) return;
                            rows.push({ rule: rule, dirty: false, isNew: false });
                        });
                    }
                    renderGrid();
                })
                .catch(function () {
                    // No synonyms is fine — render empty grid.
                    renderGrid();
                });
        }

        function getFilteredRows() {
            var text = dom.filter.value.trim().toLowerCase();
            var filtered = rows;
            if (text) filtered = filtered.filter(function (r) { return r.rule.toLowerCase().indexOf(text) !== -1; });
            return filtered.filter(function (r) { return r.isNew || (r.rule && r.rule.trim().length > 0); });
        }

        function renderGrid() {
            dom.grid.innerHTML = '';
            var filtered = getFilteredRows();
            filtered.forEach(function (row) { dom.grid.appendChild(buildRow(row)); });
        }

        function buildRow(row) {
            var tr = document.createElement('tr');
            tr.className = 'gst-syn-row' + (row.dirty ? ' is-dirty' : '') + (row.isNew ? ' is-new' : '');

            var ruleCell = document.createElement('td');
            var input = document.createElement('input');
            input.type = 'text';
            input.className = 'gst-cell-input';
            input.value = row.rule;
            input.placeholder = STRINGS.rule_placeholder || 'H2O => water  or  laptop, computer, pc';
            input.addEventListener('input', function () {
                row.rule = input.value;
                row.dirty = true;
                dirty = true;
                tr.classList.add('is-dirty');
                updateSaveButton();
            });
            ruleCell.appendChild(input);
            tr.appendChild(ruleCell);

            var actCell = document.createElement('td');
            actCell.className = 'gst-syn-actions';
            var del = document.createElement('button');
            del.type = 'button';
            del.className = 'gst-pin-delete-btn';
            del.innerHTML = '&#x2716;';
            del.title = STRINGS.action_remove || 'Remove';
            del.addEventListener('click', function () {
                rows = rows.filter(function (r) { return r !== row; });
                dirty = true;
                updateSaveButton();
                renderGrid();
            });
            actCell.appendChild(del);
            tr.appendChild(actCell);
            return tr;
        }

        function addRow() {
            rows.push({ rule: '', dirty: true, isNew: true });
            dirty = true;
            updateSaveButton();
            renderGrid();
            var trs = dom.grid.querySelectorAll('tr');
            var last = trs[trs.length - 1];
            if (last) {
                last.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                var input = last.querySelector('.gst-cell-input');
                if (input && input.focus) input.focus();
            }
        }

        function save() {
            var rules = rows.map(function (r) { return r.rule.trim(); }).filter(function (r) { return r; });
            var content = rules.join('\n');
            var promise;
            if (content) {
                promise = ajax(BASE + '/SynonymsApi/Update' + (scope === 'profile' ? '?profileKey=' + encodeURIComponent(profileKey) : ''), {
                    method: 'PUT',
                    body: buildScopeBody(content)
                });
            } else {
                promise = ajax(BASE + '/SynonymsApi/Delete' + buildScopeQuery(), { method: 'DELETE' });
            }
            promise.then(function () {
                dirty = false;
                rows = rows.filter(function (r) { return r.rule.trim(); });
                rows.forEach(function (r) { r.dirty = false; r.isNew = false; });
                updateSaveButton();
                renderGrid();
                setAlert(STRINGS.saved || 'Synonyms saved.');
            }).catch(function (err) { setAlert(err.message, true); });
        }

        dom.add.addEventListener('click', addRow);
        dom.save.addEventListener('click', save);
        dom.filter.addEventListener('input', renderGrid);

        var unloadHandler = function (e) { if (dirty) e.preventDefault(); };
        window.addEventListener('beforeunload', unloadHandler);

        updateSaveButton();
        load();

        return {
            reload: function (next) {
                next = next || {};
                if (next.scope) scope = next.scope;
                if ('language' in next) language = next.language || '';
                if ('profileKey' in next) profileKey = next.profileKey || '';
                if ('locale' in next) locale = next.locale || '';
                return load();
            },
            destroy: function () {
                window.removeEventListener('beforeunload', unloadHandler);
                container.innerHTML = '';
            },
            isDirty: function () { return dirty; }
        };
    }

    /**
     * Build the editor DOM (filter + grid + footer + save button) inside
     * `container`, returning references to the new nodes.
     */
    function renderEditorDom(container) {
        container.innerHTML = '';

        var alert = document.createElement('div');
        alert.className = 'gst-alert';
        alert.hidden = true;
        container.appendChild(alert);

        var toolbar = document.createElement('div');
        toolbar.className = 'gst-toolbar';
        var filter = document.createElement('input');
        filter.type = 'text';
        filter.placeholder = STRINGS.filter_placeholder || 'Filter rules...';
        toolbar.appendChild(filter);
        var spacer = document.createElement('div');
        spacer.className = 'gst-toolbar__spacer';
        toolbar.appendChild(spacer);
        var add = document.createElement('button');
        add.type = 'button';
        add.className = 'gst-btn gst-btn--primary';
        add.textContent = '+ ' + (STRINGS.add || 'Add');
        toolbar.appendChild(add);
        container.appendChild(toolbar);

        var table = document.createElement('table');
        table.className = 'gst-table';
        var thead = document.createElement('thead');
        thead.innerHTML = '<tr><th>' + escHtml(STRINGS.col_rule || 'Rule') + '</th><th class="gst-syn-actions-col"></th></tr>';
        table.appendChild(thead);
        var grid = document.createElement('tbody');
        table.appendChild(grid);
        container.appendChild(table);

        var footer = document.createElement('div');
        footer.className = 'gst-syn-footer';
        var help = document.createElement('div');
        help.className = 'gst-muted gst-syn-footer__help';
        help.innerHTML =
            '<strong>' + escHtml(STRINGS.help_replacement_label || 'Replacement') + '</strong> '
            + '<code>H2O =&gt; water</code> &mdash; '
            + escHtml(STRINGS.help_replacement_text || 'searching "H2O" also returns results for "water", but not the reverse.')
            + '<br/>'
            + '<strong>' + escHtml(STRINGS.help_equivalent_label || 'Equivalent') + '</strong> '
            + '<code>laptop, computer, pc</code> &mdash; '
            + escHtml(STRINGS.help_equivalent_text || 'searching any of these terms returns results for all three.');
        footer.appendChild(help);
        var actions = document.createElement('div');
        actions.className = 'gst-actions';
        var save = document.createElement('button');
        save.type = 'button';
        save.className = 'gst-btn gst-btn--primary';
        save.textContent = STRINGS.save || 'Save changes';
        actions.appendChild(save);
        footer.appendChild(actions);
        container.appendChild(footer);

        return { alert: alert, filter: filter, add: add, grid: grid, save: save };
    }

    function escHtml(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    /**
     * Convenience wrapper for the profile detail's Synonyms tab. Renders a
     * locale picker (constrained to the profile's locales) plus the editor.
     */
    function detailMount(opts) {
        opts = opts || {};
        var container = opts.container;
        var profileKey = opts.profileKey;
        var locales = (opts.locales && opts.locales.length) ? opts.locales : [''];
        if (!container || !profileKey) return null;

        container.innerHTML = '';

        var picker = document.createElement('div');
        picker.className = 'gst-toolbar';
        picker.style.marginBottom = 'var(--gst-space-md, 12px)';
        var localeLabel = document.createElement('label');
        localeLabel.className = 'gst-muted';
        localeLabel.style.marginRight = '8px';
        localeLabel.textContent = STRINGS.profile_locale || 'Locale';
        picker.appendChild(localeLabel);
        var localeSel = document.createElement('select');
        locales.forEach(function (loc) {
            var opt = document.createElement('option');
            opt.value = loc;
            opt.textContent = loc || (STRINGS.global || 'Global');
            localeSel.appendChild(opt);
        });
        picker.appendChild(localeSel);
        container.appendChild(picker);

        var editorHost = document.createElement('div');
        container.appendChild(editorHost);

        var instance = editor({
            container: editorHost,
            scope: 'profile',
            profileKey: profileKey,
            locale: locales[0]
        });

        localeSel.addEventListener('change', function () {
            instance.reload({ locale: localeSel.value });
        });

        return instance;
    }

    /**
     * Top-level Synonyms page bootstrap.
     *
     * Scope switcher (Global ⇄ Per profile) drives the URL query string so
     * editors can deep-link. Profile dropdown is fed from /api/profiles and
     * filtered to those with a SynonymSlot configured.
     */
    function pageInit(opts) {
        opts = opts || {};
        var apiBase = opts.profilesApi || '/EPiServer/cms/graphsearchtools/api/profiles';

        var alertBox = document.getElementById('gst-alert');
        var scopeBar = document.getElementById('gst-syn-scope');
        var scopeGlobalBtn = document.getElementById('gst-syn-scope-global');
        var scopeProfileBtn = document.getElementById('gst-syn-scope-profile');
        var globalControls = document.getElementById('gst-syn-global-controls');
        var profileControls = document.getElementById('gst-syn-profile-controls');
        var globalLanguageSel = document.getElementById('gst-syn-global-language');
        var profileSel = document.getElementById('gst-syn-profile');
        var profileLocaleSel = document.getElementById('gst-syn-profile-locale');
        var profileWarning = document.getElementById('gst-syn-profile-no-slot');
        var globalWarning = document.getElementById('gst-syn-global-warning');
        var editorHost = document.getElementById('gst-syn-editor-host');

        if (!editorHost) return;

        var profiles = [];
        var instance = null;

        function setAlert(msg, isError) {
            if (!alertBox) return;
            if (!msg) { alertBox.hidden = true; alertBox.textContent = ''; alertBox.classList.remove('gst-alert--danger'); return; }
            alertBox.hidden = false;
            alertBox.textContent = msg;
            alertBox.classList.toggle('gst-alert--danger', !!isError);
        }

        function readUrlState() {
            var params = new URLSearchParams(window.location.search || '');
            return {
                scope: params.get('scope') === 'profile' ? 'profile' : 'global',
                profileKey: params.get('key') || '',
                locale: params.get('locale') || '',
                language: params.get('language') || ''
            };
        }

        function writeUrlState(state) {
            var params = new URLSearchParams();
            params.set('scope', state.scope);
            if (state.scope === 'profile') {
                if (state.profileKey) params.set('key', state.profileKey);
                if (state.locale) params.set('locale', state.locale);
            } else if (state.language) {
                params.set('language', state.language);
            }
            var qs = params.toString();
            var url = window.location.pathname + (qs ? '?' + qs : '');
            if (url !== window.location.pathname + window.location.search) {
                window.history.replaceState(null, '', url);
            }
        }

        function loadProfiles() {
            return fetch(apiBase, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.ok ? r.json() : []; })
                .catch(function () { return []; })
                .then(function (rows) {
                    profiles = (rows || []).filter(function (p) { return p.synonymSlot; });
                    return profiles;
                });
        }

        function loadGraphLocales() {
            return ajax(BASE + '/SitesApi/Locales')
                .then(function (s) { return s || []; })
                .catch(function () { return []; });
        }

        function fillSelect(sel, items, includeGlobal) {
            if (!sel) return;
            sel.innerHTML = '';
            if (includeGlobal) {
                var optG = document.createElement('option');
                optG.value = '';
                optG.textContent = STRINGS.global || 'Global';
                sel.appendChild(optG);
            }
            (items || []).forEach(function (item) {
                var opt = document.createElement('option');
                if (typeof item === 'string') { opt.value = item; opt.textContent = item; }
                else { opt.value = item.value; opt.textContent = item.label; }
                sel.appendChild(opt);
            });
        }

        function renderProfileLocales() {
            var p = profiles.find(function (x) { return x.key === profileSel.value; });
            var locales = (p && p.locales && p.locales.length) ? p.locales : [''];
            fillSelect(profileLocaleSel, locales, false);
        }

        function setActiveScope(scope) {
            if (scopeGlobalBtn)  scopeGlobalBtn.classList.toggle('is-active', scope === 'global');
            if (scopeProfileBtn) scopeProfileBtn.classList.toggle('is-active', scope === 'profile');
            if (globalControls)  globalControls.hidden = scope !== 'global';
            if (profileControls) profileControls.hidden = scope !== 'profile';
            if (globalWarning)   globalWarning.hidden = scope !== 'global';
        }

        function mountForState(state) {
            setActiveScope(state.scope);

            if (state.scope === 'profile') {
                if (!profiles.length) {
                    if (profileWarning) profileWarning.hidden = false;
                    if (editorHost) editorHost.innerHTML = '';
                    return;
                }
                if (profileWarning) profileWarning.hidden = true;
                // If state.profileKey is unknown / missing, fall back to the first.
                var p = profiles.find(function (x) { return x.key === state.profileKey; }) || profiles[0];
                profileSel.value = p.key;
                renderProfileLocales();
                var locale = state.locale || (p.locales && p.locales[0]) || '';
                if (locale && Array.from(profileLocaleSel.options).some(function (o) { return o.value === locale; })) {
                    profileLocaleSel.value = locale;
                } else if (profileLocaleSel.options.length) {
                    profileLocaleSel.value = profileLocaleSel.options[0].value;
                }
                if (instance) instance.destroy();
                instance = editor({
                    container: editorHost,
                    scope: 'profile',
                    profileKey: p.key,
                    locale: profileLocaleSel.value,
                    onAlert: setAlert
                });
                writeUrlState({ scope: 'profile', profileKey: p.key, locale: profileLocaleSel.value });
            } else {
                if (state.language && globalLanguageSel) {
                    globalLanguageSel.value = state.language;
                }
                var lang = globalLanguageSel ? globalLanguageSel.value : '';
                if (instance) instance.destroy();
                instance = editor({
                    container: editorHost,
                    scope: 'global',
                    language: lang,
                    onAlert: setAlert
                });
                writeUrlState({ scope: 'global', language: lang });
            }
        }

        // Wire scope switcher.
        if (scopeGlobalBtn) {
            scopeGlobalBtn.addEventListener('click', function () { mountForState({ scope: 'global' }); });
        }
        if (scopeProfileBtn) {
            scopeProfileBtn.addEventListener('click', function () { mountForState({ scope: 'profile' }); });
        }
        if (globalLanguageSel) {
            globalLanguageSel.addEventListener('change', function () {
                mountForState({ scope: 'global', language: globalLanguageSel.value });
            });
        }
        if (profileSel) {
            profileSel.addEventListener('change', function () {
                renderProfileLocales();
                mountForState({ scope: 'profile', profileKey: profileSel.value, locale: profileLocaleSel.value });
            });
        }
        if (profileLocaleSel) {
            profileLocaleSel.addEventListener('change', function () {
                mountForState({ scope: 'profile', profileKey: profileSel.value, locale: profileLocaleSel.value });
            });
        }

        var initialState = readUrlState();
        Promise.all([loadProfiles(), loadGraphLocales()]).then(function (results) {
            var locales = results[1];
            fillSelect(globalLanguageSel, locales, true);
            fillSelect(profileSel, profiles.map(function (p) {
                return { value: p.key, label: p.displayName || p.key };
            }), false);

            // If they asked for a profile scope but none have synonyms, fall back.
            if (initialState.scope === 'profile' && !profiles.length) {
                initialState.scope = 'global';
            }
            mountForState(initialState);
        });
    }

    window.GST = window.GST || {};
    window.GST.synonyms = {
        editor: editor,
        detailMount: detailMount,
        pageInit: pageInit
    };
})();
