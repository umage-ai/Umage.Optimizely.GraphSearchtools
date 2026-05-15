/**
 * Aurora Pinned grid — Phase 4 refactor.
 *
 * Replaces the inline-editable grid in pinned.js with an Optimizely-Aurora
 * pattern: one row per (phrase, collection, locale) group + a right-edge
 * flyout for editing. Reads from the Phase 2 bulk walker so the grid is
 * driven by the full collection rather than a single 20-item page.
 *
 * Data flow:
 *   1. GET /PinnedApi/Collections     → list of pinned collections
 *   2. GET /PinnedApi/AllItems        → for each collection, walked items
 *   3. POST /ContentLookupApi/Resolve → resolve GUID target keys → names
 *   4. aggregate(items) → groups keyed by phrase|collection|locale
 *
 * Save flow (per group):
 *   diff(originalTargets, newTargets) → N CRUD calls in priority order:
 *     • removed → DELETE /PinnedApi/DeleteItem
 *     • added   → POST   /PinnedApi/CreateItem
 *     • shared  → PUT    /PinnedApi/UpdateItem (priority + active)
 *
 * Conflict check on save: scan groups across all collections for the same
 * phrase+locale; warn if found in another collection (cross-profile overlap).
 */
(function () {
    const API = window.GST_BASE_URL + '/PinnedApi';
    const LOOKUP_API = window.GST_BASE_URL + '/ContentLookupApi';
    const INSIGHTS_API = window.GST_BASE_URL + '/InsightsApi';
    const PINNED_STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinned) || {};
    // Cross-profile editor — writes have no per-profile context, so attribute
    // them to the synthesised Generic profile. PinnedApi.ResolveScope 400's
    // any write that omits profileKey once a real profile is registered. The
    // Profile detail view replaces this with the active profile's key via
    // `scope.profileKey`; see `scopeQs()`.
    const DEFAULT_SCOPE_QS = '&profileKey=generic';

    function scopeQs() {
        if (state.scope && state.scope.profileKey) {
            return '&profileKey=' + encodeURIComponent(state.scope.profileKey);
        }
        return DEFAULT_SCOPE_QS;
    }

    // 30-day window for the Activity column. Matches the SynonymCoverage
    // default so a marketer scanning both grids sees the same dataset.
    const ACTIVITY_DAYS = 30;
    // Long-tail head pulled from the telemetry reader. Pinned phrases tend
    // to be brand / category terms that live well above the noise floor,
    // so 5000 is generous; tenants with no logged traffic just see "—".
    const ACTIVITY_TAKE = 5000;

    // ── Page state ─────────────────────────────────────────────────────
    const state = {
        initialized: false,
        // Optional profile-scoping: when set, the grid limits itself to the
        // profile's collection (`scope.collectionId`) and locales
        // (`scope.locales`), and skips the unscoped collection filter. The
        // Profile detail page wires this; the top-level Pinned page leaves
        // it null. `scope.profileKey` is appended to write URLs so the
        // PinnedApi can resolve the profile context for auditing.
        scope: null,
        collections: [],
        items: [],          // flat list of every pinned item, normalised
        groups: [],         // aggregated rows
        targetNames: {},    // guidLower → name
        // Activity (30d) — populated by InsightsApi.TopPhrases. Per-group
        // hit count, or `null` when telemetry hasn't loaded / failed / the
        // window has zero logs (rendered as "—" rather than libellously
        // "0" against a fresh tenant — same convention as Synonyms).
        coverage: { loaded: false, hitsByPhrase: {}, totalHits: 0 },
        sort: { key: 'activity', dir: 'desc' },
        filters: { q: '', collectionId: '', locale: '' },
        editing: null       // current group being edited in the flyout
    };

    // Auto-init for the top-level Pinned page. The Profile detail page calls
    // `GST.pinned.aurora.init({ scope })` from its own JS before DOMContentLoaded
    // fires, so the auto-init below is a no-op there (idempotent).
    document.addEventListener('DOMContentLoaded', function () {
        if (state.initialized) return;
        if (!document.getElementById('gst-pin-aurora-rows')) return;
        init();
    });

    function init(opts) {
        if (state.initialized) return;
        state.initialized = true;
        opts = opts || {};
        state.scope = opts.scope || null;
        // Toolbar wiring
        const search = document.getElementById('gst-pin-search');
        if (search) search.addEventListener('input', function () {
            state.filters.q = search.value.trim().toLowerCase();
            renderGrid();
        });
        const collFilter = document.getElementById('gst-pin-collection-filter');
        if (collFilter) collFilter.addEventListener('change', function () {
            state.filters.collectionId = collFilter.value;
            renderGrid();
        });
        const localeFilter = document.getElementById('gst-pin-locale-filter');
        if (localeFilter) localeFilter.addEventListener('change', function () {
            state.filters.locale = localeFilter.value;
            renderGrid();
        });
        const createBtn = document.getElementById('gst-pin-create');
        if (createBtn) createBtn.addEventListener('click', openCreateFlyout);

        // Sort header clicks
        document.querySelectorAll('.gst-pin-aurora-table thead th[data-sort]').forEach(function (th) {
            th.addEventListener('click', function () {
                const key = th.dataset.sort;
                if (state.sort.key === key) {
                    state.sort.dir = state.sort.dir === 'asc' ? 'desc' : 'asc';
                } else {
                    state.sort.key = key;
                    state.sort.dir = 'asc';
                }
                renderGrid();
            });
        });

        // Save button wiring (the flyout markup has data-flyout-save="pin")
        document.querySelectorAll('[data-flyout-save="pin"]').forEach(function (btn) {
            btn.addEventListener('click', onSave);
        });
        // Add-target button inside flyout
        const addTargetBtn = document.getElementById('gst-pinfly-add-target');
        if (addTargetBtn) addTargetBtn.addEventListener('click', onAddTarget);
        // Activate/Deactivate CTA — flips state.editing.isActive and re-renders.
        const toggleActiveBtn = document.getElementById('gst-pinfly-toggle-active');
        if (toggleActiveBtn) toggleActiveBtn.addEventListener('click', function () {
            if (!state.editing) return;
            state.editing.isActive = !state.editing.isActive;
            renderActiveButton();
        });
        // Delete button — only visible in edit mode (toggled in populateFlyout).
        const deleteBtn = document.getElementById('gst-pinfly-delete');
        if (deleteBtn) deleteBtn.addEventListener('click', onDelete);

        loadAll();
    }

    // ── Data loading ───────────────────────────────────────────────────

    function loadAll() {
        const tbody = document.getElementById('gst-pin-aurora-rows');
        renderLoading(tbody);

        GST.fetchJson(API + '/Collections')
            .then(function (collections) {
                let all = collections || [];
                // Profile-scoped: narrow the collection set to just the
                // profile's pinned collection so AllItems isn't walked for
                // every other collection on the tenant. When the profile is
                // generic (collectionId not yet resolved), show every
                // collection — matches the unscoped behaviour.
                if (state.scope && state.scope.collectionId) {
                    all = all.filter(function (c) { return c.id === state.scope.collectionId; });
                }
                state.collections = all;
                populateCollectionFilter();
                return Promise.all(state.collections.map(function (col) {
                    return GST.fetchJson(API + '/AllItems?collectionId=' + encodeURIComponent(col.id))
                        .then(function (resp) { return { col: col, items: (resp && resp.items) || [] }; })
                        .catch(function () { return { col: col, items: [] }; });
                }));
            })
            .then(function (bundles) {
                state.items = [];
                bundles.forEach(function (b) {
                    b.items.forEach(function (it) {
                        state.items.push({
                            id: it.id,
                            collectionId: b.col.id,
                            collectionKey: b.col.key || b.col.title || b.col.id,
                            phrases: it.phrases || '',
                            targetKey: it.targetKey || '',
                            language: it.language || '',
                            priority: typeof it.priority === 'number' ? it.priority : 0,
                            isActive: it.isActive !== false,
                            createdAt: it.createdAt || '',
                            updatedAt: it.updatedAt || '',
                            effectiveTo: it.effectiveTo || null
                        });
                    });
                });
                return resolveTargetNames(state.items);
            })
            .then(function () {
                aggregate();
                populateLocaleFilter();
                // Paint immediately with activity=unknown so the grid doesn't
                // wait on the telemetry call (it can be slow on tenants with
                // large logs). Then refresh once coverage lands.
                renderGrid();
                return loadCoverage();
            })
            .then(function () {
                applyCoverage();
                renderGrid();
            })
            .catch(function (err) {
                console.error('Aurora pinned load failed', err);
                const tbody = document.getElementById('gst-pin-aurora-rows');
                tbody.innerHTML = '<tr><td colspan="6" class="gst-empty"><p>' +
                    GST.escHtml(GST.s('pinned.load_failed', 'Could not load pinned items.')) +
                    '</p></td></tr>';
            });
    }

    // Pulls the 30-day TopPhrases head and folds it into a phrase→hits
    // map. Pinned phrases live in a `phrases` field that's free-form
    // comma-joined ("warranty, returns") so we lower-case + trim every
    // token at lookup time rather than at index time.
    function loadCoverage() {
        return GST.fetchJson(INSIGHTS_API + '/TopPhrases?days=' + ACTIVITY_DAYS + '&take=' + ACTIVITY_TAKE)
            .then(function (rows) {
                const map = {};
                let total = 0;
                (rows || []).forEach(function (r) {
                    const phrase = (r.phrase || '').trim().toLowerCase();
                    if (!phrase) return;
                    const hits = (typeof r.count === 'number') ? r.count : 0;
                    // Same phrase can land multiple times under different
                    // profile/locale splits — accumulate rather than
                    // overwrite, matching the SynonymCoverage roll-up.
                    map[phrase] = (map[phrase] || 0) + hits;
                    total += hits;
                });
                state.coverage.hitsByPhrase = map;
                state.coverage.totalHits = total;
                state.coverage.loaded = true;
            })
            .catch(function () {
                state.coverage.loaded = false;
                state.coverage.hitsByPhrase = {};
                state.coverage.totalHits = 0;
            });
    }

    function applyCoverage() {
        const noLogs = state.coverage.loaded && state.coverage.totalHits === 0;
        state.groups.forEach(function (g) {
            if (!state.coverage.loaded || noLogs) { g.hits = null; return; }
            // Phrase aliases ("warranty, returns") count as one pin but
            // each alias can earn its own hits — sum the comma-split
            // tokens so the column reflects total reach.
            let sum = 0;
            (g.phrase || '').split(',').forEach(function (p) {
                const key = p.trim().toLowerCase();
                if (!key) return;
                const h = state.coverage.hitsByPhrase[key];
                if (typeof h === 'number') sum += h;
            });
            g.hits = sum;
        });
    }

    function resolveTargetNames(items) {
        const guids = [];
        const seen = {};
        items.forEach(function (it) {
            const k = (it.targetKey || '').trim();
            if (k && /^[0-9a-f-]{36}$/i.test(k) && !seen[k.toLowerCase()]) {
                seen[k.toLowerCase()] = true;
                guids.push(k);
            }
        });
        if (guids.length === 0) return Promise.resolve();
        return GST.postJson(LOOKUP_API + '/Resolve', guids)
            .then(function (hits) {
                (hits || []).forEach(function (h) {
                    if (h.contentGuid) state.targetNames[h.contentGuid.toLowerCase()] = h.name || '';
                });
            })
            .catch(function () { /* leave names empty — UI falls back to GUID */ });
    }

    // ── Aggregation ────────────────────────────────────────────────────
    // Group key: phrase|collectionId|locale. Within a group, items are
    // sorted by priority ascending so the flyout's target list matches the
    // storefront's render order.
    function aggregate() {
        const groups = {};
        state.items.forEach(function (it) {
            const key = (it.phrases || '').toLowerCase() + '|' + it.collectionId + '|' + (it.language || '');
            if (!groups[key]) {
                groups[key] = {
                    key: key,
                    phrase: it.phrases,
                    collectionId: it.collectionId,
                    collectionKey: it.collectionKey,
                    locale: it.language || '',
                    items: [],
                    hits: null
                };
            }
            const g = groups[key];
            g.items.push(it);
        });
        state.groups = Object.keys(groups).map(function (k) {
            const g = groups[k];
            g.items.sort(function (a, b) { return (a.priority || 0) - (b.priority || 0); });
            g.activeCount = g.items.filter(function (i) { return i.isActive; }).length;
            return g;
        });
    }

    function populateCollectionFilter() {
        const sel = document.getElementById('gst-pin-collection-filter');
        if (!sel) return;
        // Profile-scoped view hides the site/collection filter container
        // upstream (it doesn't apply when the grid is locked to one
        // collection), but the <select> may still exist as a hidden
        // sentinel — leave it untouched so a future re-open of the panel
        // doesn't see a stale dropdown.
        if (state.scope) return;
        // Keep the "All" option then append one per collection.
        state.collections.forEach(function (c) {
            const opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.title || c.key || c.id;
            sel.appendChild(opt);
        });
    }

    function populateLocaleFilter() {
        const sel = document.getElementById('gst-pin-locale-filter');
        if (!sel) return;
        const seen = {};
        const locales = [];
        // When the profile declares a fixed set of locales, surface those —
        // even if no pin exists in that locale yet — so the filter matches
        // the profile's declared scope rather than the (possibly empty)
        // intersection with the current pin set.
        if (state.scope && Array.isArray(state.scope.locales) && state.scope.locales.length) {
            state.scope.locales.forEach(function (l) {
                if (l && !seen[l]) { seen[l] = true; locales.push(l); }
            });
        } else {
            state.groups.forEach(function (g) {
                const l = g.locale || '';
                if (l && !seen[l]) { seen[l] = true; locales.push(l); }
            });
        }
        locales.sort();
        locales.forEach(function (l) {
            const opt = document.createElement('option');
            opt.value = l;
            opt.textContent = l;
            sel.appendChild(opt);
        });
    }

    // ── Grid render ────────────────────────────────────────────────────

    function renderLoading(tbody) {
        const msg = GST.s('shared.loading', 'Loading…');
        tbody.innerHTML = '<tr><td colspan="6" class="gst-muted">' + GST.escHtml(msg) + '</td></tr>';
    }

    function renderGrid() {
        const tbody = document.getElementById('gst-pin-aurora-rows');
        if (!tbody) return;

        const filtered = state.groups.filter(function (g) {
            if (state.filters.collectionId && g.collectionId !== state.filters.collectionId) return false;
            if (state.filters.locale && g.locale !== state.filters.locale) return false;
            if (state.filters.q) {
                const q = state.filters.q;
                if ((g.phrase || '').toLowerCase().indexOf(q) === -1 &&
                    (g.collectionKey || '').toLowerCase().indexOf(q) === -1) return false;
            }
            return true;
        });

        const sorted = sortGroups(filtered);

        if (sorted.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="gst-empty"><p>' +
                GST.escHtml(GST.s('pinned.empty_grid', 'No pinned items yet. Click "Add" to create one.')) +
                '</p></td></tr>';
            return;
        }

        const itemsTmpl = GST.s('pinned.items_count', '%1 items');
        const deleteLabel = GST.s('shared.delete', 'Delete');
        const trash = '<svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" aria-hidden="true">' +
            '<path d="M3 4 H13 M5 4 V13 a1 1 0 0 0 1 1 H10 a1 1 0 0 0 1 -1 V4 M6 4 V2 a1 1 0 0 1 1 -1 H9 a1 1 0 0 1 1 1 V4 M6.5 7 V11 M9.5 7 V11"/>' +
            '</svg>';
        tbody.innerHTML = sorted.map(function (g) {
            return '<tr class="is-selectable" data-group-key="' + GST.escHtml(g.key) + '">' +
                '<td><a href="#" class="gst-table__link" data-row-link>' + GST.escHtml(g.phrase || '(empty)') + '</a></td>' +
                '<td>' + GST.escHtml(g.collectionKey || '') + '</td>' +
                '<td>' + renderLocaleCell(g.locale) + '</td>' +
                '<td>' + GST.escHtml(itemsTmpl.replace('%1', g.items.length)) + '</td>' +
                '<td>' + renderActivityCell(g.hits) + '</td>' +
                '<td class="gst-table__actions">' +
                '<button class="gst-rowdelete" data-row-delete title="' + GST.escHtml(deleteLabel) + '" aria-label="' + GST.escHtml(deleteLabel) + '">' + trash + '</button>' +
                '</td>' +
                '</tr>';
        }).join('');

        tbody.querySelectorAll('tr.is-selectable').forEach(function (tr) {
            tr.addEventListener('click', function (e) {
                if (e.target.closest('a, button')) return;
                openEditFlyout(tr.dataset.groupKey);
            });
            const link = tr.querySelector('[data-row-link]');
            if (link) link.addEventListener('click', function (e) {
                e.preventDefault();
                openEditFlyout(tr.dataset.groupKey);
            });
            const deleteBtn = tr.querySelector('[data-row-delete]');
            if (deleteBtn) deleteBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                deleteGroup(tr.dataset.groupKey);
            });
        });
    }

    // Delete every item in the group via N sequential DELETEs. Used by the
    // row-menu's Delete action (and shares plumbing with the flyout's own
    // Delete button via onDelete). One-click — no confirmation; partial
    // failures fall back to a full reload so the grid can't go out of sync.
    function deleteGroup(groupKey) {
        const g = state.groups.find(function (x) { return x.key === groupKey; });
        if (!g) return;
        const ops = g.items.map(function (it) {
            return { kind: 'delete', id: it.id, collectionId: it.collectionId };
        });
        // runOps wants a button to spin; the row menu has no button to bind
        // to, so synthesize a hidden one. The button's disabled/text changes
        // are invisible but the network sequencing is the same.
        const dummy = document.createElement('button');
        runOps(ops, dummy, g.collectionId).then(function (errors) {
            if (errors > 0) {
                window.alert(GST.s('pinned.save_failed', '%1 of %2 changes failed.')
                    .replace('%1', errors).replace('%2', ops.length));
                loadAll();
                return;
            }
            removeItemsLocally(g.items);
        });
    }

    // Drops the given items from state.items, re-aggregates the groups, and
    // repaints the grid — avoids a network round-trip on the happy path.
    // Only id-bearing items are matched; an item with no id (a never-saved
    // create that shouldn't reach this path anyway) would otherwise key the
    // ids set on undefined and purge every other unsaved item with it.
    function removeItemsLocally(items) {
        const ids = {};
        let expected = 0;
        items.forEach(function (it) {
            if (it && it.id) { ids[it.id] = true; expected++; }
        });
        if (expected === 0) return;
        const before = state.items.length;
        state.items = state.items.filter(function (it) { return !ids[it.id]; });
        const removed = before - state.items.length;
        if (removed !== expected) {
            // Local state diverged from what we expected to splice — bail to
            // a full reload so the grid can't show stale or missing rows.
            console.warn('Pinned: removeItemsLocally expected ' + expected +
                ' removals but stripped ' + removed + '; reloading.');
            loadAll();
            return;
        }
        aggregate();
        // applyCoverage repopulates g.hits on the rebuilt groups so the
        // Activity column doesn't fall back to "—" for every surviving row.
        applyCoverage();
        renderGrid();
    }

    function sortGroups(arr) {
        const k = state.sort.key, dir = state.sort.dir === 'asc' ? 1 : -1;
        return arr.slice().sort(function (a, b) {
            let va, vb;
            switch (k) {
                case 'phrase':     va = (a.phrase || '').toLowerCase(); vb = (b.phrase || '').toLowerCase(); break;
                case 'collection': va = (a.collectionKey || '').toLowerCase(); vb = (b.collectionKey || '').toLowerCase(); break;
                case 'locale':     va = a.locale || ''; vb = b.locale || ''; break;
                case 'items':      va = a.items.length; vb = b.items.length; break;
                // Activity sort: numeric. Coverage-not-loaded groups
                // (hits === null) sort lowest so they don't muddy a
                // "most active first" descending scan; ascending puts
                // them after 0-hit groups. Matches Synonyms.
                case 'activity':
                default:
                    va = (typeof a.hits === 'number') ? a.hits : -1;
                    vb = (typeof b.hits === 'number') ? b.hits : -1;
                    break;
            }
            if (va < vb) return -1 * dir;
            if (va > vb) return 1 * dir;
            return 0;
        });
    }

    // Pin locale=null means "applies regardless of locale" (Graph stores
    // Language as null). Render that explicitly so the column doesn't read
    // as missing data — same text weight as a real locale code so the row
    // doesn't look disabled.
    function renderLocaleCell(locale) {
        if (locale) return GST.escHtml(locale);
        return GST.escHtml(GST.s('pinFlyout.localeAll', 'All locales'));
    }

    function renderActivityCell(hits) {
        if (typeof hits !== 'number') {
            return '<span class="gst-muted">' +
                GST.escHtml(GST.s('pinned.activity_unknown', '—')) +
                '</span>';
        }
        if (hits === 0) {
            // Mute the zero so an inactive pin reads as "needs attention"
            // without screaming colour at every fresh-tenant row.
            return '<span class="gst-muted">0</span>';
        }
        return '<strong>' + hits.toLocaleString() + '</strong>';
    }

    // ── Flyout — open / populate ───────────────────────────────────────

    function openEditFlyout(groupKey) {
        const g = state.groups.find(function (x) { return x.key === groupKey; });
        if (!g) return;
        state.editing = {
            mode: 'edit',
            group: g,
            originals: g.items.map(snapshotItem),
            targets: g.items.map(function (it) {
                return {
                    id: it.id,
                    targetKey: it.targetKey,
                    name: resolvedName(it.targetKey),
                    priority: it.priority,
                    isActive: it.isActive,
                    effectiveTo: it.effectiveTo
                };
            })
        };
        populateFlyout(g);
        GST.flyout.open('pin');
    }

    // Friendly display for a target's content key. When Graph could resolve
    // the GUID we have a real name; otherwise show the short GUID prefix so
    // the row reads as "an item" rather than wrapping a 36-char string.
    function resolvedName(targetKey) {
        if (!targetKey) return '(empty)';
        const hit = state.targetNames[targetKey.toLowerCase()];
        if (hit) return hit;
        if (/^[0-9a-f-]{36}$/i.test(targetKey)) {
            return GST.s('pinned.target_unresolved', 'Content not in index')
                + ' (' + targetKey.slice(0, 8) + '…)';
        }
        return targetKey;
    }

    function openCreateFlyout() {
        // Use the first collection by default; user can re-pick later if we
        // add a collection chooser inside the flyout (out of scope for v1).
        const col = state.filters.collectionId
            ? state.collections.find(function (c) { return c.id === state.filters.collectionId; })
            : state.collections[0];
        if (!col) {
            window.alert(GST.s('pinned.load_failed', 'Could not load pinned items.'));
            return;
        }
        state.editing = {
            mode: 'create',
            group: {
                key: '',
                phrase: '',
                collectionId: col.id,
                collectionKey: col.key || col.title || col.id,
                locale: '',
                items: [],
                modified: ''
            },
            originals: [],
            targets: []
        };
        populateFlyout(state.editing.group);
        GST.flyout.open('pin');
    }

    function populateFlyout(g) {
        document.getElementById('gst-flyout-pin-title').textContent =
            state.editing.mode === 'create'
                ? GST.s('pinFlyout.createTitle', 'Create pin')
                : GST.s('pinFlyout.editTitle', 'Edit pin');
        // Delete button is only meaningful when editing an existing group.
        const deleteBtn = document.getElementById('gst-pinfly-delete');
        if (deleteBtn) deleteBtn.hidden = state.editing.mode !== 'edit';
        document.getElementById('gst-pinfly-phrase').value = g.phrase || '';
        // Locale dropdown — populate from collections + groups so any locale
        // the tenant uses is selectable. Repopulating per-open keeps the list
        // fresh if a new locale appeared.
        const localeSel = document.getElementById('gst-pinfly-locale');
        const knownLocales = collectLocales();
        if (g.locale && knownLocales.indexOf(g.locale) === -1) knownLocales.unshift(g.locale);
        // Empty value maps to Graph's null Language ("applies regardless of
        // locale"). Label it explicitly so it doesn't read as "no choice yet".
        const allLabel = GST.escHtml(GST.s('pinFlyout.localeAll', 'All locales'));
        localeSel.innerHTML = '<option value=""' + (g.locale ? '' : ' selected') + '>' + allLabel + '</option>' +
            knownLocales.map(function (l) {
                return '<option value="' + GST.escHtml(l) + '"' + (l === g.locale ? ' selected' : '') + '>' + GST.escHtml(l) + '</option>';
            }).join('');
        // Effective until — use the first item's effectiveTo (if any).
        const first = g.items[0];
        document.getElementById('gst-pinfly-effective').value =
            first && first.effectiveTo ? String(first.effectiveTo).slice(0, 10) : '';
        // Active state — true if the first item is active (or no items yet).
        state.editing.isActive = !first || first.isActive !== false;
        renderActiveButton();

        renderTargetList();
        checkConflicts(g.phrase || '', g.locale || '');
    }

    function renderActiveButton() {
        const btn = document.getElementById('gst-pinfly-toggle-active');
        if (!btn || !state.editing) return;
        const isActive = state.editing.isActive;
        btn.textContent = isActive
            ? GST.s('pinFlyout.deactivate', 'Deactivate')
            : GST.s('pinFlyout.activate', 'Activate');
        btn.classList.toggle('gst-btn--danger', isActive);
    }

    function collectLocales() {
        const seen = {};
        state.groups.forEach(function (g) { if (g.locale) seen[g.locale] = true; });
        return Object.keys(seen).sort();
    }

    function renderTargetList() {
        const host = document.getElementById('gst-pinfly-targets');
        const count = document.getElementById('gst-pinfly-targets-count');
        const targets = state.editing.targets;
        count.textContent = '(' + targets.length + ')';
        if (targets.length === 0) {
            host.innerHTML = '<div class="gst-muted" style="padding:8px 0">' +
                GST.escHtml(GST.s('pinFlyout.targetsEmpty', 'No targets yet.')) + '</div>';
            return;
        }
        host.innerHTML = targets.map(function (t, idx) {
            return '<div class="gst-target-row" draggable="true" data-idx="' + idx + '">' +
                '<span class="gst-target-row__drag" aria-hidden="true"></span>' +
                '<span class="gst-target-row__body">' +
                    '<span class="gst-target-row__name">' + GST.escHtml(t.name || t.targetKey || '(empty)') + '</span>' +
                '</span>' +
                '<button type="button" class="gst-target-row__remove" data-remove="' + idx + '" aria-label="Remove">×</button>' +
                '</div>';
        }).join('');
        // Drag reorder + remove wiring
        host.querySelectorAll('[data-remove]').forEach(function (btn) {
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                const idx = parseInt(btn.getAttribute('data-remove'), 10);
                state.editing.targets.splice(idx, 1);
                renderTargetList();
            });
        });
        wireDrag(host);
    }

    function wireDrag(host) {
        let dragSrc = null;
        host.querySelectorAll('.gst-target-row').forEach(function (el) {
            el.addEventListener('dragstart', function () {
                dragSrc = el;
                el.classList.add('is-dragging');
            });
            el.addEventListener('dragend', function () {
                el.classList.remove('is-dragging');
                dragSrc = null;
            });
            el.addEventListener('dragover', function (e) { e.preventDefault(); });
            el.addEventListener('drop', function (e) {
                e.preventDefault();
                if (!dragSrc || dragSrc === el) return;
                const from = parseInt(dragSrc.dataset.idx, 10);
                const to = parseInt(el.dataset.idx, 10);
                const moved = state.editing.targets.splice(from, 1)[0];
                state.editing.targets.splice(to, 0, moved);
                renderTargetList();
            });
        });
    }

    function onAddTarget(e) {
        e.preventDefault();
        if (typeof GST.contentPicker !== 'function') {
            window.alert('Content picker not available.');
            return;
        }
        GST.contentPicker({ title: GST.s('pinned.target_pick', 'Pick content') }).then(function (hit) {
            if (!hit) return;
            // ContentPicker may return either a CMS-id-based hit ({id, name}) or
            // a Graph-resolved hit ({contentGuid, name}). Prefer the GUID since
            // Graph pinned items key off ContentGuid.
            const guid = hit.contentGuid || hit.guid || hit.contentGuidValue || '';
            const name = hit.name || hit.displayName || '';
            if (!guid && !name) return;
            state.editing.targets.push({
                id: '',                                  // empty = new
                targetKey: guid,
                name: name || guid,
                priority: state.editing.targets.length,  // append
                isActive: true,
                effectiveTo: null
            });
            renderTargetList();
        });
    }

    // ── Conflict detection ─────────────────────────────────────────────
    // For the active phrase+locale, look across other collections; surface
    // an inline warning so the marketer sees the overlap before saving.
    function checkConflicts(phrase, locale) {
        const notice = document.getElementById('gst-pinfly-conflict');
        const body = document.getElementById('gst-pinfly-conflict-body');
        if (!notice || !body || !phrase) { if (notice) notice.hidden = true; return; }

        const phraseLower = phrase.trim().toLowerCase();
        if (!phraseLower) { notice.hidden = true; return; }

        const otherCollections = {};
        state.items.forEach(function (it) {
            if (it.collectionId === state.editing.group.collectionId) return;
            if ((it.phrases || '').toLowerCase() !== phraseLower) return;
            if (locale && it.language && it.language !== locale) return;
            otherCollections[it.collectionKey] = true;
        });
        const conflicts = Object.keys(otherCollections);
        if (conflicts.length === 0) { notice.hidden = true; return; }
        notice.hidden = false;
        body.textContent = ' ' + GST.s('pinned.conflict', 'This phrase is also pinned in: %1')
            .replace('%1', conflicts.join(', '));
    }

    function snapshotItem(it) {
        return {
            id: it.id,
            targetKey: it.targetKey,
            priority: it.priority,
            isActive: it.isActive,
            effectiveTo: it.effectiveTo
        };
    }

    // ── Delete ─────────────────────────────────────────────────────────
    // Removes every item in the group via N sequential DELETEs.
    function onDelete(e) {
        e.preventDefault();
        if (!state.editing || state.editing.mode !== 'edit') return;
        const g = state.editing.group;
        const btn = e.currentTarget;
        const ops = g.items.map(function (it) {
            return { kind: 'delete', id: it.id, collectionId: it.collectionId };
        });
        runOps(ops, btn, g.collectionId).then(function (errors) {
            GST.flyout.close('pin');
            if (errors > 0) {
                window.alert(GST.s('pinned.save_failed', '%1 of %2 changes failed.')
                    .replace('%1', errors).replace('%2', ops.length));
                loadAll();
                return;
            }
            removeItemsLocally(g.items);
        });
    }

    // ── Save ───────────────────────────────────────────────────────────

    function onSave(e) {
        e.preventDefault();
        if (!state.editing) return;
        const btn = e.currentTarget;
        const g = state.editing.group;
        const newPhrase = document.getElementById('gst-pinfly-phrase').value.trim();
        const newLocale = document.getElementById('gst-pinfly-locale').value.trim();
        const newEffective = document.getElementById('gst-pinfly-effective').value.trim() || null;
        const newActive = state.editing.isActive !== false;

        if (!newPhrase) { window.alert(GST.s('pinned.error_phrase_and_content_required', 'Phrase and content are required.')); return; }
        if (state.editing.targets.length === 0) { window.alert(GST.s('pinned.error_phrase_and_content_required', 'Phrase and content are required.')); return; }

        // Apply form-level fields to every target so the diff below picks them up.
        state.editing.targets.forEach(function (t, idx) {
            t.priority = idx;
            t.isActive = newActive;
            t.effectiveTo = newEffective;
        });

        const ops = diff(state.editing.originals, state.editing.targets, {
            phrases: newPhrase,
            language: newLocale,
            collectionId: g.collectionId
        });

        runOps(ops, btn, g.collectionId).then(function (errors) {
            if (errors > 0) {
                window.alert(GST.s('pinned.save_failed', '%1 of %2 changes failed.')
                    .replace('%1', errors).replace('%2', ops.length));
            }
            GST.flyout.close('pin');
            loadAll();
        });
    }

    function diff(originals, current, env) {
        const ops = [];
        const byId = {};
        originals.forEach(function (o) { if (o.id) byId[o.id] = o; });

        // Adds and updates
        current.forEach(function (t) {
            if (!t.id) {
                ops.push({
                    kind: 'create',
                    payload: {
                        phrases: env.phrases,
                        targetKey: t.targetKey,
                        language: env.language || null,
                        priority: t.priority,
                        isActive: t.isActive
                    },
                    collectionId: env.collectionId
                });
            } else {
                const o = byId[t.id];
                if (!o) return;
                const changed =
                    o.targetKey !== t.targetKey ||
                    o.priority !== t.priority ||
                    o.isActive !== t.isActive ||
                    (originalPhraseOrLocaleChanged(env));
                if (changed) {
                    ops.push({
                        kind: 'update',
                        id: t.id,
                        payload: {
                            phrases: env.phrases,
                            targetKey: t.targetKey,
                            language: env.language || null,
                            priority: t.priority,
                            isActive: t.isActive
                        },
                        collectionId: env.collectionId
                    });
                }
            }
        });

        // Deletes (originals not represented in current)
        const currentIds = {};
        current.forEach(function (t) { if (t.id) currentIds[t.id] = true; });
        originals.forEach(function (o) {
            if (o.id && !currentIds[o.id]) {
                ops.push({ kind: 'delete', id: o.id, collectionId: env.collectionId });
            }
        });
        return ops;
    }

    // Always update when phrase or locale changes form-wide. We don't track
    // the originals' phrase/locale separately because they live on the
    // group — if those mutated, every original needs a refresh.
    function originalPhraseOrLocaleChanged(env) {
        const g = state.editing.group;
        return (g.phrase || '') !== (env.phrases || '') ||
               (g.locale || '') !== (env.language || '');
    }

    function runOps(ops, btn, collectionId) {
        if (ops.length === 0) return Promise.resolve(0);
        const saveTmpl = GST.s('pinned.save_progress', 'Saving %1 of %2…');
        let errors = 0;
        let done = 0;
        btn.disabled = true;
        const orig = btn.textContent;

        const step = function (op) {
            done++;
            btn.textContent = saveTmpl.replace('%1', done).replace('%2', ops.length);
            switch (op.kind) {
                case 'create':
                    return GST.postJson(API + '/CreateItem?collectionId=' + encodeURIComponent(op.collectionId) + scopeQs(), op.payload);
                case 'update':
                    return fetch(API + '/UpdateItem?collectionId=' + encodeURIComponent(op.collectionId) +
                        '&id=' + encodeURIComponent(op.id) + scopeQs(), {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                        body: JSON.stringify(op.payload)
                    }).then(function (r) {
                        if (!r.ok) throw new Error('Update failed ' + r.status);
                        return r.json().catch(function () { return null; });
                    });
                case 'delete':
                    return fetch(API + '/DeleteItem?collectionId=' + encodeURIComponent(op.collectionId) +
                        '&id=' + encodeURIComponent(op.id) + scopeQs(), {
                        method: 'DELETE',
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    }).then(function (r) {
                        // 404 means the item is already gone — that's the desired end
                        // state, so treat as success. Without this, rapid clicks on
                        // adjacent rows or a row whose first delete is mid-flight
                        // surface as "1 of 1 changes failed" alerts even though the
                        // user got what they wanted.
                        if (r.status === 404) return;
                        if (!r.ok) throw new Error('Delete failed ' + r.status);
                    });
            }
        };

        // Run sequentially so a failure doesn't race past the rest of the
        // diff and corrupt priority numbering.
        let chain = Promise.resolve();
        ops.forEach(function (op) {
            chain = chain.then(function () {
                return step(op).catch(function (err) {
                    errors++;
                    console.error('Pin op failed', op, err);
                });
            });
        });
        return chain.then(function () {
            btn.disabled = false;
            btn.textContent = orig;
            return errors;
        });
    }

    // Expose `init` so the Profile detail view can mount a scoped instance
    // before DOMContentLoaded fires. The auto-init handler above bails when
    // `state.initialized` is already true, so calling `init({ scope })`
    // pre-empts the unscoped default.
    window.GST = window.GST || {};
    window.GST.pinned = window.GST.pinned || {};
    window.GST.pinned.aurora = { init: init };
})();
