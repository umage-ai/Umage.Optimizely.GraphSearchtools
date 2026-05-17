/**
 * Aurora Synonyms grid — Phase 5 refactor.
 *
 * Replaces the inline-editable grid in synonyms-grid.js with an Aurora-style
 * summary grid + flyout for editing. One row per rule (across every locale).
 *
 * Data model:
 *   • Each Graph synonym slot is a plain-text blob, one rule per line.
 *   • We fetch the Global blob + one blob per known locale, parse line-by-line,
 *     and merge into a single rule list with locale-tagged rows.
 *   • Save reassembles the affected locale's blob and PUTs it back.
 *
 * Locale discovery: the JS asks the Profiles registry for the union of all
 * declared locales. As a fallback (no Profiles), it uses the Graph locales
 * endpoint.
 *
 * Rule format:
 *   • `a, b, c`   → equivalent (every term triggers the others)
 *   • `a => b`    → replacement (LHS triggers, RHS is the replacement)
 */
(function () {
    const API = window.GST_BASE_URL + '/SynonymsApi';
    const COVERAGE_API = window.GST_BASE_URL + '/SynonymCoverageApi/Index';
    const GRAPH_LOCALES_API = window.GST_BASE_URL + '/SitesApi/Locales';
    const PROFILES_API = window.GST_BASE_URL + '/api/profiles';

    const state = {
        initialized: false,
        // Optional scope. When the Profile detail view mounts the grid, it
        // passes `scope.locales` to narrow the rule list to the profile's
        // declared languages (plus the tenant-global pool, which always
        // applies). Top-level synonyms tool leaves this null.
        scope: null,
        locales: ['Global'],  // editable scopes; Global + per-locale
        blobs: {},            // locale → original raw blob ('' if missing)
        rules: [],            // [{ id, raw, type, locale, lineIndex, hits }]
        // Activity (30d) — populated by SynonymCoverage analyzer. Per-rule
        // hit count, or `null` when the analyzer hasn't loaded / failed /
        // the window has zero logs (rendered as "—" rather than libellously
        // "0" against a fresh tenant).
        coverage: { loaded: false, hitsByKey: {} },
        // Default sort by Activity desc — most-fired rules first. Coverage
        // hits land asynchronously; sortRules treats `null` as the lowest
        // value so untagged rows fall to the bottom rather than the top.
        sort: { key: 'activity', dir: 'desc' },
        filters: { q: '', locale: '', type: '' },
        editing: null
    };

    // Auto-init for the top-level Synonyms page. The Profile detail page
    // calls `GST.synonyms.aurora.init({ scope })` from its own JS before
    // DOMContentLoaded fires, so this handler becomes a no-op there.
    document.addEventListener('DOMContentLoaded', function () {
        if (state.initialized) return;
        if (!document.getElementById('gst-syn-aurora-rows')) return;
        init();
    });

    function init(opts) {
        if (state.initialized) return;
        state.initialized = true;
        opts = opts || {};
        state.scope = opts.scope || null;
        const search = document.getElementById('gst-syn-search');
        if (search) search.addEventListener('input', function () {
            state.filters.q = search.value.trim().toLowerCase();
            renderGrid();
        });
        const localeF = document.getElementById('gst-syn-locale-filter');
        if (localeF) localeF.addEventListener('change', function () {
            state.filters.locale = localeF.value;
            renderGrid();
        });
        const typeF = document.getElementById('gst-syn-type-filter');
        if (typeF) typeF.addEventListener('change', function () {
            state.filters.type = typeF.value;
            renderGrid();
        });
        const createBtn = document.getElementById('gst-syn-create');
        if (createBtn) createBtn.addEventListener('click', openCreateFlyout);

        document.querySelectorAll('.gst-syn-aurora-table thead th[data-sort]').forEach(function (th) {
            th.addEventListener('click', function () {
                const key = th.dataset.sort;
                if (state.sort.key === key) state.sort.dir = state.sort.dir === 'asc' ? 'desc' : 'asc';
                else { state.sort.key = key; state.sort.dir = 'asc'; }
                renderGrid();
            });
        });

        document.querySelectorAll('[data-flyout-save="syn"]').forEach(function (b) { b.addEventListener('click', onSave); });
        const ruleInput = document.getElementById('gst-synfly-rule');
        if (ruleInput) ruleInput.addEventListener('input', renderParseHint);
        const deleteBtn = document.getElementById('gst-synfly-delete');
        if (deleteBtn) deleteBtn.addEventListener('click', onDeleteFromFlyout);

        loadAll();
    }

    function onDeleteFromFlyout(e) {
        e.preventDefault();
        if (!state.editing || state.editing.mode !== 'edit' || !state.editing.original) return;
        const id = state.editing.original.id;
        GST.flyout.close('syn');
        deleteRule(id);
    }

    // ── Data loading ───────────────────────────────────────────────────

    function loadAll() {
        const tbody = document.getElementById('gst-syn-aurora-rows');
        renderLoading(tbody);
        discoverLocales()
            .then(function (locales) {
                state.locales = ['Global'].concat(locales);
                return Promise.all(state.locales.map(function (l) {
                    return fetchBlob(l).then(function (text) { return { locale: l, text: text || '' }; });
                }));
            })
            .then(function (bundles) {
                state.blobs = {};
                state.rules = [];
                bundles.forEach(function (b) {
                    state.blobs[b.locale] = b.text;
                    parseBlob(b.locale, b.text).forEach(function (r) { state.rules.push(r); });
                });
                // Populate the Scope dropdown from the parsed rules so it
                // shows only scopes that actually have rules. Runs idempotent —
                // a re-load after save clears + repopulates without dupes.
                populateLocaleFilter();
                // Paint immediately with activity=unknown so the grid doesn't
                // wait on the coverage analyzer (it can be slow on tenants
                // with large logs). Then refresh once coverage lands.
                renderGrid();
                return loadCoverage();
            })
            .then(function () {
                applyCoverage();
                renderGrid();
            })
            .catch(function (err) {
                console.error('Aurora synonyms load failed', err);
                tbody.innerHTML = '<tr><td colspan="4" class="gst-empty"><p>' +
                    GST.escHtml(GST.s('synonyms.request_failed', 'Could not load synonyms.')) +
                    '</p></td></tr>';
            });
    }

    // Pulls SynonymCoverage's 30-day per-rule hit counts and builds a fast
    // lookup keyed by "{language}|{raw rule}". The analyzer tags the no-
    // locale slot as "Global" — same casing we use in state.locales — so no
    // normalisation is needed.
    function loadCoverage() {
        return GST.fetchJson(COVERAGE_API)
            .then(function (r) {
                const hits = {};
                (r && r.ruleActivity ? r.ruleActivity : []).forEach(function (a) {
                    hits[(a.language || '') + '|' + (a.entry || '').trim()] = a.hits || 0;
                });
                state.coverage.hitsByKey = hits;
                state.coverage.loaded = true;
                state.coverage.logsScanned = (r && typeof r.logsScanned === 'number') ? r.logsScanned : 0;
            })
            .catch(function () {
                state.coverage.loaded = false;
                state.coverage.hitsByKey = {};
            });
    }

    function applyCoverage() {
        const noLogs = state.coverage.loaded && (state.coverage.logsScanned || 0) === 0;
        state.rules.forEach(function (r) {
            // Without coverage data (analyzer failed, or no logs in window)
            // surface "—" rather than 0 so a fresh tenant doesn't libel
            // every rule as inactive.
            if (!state.coverage.loaded || noLogs) { r.hits = null; return; }
            const key = r.locale + '|' + r.raw;
            const h = state.coverage.hitsByKey[key];
            r.hits = (typeof h === 'number') ? h : 0;
        });
    }

    function discoverLocales() {
        // Profile-scoped: the active profile declares exactly which locales
        // it cares about. Use that list verbatim so the editable scopes
        // match the profile's surface — fewer empty rule blobs to fetch and
        // a tighter Scope filter dropdown.
        if (state.scope && Array.isArray(state.scope.locales) && state.scope.locales.length) {
            return Promise.resolve(state.scope.locales.slice());
        }
        // Top-level synonyms tool: prefer the Profiles registry's union of
        // declared locales; fall back to Graph's introspection endpoint,
        // then to a hardcoded shortlist.
        return GST.fetchJson(PROFILES_API)
            .then(function (profiles) {
                const seen = {};
                (profiles || []).forEach(function (p) {
                    (p.locales || []).forEach(function (l) { if (l) seen[l] = true; });
                });
                const arr = Object.keys(seen).sort();
                return arr.length ? arr : null;
            })
            .catch(function () { return null; })
            .then(function (fromProfiles) {
                if (fromProfiles && fromProfiles.length) return fromProfiles;
                return GST.fetchJson(GRAPH_LOCALES_API).catch(function () { return ['en']; });
            });
    }

    function fetchBlob(locale) {
        const q = locale === 'Global' ? '' : '?languageRouting=' + encodeURIComponent(locale);
        return GST.fetchJson(API + '/Get' + q)
            .then(function (resp) { return (resp && resp.content) || ''; })
            .catch(function () { return ''; });
    }

    function parseBlob(locale, text) {
        if (!text) return [];
        // Optimizely Graph returns the synonym body as a JSON-quoted string
        // for some tenants (leading `"`, escaped `\n` between rules) and as a
        // plain newline-separated blob for others. Match synonyms-grid.js's
        // defensive normalization: if it looks JSON-encoded, unwrap it.
        if (text.charAt(0) === '"' && text.charAt(text.length - 1) === '"') {
            try { text = JSON.parse(text); } catch (_) { /* leave as-is */ }
        }
        const lines = text.split(/\r?\n/);
        const out = [];
        lines.forEach(function (line, idx) {
            const trimmed = line.trim();
            if (!trimmed) return;
            const type = trimmed.indexOf('=>') >= 0 ? 'replacement' : 'equivalent';
            out.push({
                id: locale + ':' + idx + ':' + hash(trimmed),
                raw: trimmed,
                type: type,
                locale: locale,
                lineIndex: idx
            });
        });
        return out;
    }

    function hash(s) {
        let h = 0;
        for (let i = 0; i < s.length; i++) { h = (h * 31 + s.charCodeAt(i)) | 0; }
        return h;
    }

    function populateLocaleFilter() {
        const sel = document.getElementById('gst-syn-locale-filter');
        if (!sel) return;
        // Derive scopes from the rules we actually parsed rather than the
        // upstream locale discovery: avoids surfacing scopes that have no
        // rules (e.g. Graph's synthetic `NEUTRAL`/`ALL` enums, or profile-
        // declared locales whose synonym blob doesn't exist yet), and keeps
        // the option set in sync after a reload that adds or removes a scope.
        const seen = {};
        const scopes = [];
        state.rules.forEach(function (r) {
            if (r.locale && !seen[r.locale]) { seen[r.locale] = true; scopes.push(r.locale); }
        });
        scopes.sort(function (a, b) {
            // Pin "Global" to the top — it's the tenant-wide bucket and
            // marketers usually scan it first.
            if (a === 'Global') return -1;
            if (b === 'Global') return 1;
            return a.localeCompare(b);
        });
        // Keep the current selection if it's still represented; otherwise
        // fall back to "All" so the chip-display doesn't go blank.
        const current = sel.value;
        // Clear all options except the static "All" one (data attribute marks
        // it; if absent, leave the first option as-is).
        const all = sel.querySelector('option[value=""]');
        sel.innerHTML = '';
        if (all) sel.appendChild(all);
        scopes.forEach(function (l) {
            const opt = document.createElement('option');
            opt.value = l;
            opt.textContent = l;
            sel.appendChild(opt);
        });
        sel.value = (current && seen[current]) ? current : '';
    }

    // ── Grid render ────────────────────────────────────────────────────

    function renderLoading(tbody) {
        const msg = GST.s('shared.loading', 'Loading…');
        tbody.innerHTML = '<tr><td colspan="4" class="gst-muted">' + GST.escHtml(msg) + '</td></tr>';
    }

    function renderGrid() {
        const tbody = document.getElementById('gst-syn-aurora-rows');
        if (!tbody) return;

        const filtered = state.rules.filter(function (r) {
            if (state.filters.locale && r.locale !== state.filters.locale) return false;
            if (state.filters.q && r.raw.toLowerCase().indexOf(state.filters.q) === -1) return false;
            return true;
        });

        const sorted = sortRules(filtered);
        if (sorted.length === 0) {
            tbody.innerHTML = '<tr><td colspan="4" class="gst-empty"><p>' +
                GST.escHtml(GST.s('profiles.detail.synonyms.emptyHeadline', 'No rules yet — add one to start.')) +
                '</p></td></tr>';
            return;
        }

        const deleteLabel = GST.s('shared.delete', 'Delete');
        const trash = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" aria-hidden="true">' +
            '<path d="M3 4 H13 M5 4 V13 a1 1 0 0 0 1 1 H10 a1 1 0 0 0 1 -1 V4 M6 4 V2 a1 1 0 0 1 1 -1 H9 a1 1 0 0 1 1 1 V4 M6.5 7 V11 M9.5 7 V11"/>' +
            '</svg>';
        tbody.innerHTML = sorted.map(function (r) {
            return '<tr class="is-selectable" data-rule-id="' + GST.escHtml(r.id) + '">' +
                '<td><a href="#" class="gst-table__link" data-row-link>' + GST.escHtml(r.raw) + '</a></td>' +
                '<td>' + GST.escHtml(r.locale) + '</td>' +
                '<td class="num">' + renderActivityCell(r.hits) + '</td>' +
                '<td class="gst-table__actions">' +
                '<button class="gst-rowdelete" data-row-delete title="' + GST.escHtml(deleteLabel) + '" aria-label="' + GST.escHtml(deleteLabel) + '">' + trash + '</button>' +
                '</td></tr>';
        }).join('');

        tbody.querySelectorAll('tr.is-selectable').forEach(function (tr) {
            tr.addEventListener('click', function (e) {
                if (e.target.closest('a, button')) return;
                openEditFlyout(tr.dataset.ruleId);
            });
            const link = tr.querySelector('[data-row-link]');
            if (link) link.addEventListener('click', function (e) {
                e.preventDefault();
                openEditFlyout(tr.dataset.ruleId);
            });
            const deleteBtn = tr.querySelector('[data-row-delete]');
            if (deleteBtn) deleteBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                deleteRule(tr.dataset.ruleId);
            });
        });
    }

    // Delete the rule from its locale's blob and PUT the reduced blob back.
    // One-click — no confirmation. On success, splice the rule out of local
    // state and repaint instead of reloading the whole grid; the scope filter
    // is rebuilt in case the deleted rule was the last one in its scope.
    function deleteRule(ruleId) {
        const r = state.rules.find(function (x) { return x.id === ruleId; });
        if (!r) return;
        const remainingRules = state.rules.filter(function (x) {
            return x.locale === r.locale && x.id !== r.id;
        });
        const blobBody = remainingRules.map(function (x) { return x.raw; }).join('\n');
        saveBlob(r.locale, blobBody)
            .then(function () {
                state.rules = state.rules.filter(function (x) { return x.id !== r.id; });
                state.blobs[r.locale] = blobBody;
                populateLocaleFilter();
                renderGrid();
            })
            .catch(function (err) {
                console.error('Delete synonym failed', err);
                window.alert(GST.s('synonyms.request_failed', 'Could not delete rule.'));
            });
    }

    function sortRules(arr) {
        const k = state.sort.key, dir = state.sort.dir === 'asc' ? 1 : -1;
        return arr.slice().sort(function (a, b) {
            let va, vb;
            switch (k) {
                case 'rule':     va = a.raw.toLowerCase(); vb = b.raw.toLowerCase(); break;
                case 'type':     va = a.type; vb = b.type; break;
                case 'locale':   va = a.locale; vb = b.locale; break;
                // Activity sort: numeric. Coverage-not-loaded rules (hits ===
                // null) sort lowest so they don't muddy a "most active first"
                // descending scan; ascending puts them after 0-hit rules.
                case 'activity':
                    va = (typeof a.hits === 'number') ? a.hits : -1;
                    vb = (typeof b.hits === 'number') ? b.hits : -1;
                    break;
                default:         va = a.raw.toLowerCase(); vb = b.raw.toLowerCase(); break;
            }
            if (va < vb) return -1 * dir;
            if (va > vb) return 1 * dir;
            return 0;
        });
    }

    function renderActivityCell(hits) {
        if (typeof hits !== 'number') {
            return '<span class="gst-muted">' +
                GST.escHtml(GST.s('synonyms.activity_unknown', '—')) +
                '</span>';
        }
        if (hits === 0) {
            // Mute the zero so an inactive rule reads as "needs attention"
            // without screaming colour at every fresh-tenant row.
            return '<span class="gst-muted">0</span>';
        }
        return '<strong>' + hits.toLocaleString() + '</strong>';
    }

    // ── Flyout — open / populate ───────────────────────────────────────

    function openEditFlyout(ruleId) {
        const r = state.rules.find(function (x) { return x.id === ruleId; });
        if (!r) return;
        state.editing = { mode: 'edit', original: r };
        populateFlyout(r);
        GST.flyout.open('syn');
    }

    function openCreateFlyout() {
        state.editing = { mode: 'create', original: null };
        populateFlyout({ raw: '', type: 'equivalent', locale: state.filters.locale || 'Global' });
        GST.flyout.open('syn');
    }

    function populateFlyout(r) {
        document.getElementById('gst-flyout-syn-title').textContent =
            state.editing.mode === 'create'
                ? GST.s('synFlyout.createTitle', 'Create synonym rule')
                : GST.s('synFlyout.editTitle', 'Edit synonym rule');
        document.getElementById('gst-synfly-rule').value = r.raw || '';
        // Locale dropdown — all known scopes.
        const localeSel = document.getElementById('gst-synfly-locale');
        localeSel.innerHTML = state.locales.map(function (l) {
            return '<option value="' + GST.escHtml(l) + '"' + (l === r.locale ? ' selected' : '') + '>' + GST.escHtml(l) + '</option>';
        }).join('');
        // Delete only makes sense when editing an existing rule.
        const deleteBtn = document.getElementById('gst-synfly-delete');
        if (deleteBtn) deleteBtn.hidden = state.editing.mode !== 'edit';
        renderParseHint();
    }

    function renderParseHint() {
        const input = document.getElementById('gst-synfly-rule');
        const hint = document.getElementById('gst-synfly-parse');
        if (!input || !hint) return;
        const raw = (input.value || '').trim();
        if (!raw) { hint.textContent = ''; return; }
        if (raw.indexOf('=>') >= 0) {
            const parts = raw.split('=>').map(function (p) { return p.trim(); });
            if (!parts[0] || !parts[1]) {
                hint.textContent = 'Invalid: replacement needs both sides of "=>".';
            } else {
                hint.textContent = 'Replacement: ' + parts[0] + ' → ' + parts[1];
            }
        } else {
            const terms = raw.split(',').map(function (t) { return t.trim(); }).filter(function (t) { return t; });
            hint.textContent = terms.length < 2
                ? 'Invalid: equivalent needs at least two comma-separated terms.'
                : 'Equivalent: ' + terms.length + ' terms';
        }
    }

    // ── Save ───────────────────────────────────────────────────────────

    function onSave(e) {
        e.preventDefault();
        const btn = e.currentTarget;
        const raw = (document.getElementById('gst-synfly-rule').value || '').trim();
        const locale = (document.getElementById('gst-synfly-locale').value || 'Global');
        if (!raw) return;
        const type = raw.indexOf('=>') >= 0 ? 'replacement' : 'equivalent';
        if (type === 'replacement') {
            const parts = raw.split('=>').map(function (p) { return p.trim(); });
            if (!parts[0] || !parts[1]) {
                window.alert('Replacement needs both sides of "=>".');
                return;
            }
        }

        // Reassemble the affected locale's blob with the edit applied.
        // When the user moved a rule to a different locale, write both the
        // source (rule removed) and the target (rule appended).
        const affectedLocales = {};
        if (state.editing.mode === 'edit') {
            affectedLocales[state.editing.original.locale] = true;
        }
        affectedLocales[locale] = true;

        btn.disabled = true;
        const origLabel = btn.textContent;
        btn.textContent = GST.s('shared.saving', 'Saving…');

        // Compute new rules list locally, then derive each affected blob.
        const next = state.rules.slice();
        if (state.editing.mode === 'edit') {
            const idx = next.findIndex(function (r) { return r.id === state.editing.original.id; });
            if (idx >= 0) next.splice(idx, 1);
        }
        next.push({
            id: locale + ':new:' + hash(raw),
            raw: raw,
            type: type,
            locale: locale,
            lineIndex: 999999
        });

        const ops = Object.keys(affectedLocales).map(function (l) {
            const lines = next.filter(function (r) { return r.locale === l; }).map(function (r) { return r.raw; });
            return { locale: l, body: lines.join('\n') };
        });

        let errors = 0;
        let chain = Promise.resolve();
        ops.forEach(function (op) {
            chain = chain.then(function () {
                return saveBlob(op.locale, op.body).catch(function (err) {
                    errors++;
                    console.error('Synonym save failed', op, err);
                });
            });
        });
        chain.then(function () {
            btn.disabled = false;
            btn.textContent = origLabel;
            if (errors > 0) {
                window.alert(GST.s('synonyms.request_failed', 'Could not save synonym rule.'));
            } else {
                GST.flyout.close('syn');
                loadAll();
            }
        });
    }

    function saveBlob(locale, body) {
        // Synonyms PUT expects text/plain. Build the URL with the routing query
        // when not the Global scope.
        const q = locale === 'Global' ? '' : '?languageRouting=' + encodeURIComponent(locale);
        return fetch(API + '/Update' + q, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            body: JSON.stringify({ content: body, languageRouting: locale === 'Global' ? null : locale })
        }).then(function (r) {
            if (!r.ok) throw new Error('Update failed ' + r.status);
        });
    }

    // Expose `init` so the Profile detail view can mount a scoped instance
    // before DOMContentLoaded fires.
    window.GST = window.GST || {};
    window.GST.synonyms = window.GST.synonyms || {};
    window.GST.synonyms.aurora = { init: init };
})();
