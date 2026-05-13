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
    const PINNED_STRINGS = (window.GST_STRINGS && window.GST_STRINGS.pinned) || {};

    // ── Page state ─────────────────────────────────────────────────────
    const state = {
        collections: [],
        items: [],          // flat list of every pinned item, normalised
        groups: [],         // aggregated rows
        targetNames: {},    // guidLower → name
        sort: { key: 'modified', dir: 'desc' },
        filters: { q: '', collectionId: '', locale: '' },
        editing: null       // current group being edited in the flyout
    };

    document.addEventListener('DOMContentLoaded', init);

    function init() {
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
                state.collections = collections || [];
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
                renderGrid();
            })
            .catch(function (err) {
                console.error('Aurora pinned load failed', err);
                const tbody = document.getElementById('gst-pin-aurora-rows');
                tbody.innerHTML = '<tr><td colspan="7" class="gst-empty"><p>' +
                    GST.escHtml(GST.s('pinned.load_failed', 'Could not load pinned items.')) +
                    '</p></td></tr>';
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
                    modified: ''
                };
            }
            const g = groups[key];
            g.items.push(it);
            if (it.updatedAt && it.updatedAt > g.modified) g.modified = it.updatedAt;
        });
        state.groups = Object.keys(groups).map(function (k) {
            const g = groups[k];
            g.items.sort(function (a, b) { return (a.priority || 0) - (b.priority || 0); });
            g.activeCount = g.items.filter(function (i) { return i.isActive; }).length;
            g.state = computeState(g);
            return g;
        });
    }

    function computeState(g) {
        // Past effective-to on any item → Expired; otherwise Mixed/Active/Inactive.
        const now = new Date();
        const expired = g.items.some(function (i) {
            if (!i.effectiveTo) return false;
            const t = new Date(i.effectiveTo);
            return !isNaN(t.getTime()) && t < now;
        });
        if (expired) return 'expired';
        if (g.activeCount === g.items.length) return 'active';
        if (g.activeCount === 0) return 'inactive';
        return 'mixed';
    }

    function populateCollectionFilter() {
        const sel = document.getElementById('gst-pin-collection-filter');
        if (!sel) return;
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
        state.groups.forEach(function (g) {
            const l = g.locale || '';
            if (l && !seen[l]) { seen[l] = true; locales.push(l); }
        });
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
        tbody.innerHTML = '<tr><td colspan="7" class="gst-muted">' + GST.escHtml(msg) + '</td></tr>';
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
            tbody.innerHTML = '<tr><td colspan="7" class="gst-empty"><p>' +
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
                '<td>' + GST.escHtml(g.locale || '') + '</td>' +
                '<td>' + GST.escHtml(itemsTmpl.replace('%1', g.items.length)) + '</td>' +
                '<td>' + renderStateBadge(g.state) + '</td>' +
                '<td>' + GST.escHtml(formatWhen(g.modified)) + '</td>' +
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
    // Delete button via onDelete). Confirms before issuing the requests.
    function deleteGroup(groupKey) {
        const g = state.groups.find(function (x) { return x.key === groupKey; });
        if (!g) return;
        if (!window.confirm(GST.s('pinned.confirm_delete', 'Delete this pinned item?'))) return;
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
            }
            loadAll();
        });
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
                case 'state':      va = a.state; vb = b.state; break;
                case 'modified':
                default:           va = a.modified || ''; vb = b.modified || ''; break;
            }
            if (va < vb) return -1 * dir;
            if (va > vb) return 1 * dir;
            return 0;
        });
    }

    function renderStateBadge(s) {
        const label = GST.s('pinned.state_' + s, s);
        const cls = s === 'active' ? 'gst-badge--success'
            : s === 'inactive' ? 'gst-badge--default'
            : s === 'expired' ? 'gst-badge--danger'
            : 'gst-badge--warning';
        return '<span class="gst-badge ' + cls + '">' + GST.escHtml(label) + '</span>';
    }

    function formatWhen(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        if (isNaN(d.getTime())) return iso;
        const pad = function (n) { return n < 10 ? '0' + n : '' + n; };
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
            ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes());
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
        localeSel.innerHTML = '<option value=""></option>' + knownLocales.map(function (l) {
            return '<option value="' + GST.escHtml(l) + '"' + (l === g.locale ? ' selected' : '') + '>' + GST.escHtml(l) + '</option>';
        }).join('');
        // Effective until — use the first item's effectiveTo (if any).
        const first = g.items[0];
        document.getElementById('gst-pinfly-effective').value =
            first && first.effectiveTo ? String(first.effectiveTo).slice(0, 10) : '';
        // Active checkbox — true if any item is active.
        document.getElementById('gst-pinfly-active').checked = !first || first.isActive !== false;

        renderTargetList();
        checkConflicts(g.phrase || '', g.locale || '');
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
                GST.escHtml(GST.s('pinFlyout.targetsHint', '')) + '</div>';
            return;
        }
        host.innerHTML = targets.map(function (t, idx) {
            const sub = t.targetKey && t.targetKey !== t.name ? t.targetKey : '';
            return '<div class="gst-target-row" draggable="true" data-idx="' + idx + '">' +
                '<span class="gst-target-row__drag" aria-hidden="true"></span>' +
                '<span class="gst-target-row__body">' +
                    '<span class="gst-target-row__name">' + GST.escHtml(t.name || t.targetKey || '(empty)') + '</span>' +
                    (sub ? '<span class="gst-target-row__sub">' + GST.escHtml(sub) + '</span>' : '') +
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
        if (!window.confirm(GST.s('pinned.confirm_delete', 'Delete this pinned item?'))) return;
        const btn = e.currentTarget;
        const ops = g.items.map(function (it) {
            return { kind: 'delete', id: it.id, collectionId: it.collectionId };
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

    // ── Save ───────────────────────────────────────────────────────────

    function onSave(e) {
        e.preventDefault();
        if (!state.editing) return;
        const btn = e.currentTarget;
        const g = state.editing.group;
        const newPhrase = document.getElementById('gst-pinfly-phrase').value.trim();
        const newLocale = document.getElementById('gst-pinfly-locale').value.trim();
        const newEffective = document.getElementById('gst-pinfly-effective').value.trim() || null;
        const newActive = document.getElementById('gst-pinfly-active').checked;

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
                    return GST.postJson(API + '/CreateItem?collectionId=' + encodeURIComponent(op.collectionId), op.payload);
                case 'update':
                    return fetch(API + '/UpdateItem?collectionId=' + encodeURIComponent(op.collectionId) +
                        '&id=' + encodeURIComponent(op.id), {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                        body: JSON.stringify(op.payload)
                    }).then(function (r) {
                        if (!r.ok) throw new Error('Update failed ' + r.status);
                        return r.json().catch(function () { return null; });
                    });
                case 'delete':
                    return fetch(API + '/DeleteItem?collectionId=' + encodeURIComponent(op.collectionId) +
                        '&id=' + encodeURIComponent(op.id), {
                        method: 'DELETE',
                        headers: { 'X-Requested-With': 'XMLHttpRequest' }
                    }).then(function (r) {
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
})();
