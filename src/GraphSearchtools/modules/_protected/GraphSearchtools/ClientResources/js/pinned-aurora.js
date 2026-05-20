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
 * phrase+locale; warn if found in another collection (cross-channel overlap).
 */
(function () {
    const API = window.GST_BASE_URL + '/PinnedApi';
    const LOOKUP_API = window.GST_BASE_URL + '/ContentLookupApi';
    const INSIGHTS_API = window.GST_BASE_URL + '/InsightsApi';
    const PINNED_STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinned) || {};

    // Editing happens either inside a Channel detail tab (state.scope) or
    // via the top-level + Add flyout, which seeds state.flyoutScope from the
    // channel-picker. Flyout scope wins so a save attributes to whatever the
    // picker has selected.
    function effectiveScope() { return state.flyoutScope || state.scope || null; }

    // Tell the Channel detail's live preview that a mutation just landed so
    // it can repaint against the new state. No-op when the listener isn't
    // mounted (top-level Pinned page has no preview to refresh).
    function notifyPreviewChanged() {
        document.dispatchEvent(new CustomEvent('gst:preview-refresh'));
    }

    function scopeQs() {
        var s = effectiveScope();
        if (s && s.channelKey) {
            return '&channelKey=' + encodeURIComponent(s.channelKey);
        }
        return '';
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
        // Optional channel-scoping: when set, the grid limits itself to the
        // channel's collection (`scope.collectionId`) and locales
        // (`scope.locales`), and skips the unscoped collection filter. The
        // Channel detail page wires this; the top-level Pinned page leaves
        // it null. `scope.channelKey` is appended to write URLs so the
        // PinnedApi can resolve the channel context for auditing.
        scope: null,
        // Top-level only: registered channels fetched on init, used by the
        // flyout's matching-channels panel so the user can jump to the
        // owning Channel detail from a pin row.
        availableChannels: [],
        // Top-level only: full collection→[{channelKey, locale}, ...] map
        // (CollectionChannels endpoint). Used by the pin flyout's
        // matching-channels panel — same shape as the Collection flyout's.
        collectionChannelsMap: {},
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

    // Auto-init for the top-level Pinned page only. On Channel detail the
    // page has a `.gst-prof-switcher` element and mounts the Pinned tab
    // lazily on click via `GST.pinned.aurora.init({ scope })` — auto-init
    // would race ahead with no scope and the scoped call would no-op.
    document.addEventListener('DOMContentLoaded', function () {
        if (state.initialized) return;
        if (!document.getElementById('gst-pin-aurora-rows')) return;
        if (document.querySelector('.gst-prof-switcher')) return;
        init();
    });

    function init(opts) {
        opts = opts || {};
        // Re-init: if a scoped caller arrives after auto-init has already
        // taken effect (e.g. user clicks the Pinned tab on Channel detail
        // after DOMContentLoaded), update the scope and reload instead of
        // silently dropping the call.
        if (state.initialized) {
            if (opts.scope) {
                state.scope = opts.scope;
                loadAll();
            }
            return;
        }
        state.initialized = true;
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
        // Channel detail's page-level locale chip (#gst-pin-locale) lives in
        // the Try-It header and is the page's single locale source of truth
        // — Insights listens to it too. Mirror its value into the Pinned
        // grid's filter so changing the chip narrows the grid in lockstep.
        const localeChip = document.getElementById('gst-pin-locale');
        if (localeChip) {
            const syncFromChip = function () {
                state.filters.locale = localeChip.value || '';
                if (localeFilter) localeFilter.value = state.filters.locale;
                renderGrid();
            };
            localeChip.addEventListener('change', syncFromChip);
            // Seed once at init so the grid opens scoped to the chip's
            // initial value (typically the channel's first declared locale).
            if (localeChip.value) {
                state.filters.locale = localeChip.value;
            }
        }
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
        // Cancel / close: clear any transient flyout scope so a subsequent
        // open starts clean.
        document.querySelectorAll('[data-flyout-close="pin"]').forEach(function (btn) {
            btn.addEventListener('click', function () { state.flyoutScope = null; });
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

        applyReadOnlyGate();

        loadAll();
    }

    // Gate every mutating control off window.GST_PERMS. PinnedEdit covers
    // item create/update/delete; Collections covers the collection-shell
    // operations (add collection, ensure-on-save). If the user has neither,
    // show a banner explaining why everything's read-only.
    function applyReadOnlyGate() {
        if (!window.GST) return;
        var canEditItem = GST.can('pinnedEdit');
        var canEditColl = GST.can('collections');
        var tip = GST.s('pinned.readonly_tooltip', 'You do not have edit permission for pinned items.');
        var collTip = GST.s('pinned.readonly_collection_tooltip', 'You do not have permission to manage pinned collections.');
        // Anchor inside the tab/panel that owns the table so the banner sits
        // with the grid, not above the Channel-detail header that spans every
        // sibling tab. Falls back to the top-level page header on standalone
        // pages that don't use the tabpanel wrapper.
        var table = document.querySelector('.gst-pin-aurora-table');
        var host = (table && table.closest('[data-tab],[data-panel]'))
            || document.querySelector('.gst-page-header')
            || document.querySelector('main')
            || document.body;

        if (!canEditItem && !canEditColl) {
            GST.renderReadOnlyBanner(host,
                GST.s('pinned.readonly_banner', 'Read-only access — your role does not include edit permission for pinned results.'));
        }

        if (!canEditItem) {
            // Top-level Add-Pin button, the row-level delete buttons, and the
            // flyout's save/delete + add-target controls.
            GST.disableAll(document, '#gst-pin-create, [data-flyout-save="pin"], #gst-pinfly-delete, #gst-pinfly-add-target, #gst-pinfly-toggle-active', tip);
            GST.disableAll(document, '.gst-pin-row-delete, [data-action="delete-pin"]', tip);
        }
        if (!canEditColl) {
            // Add-Collection / EnsureCollection-driven buttons live on the
            // Channel detail Pinned tab and the Collections-tab flyout.
            GST.disableAll(document, '#gst-pin-add-collection, [data-action="add-collection"], [data-action="delete-collection"]', collTip);
        }
    }

    // ── Data loading ───────────────────────────────────────────────────

    function loadAll() {
        const tbody = document.getElementById('gst-pin-aurora-rows');
        renderLoading(tbody);

        // Top-level (no scope) needs both:
        //   * The collection→channel map so each row deep-links to its owner.
        //   * The list of registered channels so the Add-Pin flyout can offer
        //     a channel picker.
        var isTop = !(state.scope && state.scope.channelKey);
        var mapPromise = isTop
            ? GST.fetchJson(API + '/ChannelMap').catch(function () { return {}; })
            : Promise.resolve({});
        var channelsPromise = isTop
            ? GST.fetchJson('/EPiServer/cms/graphsearchtools/api/channels').catch(function () { return []; })
            : Promise.resolve([]);
        var collectionChannelsPromise = isTop
            ? GST.fetchJson(API + '/CollectionChannels').catch(function () { return {}; })
            : Promise.resolve({});

        Promise.all([GST.fetchJson(API + '/Collections'), mapPromise, channelsPromise, collectionChannelsPromise])
            .then(function (results) {
                var collections = results[0];
                state.collectionChannelMap = results[1] || {};
                state.availableChannels = results[2] || [];
                state.collectionChannelsMap = results[3] || {};
                let all = collections || [];
                // Channel-scoped: narrow the collection set to the (possibly
                // multiple) collections backing this channel's locales. Nulls
                // in the map are locales without a backing collection yet —
                // skip them on the read side; EnsureCollection materializes
                // them on first save.
                if (state.scope && state.scope.collectionsByLocale) {
                    var allowed = {};
                    Object.keys(state.scope.collectionsByLocale).forEach(function (l) {
                        var cid = state.scope.collectionsByLocale[l];
                        if (cid) allowed[cid] = true;
                    });
                    all = all.filter(function (c) { return allowed[c.id]; });
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
                            collectionKey: b.col.key || b.col.id,
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
                    // channel/locale splits — accumulate rather than
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
        // Channel-scoped view hides the site/collection filter container
        // upstream (it doesn't apply when the grid is locked to one
        // collection), but the <select> may still exist as a hidden
        // sentinel — leave it untouched so a future re-open of the panel
        // doesn't see a stale dropdown.
        if (state.scope) return;
        // Keep the "All" option then append one per collection.
        state.collections.forEach(function (c) {
            const opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.key || c.id;
            sel.appendChild(opt);
        });
    }

    function populateLocaleFilter() {
        const sel = document.getElementById('gst-pin-locale-filter');
        if (!sel) return;
        const seen = {};
        const locales = [];
        // When the channel declares a fixed set of locales, surface those —
        // even if no pin exists in that locale yet — so the filter matches
        // the channel's declared scope rather than the (possibly empty)
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
        // Reflect the seeded filter (typically mirrored from the page-level
        // locale chip) in the dropdown so the visible selection matches what
        // the grid is actually filtering on. Falls back to "all" when the
        // seeded value isn't among the rendered options.
        sel.value = (state.filters.locale && seen[state.filters.locale]) ? state.filters.locale : '';
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
            // Locale filter: a pin with an empty locale is treated as
            // cross-locale ("global"), so it always shows alongside the
            // selected locale. Matches the preview semantics — the server
            // includes hits with no Language alongside the locale-matched
            // hits when running /preview, and the marketer expects the grid
            // and preview to agree on what's "applicable here".
            if (state.filters.locale && g.locale && g.locale !== state.filters.locale) return false;
            if (state.filters.q) {
                const q = state.filters.q;
                if ((g.phrase || '').toLowerCase().indexOf(q) === -1 &&
                    (g.collectionKey || '').toLowerCase().indexOf(q) === -1) return false;
            }
            return true;
        });

        const sorted = sortGroups(filtered);

        // Top-level (no channel scope) is read-only: suppress per-row delete
        // and let openOrJump send row clicks to the owning channel detail.
        var isTopLevel = !(state.scope && state.scope.channelKey);

        if (sorted.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" class="gst-empty"><p>' +
                GST.escHtml(GST.s('pinned.empty_grid', 'No pinned items yet. Click "Add" to create one.')) +
                '</p></td></tr>';
            return;
        }

        const itemsTmpl = GST.s('pinned.items_count', '%1 items');
        const deleteLabel = GST.s('shared.delete', 'Delete');
        const previewLabel = GST.s('channels.detail.insights.actionPreview', 'Preview this phrase');
        const trash = GST.icons.trash;
        // Magnifier glyph — matches the SERP input's own icon so the
        // affordance reads as "send this phrase to the preview".
        const eye = GST.icons.search;
        // Preview button only renders inside a channel-scoped grid — that's
        // the only context where #gst-pin-tryit-q (the SERP input) exists.
        // Skipping it on the top-level Pinned page keeps the actions cell
        // tidy when the affordance would have nowhere to land.
        var hasPreviewTarget = !isTopLevel && !!document.getElementById('gst-pin-tryit-q');
        tbody.innerHTML = sorted.map(function (g) {
            var actionButtons = '';
            if (hasPreviewTarget) {
                actionButtons += '<button class="gst-rowaction" data-row-preview title="' + GST.escHtml(previewLabel) + '" aria-label="' + GST.escHtml(previewLabel) + '">' + eye + '</button>';
            }
            if (!isTopLevel) {
                actionButtons += '<button class="gst-rowdelete" data-row-delete title="' + GST.escHtml(deleteLabel) + '" aria-label="' + GST.escHtml(deleteLabel) + '">' + trash + '</button>';
            }
            var actionsCell = '<td class="gst-table__actions">' + actionButtons + '</td>';
            return '<tr class="is-selectable" data-group-key="' + GST.escHtml(g.key) + '">' +
                '<td><a href="#" class="gst-table__link" data-row-link>' + GST.escHtml(g.phrase || '(empty)') + '</a></td>' +
                '<td class="col-collection">' + GST.escHtml(g.collectionKey || '') + '</td>' +
                '<td>' + renderLocaleCell(g.locale) + '</td>' +
                '<td>' + GST.escHtml(itemsTmpl.replace('%1', g.items.length)) + '</td>' +
                '<td>' + renderActivityCell(g.hits) + '</td>' +
                actionsCell +
                '</tr>';
        }).join('');

        tbody.querySelectorAll('tr.is-selectable').forEach(function (tr) {
            tr.addEventListener('click', function (e) {
                if (e.target.closest('a, button')) return;
                openOrJump(tr.dataset.groupKey);
            });
            const link = tr.querySelector('[data-row-link]');
            if (link) link.addEventListener('click', function (e) {
                e.preventDefault();
                openOrJump(tr.dataset.groupKey);
            });
            const previewBtn = tr.querySelector('[data-row-preview]');
            if (previewBtn) previewBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                applyGroupToPreview(tr.dataset.groupKey);
            });
            const deleteBtn = tr.querySelector('[data-row-delete]');
            if (deleteBtn) deleteBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                deleteGroup(tr.dataset.groupKey);
            });
        });
    }

    // Drop the row's phrase into the Channel detail page's live preview.
    // The SERP input has its own debounced input listener (wired in
    // channels.js → wireLivePreview), so dispatching an `input` event is
    // enough to trigger a fetch + re-render. The pin's locale is left
    // alone — the page-level locale chip is the source of truth and
    // marketers usually want to see how the pin performs in the locale
    // they're currently inspecting.
    function applyGroupToPreview(groupKey) {
        const g = state.groups.find(function (x) { return x.key === groupKey; });
        if (!g) return;
        const phrase = (g.phrase || '').trim();
        if (!phrase) return;
        const input = document.getElementById('gst-pin-tryit-q');
        if (!input) return;
        input.value = phrase;
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }

    // Both channel-scoped and top-level open the editor flyout. The
    // top-level path uses a best-effort channelKey lookup (via the
    // collectionChannelMap) for audit attribution and surfaces all
    // matching channels as deep-links inside the flyout body.
    function openOrJump(groupKey) {
        openEditFlyout(groupKey);
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
            notifyPreviewChanged();
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
        return GST.escHtml(GST.s('pinFlyout.localeAll', 'Global'));
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
        // Top-level edit needs a flyout scope so subsequent CRUD ops carry
        // an audit channelKey when one resolves to this group's collection.
        // Channel-scoped pages keep their mount-time state.scope and leave
        // flyoutScope null.
        var isTop = !(state.scope && state.scope.channelKey);
        state.flyoutScope = isTop ? buildFlyoutScopeForCollection(g.collectionId) : null;
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

    // Builds a flyout-only scope for a collection picked from the dropdown
    // (create flow) or inferred from a clicked row (edit flow). channelKey
    // is best-effort: when a registered channel resolves to this
    // collection's key, use it for audit attribution; otherwise leave it
    // blank and the backend skips the audit row.
    function buildFlyoutScopeForCollection(collectionId) {
        var c = state.collections.find(function (x) { return x.id === collectionId; });
        if (!c) return null;
        var key = c.key || '';
        var channelKey = (state.collectionChannelMap || {})[key] || '';
        return {
            collectionId: c.id,
            collectionKey: key,
            channelKey: channelKey,
            // collectionsByLocale stays empty — the collection is already
            // known by id, so ensureCollectionForLocale short-circuits.
            collectionsByLocale: {},
            locales: []
        };
    }

    // Renders a <ul> of channel×locales pairs that resolve to the given
    // collection key. Mirrors the Collection flyout's "Matching channels"
    // panel so the two surfaces feel like one feature.
    function renderMatchingChannels(host, collectionKey) {
        host.innerHTML = '';
        var matches = (state.collectionChannelsMap || {})[collectionKey] || [];
        if (matches.length === 0) {
            host.innerHTML = '<p class="gst-muted">No registered channel resolves to this collection.</p>';
            return;
        }
        var byChannel = {};
        matches.forEach(function (m) {
            (byChannel[m.channelKey] = byChannel[m.channelKey] || []).push(m.locale);
        });
        var items = Object.keys(byChannel).sort().map(function (pk) {
            var locales = byChannel[pk].slice().sort();
            var localesHtml = locales.map(function (l) {
                return '<code class="gst-locale-chip">' + GST.escHtml(l) + '</code>';
            }).join(' ');
            return '<li class="gst-colfly-profrow">' +
                '<a class="gst-table__link" href="/EPiServer/cms/graphsearchtools/channels?key=' +
                encodeURIComponent(pk) + '">' + GST.escHtml(pk) + '</a>' +
                ' <span class="gst-muted">via</span> ' + localesHtml +
                '</li>';
        }).join('');
        host.innerHTML = '<ul class="gst-colfly-proflist">' + items + '</ul>';
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
        var isTop = !(state.scope && state.scope.channelKey);

        // Reset any leftover transient scope so a previous open/cancel can't
        // leak into this one.
        state.flyoutScope = null;

        // Top-level: surface the collection picker and seed a flyout-only
        // scope with the chosen collection's id. Audit attribution is
        // best-effort — when a registered channel resolves to the picked
        // collection key, its channelKey rides along in scopeQs(); orphan
        // collections write through without audit (backend tolerates that).
        var collectionRow = document.getElementById('gst-pinfly-collection-row');
        var collectionSel = document.getElementById('gst-pinfly-collection');
        if (collectionRow && collectionSel) {
            if (isTop) {
                if (!state.collections.length) {
                    window.alert('Create a collection before adding pins.');
                    return;
                }
                collectionRow.hidden = false;
                var sortedCollections = state.collections.slice().sort(function (a, b) {
                    return (a.key || '').localeCompare(b.key || '');
                });
                collectionSel.innerHTML = sortedCollections.map(function (c) {
                    var label = c.key || c.id;
                    return '<option value="' + GST.escHtml(c.id) + '">' + GST.escHtml(label) + '</option>';
                }).join('');
                // Honor the grid's current collection filter as the default
                // so "Add" from a filtered view stays in that collection.
                var defaultId = (state.filters.collectionId && state.collections.some(function (c) { return c.id === state.filters.collectionId; }))
                    ? state.filters.collectionId
                    : sortedCollections[0].id;
                collectionSel.value = defaultId;
                state.flyoutScope = buildFlyoutScopeForCollection(defaultId);
                collectionSel.onchange = function () {
                    state.flyoutScope = buildFlyoutScopeForCollection(collectionSel.value);
                    var c = state.collections.find(function (x) { return x.id === collectionSel.value; });
                    if (c && state.editing) {
                        state.editing.group.collectionId = c.id;
                        state.editing.group.collectionKey = c.key || c.id;
                    }
                    populateFlyout(state.editing.group);
                };
            } else {
                collectionRow.hidden = true;
            }
        }

        // Pick a sensible default collection + locale. Effective scope = the
        // flyout-only scope if present, else the (real) state.scope.
        var effScope = state.flyoutScope || state.scope || null;
        var defaultLocale = state.filters.locale || '';
        var col = null;
        // Top-level: the collection picker already populated flyoutScope
        // with a concrete collectionId — honor it so the default lines up
        // with whatever the dropdown shows.
        if (isTop && effScope && effScope.collectionId) {
            col = state.collections.find(function (c) { return c.id === effScope.collectionId; }) || null;
        }
        var byLoc = (effScope && effScope.collectionsByLocale) || null;
        if (!col && !isTop && state.filters.collectionId) {
            col = state.collections.find(function (c) { return c.id === state.filters.collectionId; }) || null;
        }
        if (!col && byLoc) {
            if (defaultLocale && byLoc[defaultLocale]) {
                col = state.collections.find(function (c) { return c.id === byLoc[defaultLocale]; }) || null;
            }
            if (!col) {
                Object.keys(byLoc).some(function (l) {
                    if (byLoc[l]) {
                        col = state.collections.find(function (c) { return c.id === byLoc[l]; }) || null;
                        if (col) { defaultLocale = defaultLocale || l; return true; }
                    }
                    return false;
                });
            }
        }
        if (!col) {
            var firstLocale = defaultLocale || (effScope && effScope.locales && effScope.locales[0]) || '';
            defaultLocale = defaultLocale || firstLocale;
            col = {
                id: '',
                key: '__pending-' + firstLocale
            };
        }
        state.editing = {
            mode: 'create',
            group: {
                key: '',
                phrase: '',
                collectionId: col.id || '',
                collectionKey: col.key || col.id,
                locale: defaultLocale,
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
        // Collection picker only on top-level Create. Edit shows the
        // collection as part of the matching-channels panel below.
        const collectionRow = document.getElementById('gst-pinfly-collection-row');
        if (collectionRow) {
            const showPicker = state.editing.mode === 'create'
                && !(state.scope && state.scope.channelKey);
            collectionRow.hidden = !showPicker;
        }
        // Matching-channels panel only on the top-level Pinned page when
        // editing an existing pin. Channel detail already has the channel
        // context in the page chrome, and create-mode hasn't picked a
        // collection yet for create flows.
        const channelsRow = document.getElementById('gst-pinfly-channels-row');
        const channelsHost = document.getElementById('gst-pinfly-channels');
        if (channelsRow && channelsHost) {
            const isTopEdit = state.editing.mode === 'edit'
                && !(state.scope && state.scope.channelKey);
            if (isTopEdit) {
                renderMatchingChannels(channelsHost, g.collectionKey || '');
                channelsRow.hidden = false;
            } else {
                channelsRow.hidden = true;
                channelsHost.innerHTML = '';
            }
        }
        document.getElementById('gst-pinfly-phrase').value = g.phrase || '';
        // Locale dropdown — populate from collections + groups so any locale
        // the tenant uses is selectable. Repopulating per-open keeps the list
        // fresh if a new locale appeared.
        const localeSel = document.getElementById('gst-pinfly-locale');
        const knownLocales = collectLocales();
        if (g.locale && knownLocales.indexOf(g.locale) === -1) knownLocales.unshift(g.locale);
        // Empty value maps to Graph's null Language ("applies regardless of
        // locale"). Label it explicitly so it doesn't read as "no choice yet".
        const allLabel = GST.escHtml(GST.s('pinFlyout.localeAll', 'Global'));
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
        // Surface declared-but-empty locales so the flyout picker offers them
        // even before any pin exists for that locale — first save against one
        // of these triggers EnsureCollection. The flyout-scope wins when set
        // (top-level + Add Pin); else fall back to the mount-time scope.
        var s = effectiveScope();
        if (s && Array.isArray(s.locales)) {
            s.locales.forEach(function (l) { if (l) seen[l] = true; });
        }
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
            notifyPreviewChanged();
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

        // EnsureCollection — when this channel/locale pair has no backing
        // collection yet (collectionsByLocale[newLocale] is null), materialize
        // it server-side before the create/update ops run. The new id replaces
        // g.collectionId on the editing group so runOps and diff use it.
        ensureCollectionForLocale(newLocale).then(function (collectionId) {
            if (collectionId) g.collectionId = collectionId;

            const ops = diff(state.editing.originals, state.editing.targets, {
                phrases: newPhrase,
                language: newLocale,
                collectionId: g.collectionId
            });

            return runOps(ops, btn, g.collectionId).then(function (errors) {
                if (errors > 0) {
                    window.alert(GST.s('pinned.save_failed', '%1 of %2 changes failed.')
                        .replace('%1', errors).replace('%2', ops.length));
                }
                state.flyoutScope = null;     // transient scope ends here
                GST.flyout.close('pin');
                loadAll();
                notifyPreviewChanged();
            });
        }).catch(function (err) {
            state.flyoutScope = null;
            window.alert(GST.s('pinned.save_failed_generic', 'Save failed: ') + (err && err.message || ''));
        });
    }

    // Resolve the (channel, locale) tuple to a collection id, creating the
    // collection server-side if it doesn't exist. Caches the result back into
    // the active scope's collectionsByLocale so subsequent saves for the same
    // locale skip the round-trip. No-op when neither scope is channel-scoped.
    function ensureCollectionForLocale(locale) {
        var s = effectiveScope();
        // Direct-collection flyout scope already has a known id — skip the
        // EnsureCollection round-trip and use it verbatim. The save flow
        // overwrites group.collectionId with this return value, so passing
        // the existing id keeps everything pointing at the picked collection.
        if (s && s.collectionId) return Promise.resolve(s.collectionId);
        if (!s || !s.channelKey) return Promise.resolve('');
        var map = s.collectionsByLocale || (s.collectionsByLocale = {});
        var existing = map[locale];
        if (existing) return Promise.resolve(existing);

        var url = API + '/EnsureCollection'
            + '?channelKey=' + encodeURIComponent(s.channelKey)
            + '&locale=' + encodeURIComponent(locale || '');
        return GST.postJson(url, {}).then(function (resp) {
            if (resp && resp.collectionId) {
                map[locale] = resp.collectionId;
                return resp.collectionId;
            }
            return '';
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

    // Expose `init` so the Channel detail view can mount a scoped instance
    // before DOMContentLoaded fires. The auto-init handler above bails when
    // `state.initialized` is already true, so calling `init({ scope })`
    // pre-empts the unscoped default.
    window.GST = window.GST || {};
    window.GST.pinned = window.GST.pinned || {};
    window.GST.pinned.aurora = { init: init };
})();
