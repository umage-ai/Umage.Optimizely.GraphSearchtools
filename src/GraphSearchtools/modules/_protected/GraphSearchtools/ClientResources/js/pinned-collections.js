/*
 * Pinned → Collections tab.
 *
 * Read-only browser over every Graph pinned collection, decorated with
 * which registered channel (× locale) resolves to each collection via
 * SearchChannel.PinnedKeyForLocale.
 *
 * Tab switcher is page-level and mounts both panels' state lazily:
 * pinned-aurora.js owns the Pins panel; this module owns Collections.
 */
(function () {
    const API = window.GST_BASE_URL + '/PinnedApi';

    const state = {
        initialized: false,
        collections: [],          // raw PinnedCollectionResult rows
        channelMatches: {},       // { collectionKey: [{channelKey, locale}, ...] }
        sort: { key: 'key', dir: 'asc' },
        filters: { q: '' },
        // Collection currently rendered in the detail flyout — the delete
        // button reads this rather than a closure so the click handler can
        // be wired once at init time.
        flyoutCollection: null
    };

    document.addEventListener('DOMContentLoaded', autoInit);
    // AJAX tool-switch (see graphsearchtools.js → "Smooth tool-switch
    // navigation"): the Pinned page may have just been swapped in. Reset
    // the closure state and re-wire — old listeners point at detached nodes.
    document.addEventListener('gst:pageswapped', function () {
        state.initialized = false;
        autoInit();
    });

    function autoInit() {
        // No-op on pages that don't have Pinned tabs.
        if (!document.querySelector('.gst-tabs__btn[data-tab="collections"]')) return;
        wireTabs();
        // Defer data load until the Collections tab is first activated —
        // most editors land on Pins and never open this one.
    }

    // ── Tab switcher (page-level Pinned tabs: Pins / Collections) ──────
    function wireTabs() {
        const buttons = document.querySelectorAll('.gst-tabs__btn[data-tab]');
        const panels = document.querySelectorAll('.gst-tabpanel[data-tab]');
        if (!buttons.length || !panels.length) return;

        function activate(tab) {
            buttons.forEach(function (b) {
                const on = b.dataset.tab === tab;
                b.classList.toggle('is-active', on);
                b.setAttribute('aria-selected', on ? 'true' : 'false');
            });
            panels.forEach(function (p) {
                const on = p.dataset.tab === tab;
                p.hidden = !on;
            });
            if (tab === 'collections' && !state.initialized) {
                init();
            }
        }

        buttons.forEach(function (b) {
            b.addEventListener('click', function () { activate(b.dataset.tab); });
        });

        // Initial state from URL hash (#collections jumps straight in).
        const hash = (window.location.hash || '').replace('#', '');
        if (hash === 'collections') activate('collections');
    }

    // ── Init: load collections + channel matches in parallel ──────────
    function init() {
        if (state.initialized) return;
        state.initialized = true;

        const search = document.getElementById('gst-col-search');
        if (search) search.addEventListener('input', function () {
            state.filters.q = search.value.trim().toLowerCase();
            renderGrid();
        });

        const createBtn = document.getElementById('gst-col-create');
        if (createBtn) createBtn.addEventListener('click', openCreateFlyout);
        const saveBtn = document.getElementById('gst-colcreate-save');
        if (saveBtn) saveBtn.addEventListener('click', onCreateSave);
        const deleteBtn = document.getElementById('gst-colfly-delete');
        if (deleteBtn) deleteBtn.addEventListener('click', onDeleteCollection);

        document.querySelectorAll('.gst-col-aurora-table thead th[data-sort]').forEach(function (th) {
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

        const tbody = document.getElementById('gst-col-aurora-rows');
        renderLoading(tbody);

        Promise.all([
            GST.fetchJson(API + '/Collections').catch(function () { return []; }),
            GST.fetchJson(API + '/CollectionChannels').catch(function () { return {}; })
        ]).then(function (results) {
            state.collections = results[0] || [];
            state.channelMatches = results[1] || {};
            // For each collection, fetch a cheap item-count probe. Pinned API
            // doesn't expose count directly, so we walk AllItems — ok for now,
            // the prototype dataset is small.
            return Promise.all(state.collections.map(function (col) {
                return GST.fetchJson(API + '/AllItems?collectionId=' + encodeURIComponent(col.id))
                    .then(function (resp) { return { col: col, count: ((resp && resp.items) || []).length }; })
                    .catch(function () { return { col: col, count: 0 }; });
            }));
        }).then(function (bundles) {
            state.collections = bundles.map(function (b) {
                return Object.assign({}, b.col, { __itemCount: b.count });
            });
            renderGrid();
        }).catch(function (err) {
            GST.alert('Could not load collections. ' + (err && err.message || ''), 'danger');
        });
    }

    // ── Render ─────────────────────────────────────────────────────────
    function renderLoading(tbody) {
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="5" class="gst-empty"><p>Loading…</p></td></tr>';
    }

    function renderGrid() {
        const tbody = document.getElementById('gst-col-aurora-rows');
        if (!tbody) return;

        const q = state.filters.q;
        let rows = state.collections.filter(function (c) {
            if (!q) return true;
            return ((c.key || '') + ' ' + (c.id || ''))
                .toLowerCase().indexOf(q) !== -1;
        });

        rows.sort(function (a, b) {
            const k = state.sort.key, dir = state.sort.dir === 'asc' ? 1 : -1;
            switch (k) {
                case 'items':   return (a.__itemCount - b.__itemCount) * dir;
                case 'channels':
                    return (matchCount(a.key) - matchCount(b.key)) * dir;
                case 'updated':
                    return ((a.updatedAt || '') > (b.updatedAt || '') ? 1 : -1) * dir;
                default:
                    return ((a.key || '').toLowerCase() > (b.key || '').toLowerCase() ? 1 : -1) * dir;
            }
        });

        if (rows.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" class="gst-empty"><p>No collections found.</p></td></tr>';
            return;
        }

        tbody.innerHTML = rows.map(function (c) {
            const matches = state.channelMatches[c.key] || [];
            const channelCell = matches.length === 0
                ? '<span class="gst-muted">—</span>'
                : matches
                    .map(function (m) { return GST.escHtml(m.channelKey); })
                    .filter(uniq).join(', ');
            return '<tr class="is-selectable" data-col-id="' + GST.escHtml(c.id) + '">' +
                '<td><a href="#" class="gst-table__link" data-row-link>' + GST.escHtml(c.key || c.id) + '</a></td>' +
                '<td>' + c.__itemCount + '</td>' +
                '<td>' + channelCell + '</td>' +
                '<td>' + GST.escHtml(fmtDate(c.updatedAt)) + '</td>' +
                '<td class="gst-table__actions"></td>' +
                '</tr>';
        }).join('');

        tbody.querySelectorAll('tr.is-selectable').forEach(function (tr) {
            tr.addEventListener('click', function () { openFlyout(tr.dataset.colId); });
        });
    }

    function uniq(value, idx, arr) { return arr.indexOf(value) === idx; }

    function matchCount(collectionKey) {
        const list = state.channelMatches[collectionKey] || [];
        // Distinct channels, not (channel,locale) pairs — sort by "how many
        // channels are competing for this collection," which is the question
        // an editor scanning the grid actually has.
        return list.filter(function (m, i, all) {
            return all.findIndex(function (x) { return x.channelKey === m.channelKey; }) === i;
        }).length;
    }

    function fmtDate(s) {
        if (!s) return '—';
        try {
            const d = new Date(s);
            if (isNaN(d.getTime())) return s;
            return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        } catch (_) { return s; }
    }

    // ── Flyout ─────────────────────────────────────────────────────────
    function openFlyout(collectionId) {
        const col = state.collections.find(function (c) { return c.id === collectionId; });
        if (!col) return;
        state.flyoutCollection = col;

        document.getElementById('gst-flyout-col-title').textContent = col.key || 'Collection';
        document.getElementById('gst-colfly-key').textContent = col.key || '—';
        document.getElementById('gst-colfly-id').textContent = col.id || '—';
        document.getElementById('gst-colfly-items').textContent = String(col.__itemCount || 0);

        const matchesEl = document.getElementById('gst-colfly-channels');
        const matches = state.channelMatches[col.key] || [];
        if (matches.length === 0) {
            matchesEl.innerHTML = '<p class="gst-muted">No registered channel resolves to this collection.</p>';
        } else {
            // Group by channelKey → sorted locales.
            const byChannel = {};
            matches.forEach(function (m) {
                (byChannel[m.channelKey] = byChannel[m.channelKey] || []).push(m.locale);
            });
            const items = Object.keys(byChannel).sort().map(function (pk) {
                const locales = byChannel[pk].slice().sort();
                const localesHtml = locales.map(function (l) {
                    return '<code class="gst-locale-chip">' + GST.escHtml(l) + '</code>';
                }).join(' ');
                return '<li class="gst-colfly-profrow">' +
                    '<a class="gst-table__link" href="' + (window.GST_BASE_URL || '') + '/Channels/Index?key=' + encodeURIComponent(pk) + '">' + GST.escHtml(pk) + '</a>' +
                    ' <span class="gst-muted">via</span> ' + localesHtml +
                '</li>';
            }).join('');
            matchesEl.innerHTML = '<ul class="gst-colfly-proflist">' + items + '</ul>';
        }

        if (GST.flyout) {
            GST.flyout.open('collection');
        } else {
            document.getElementById('gst-flyout-collection-backdrop').hidden = false;
            document.getElementById('gst-flyout-collection').hidden = false;
        }
    }

    // ── Create-collection flyout ───────────────────────────────────────
    function openCreateFlyout() {
        document.getElementById('gst-colcreate-key').value = '';
        document.getElementById('gst-colcreate-active').checked = true;
        GST.flyout.open('colcreate');
        setTimeout(function () {
            var el = document.getElementById('gst-colcreate-key');
            if (el) el.focus();
        }, 0);
    }

    function onCreateSave() {
        var key = (document.getElementById('gst-colcreate-key').value || '').trim();
        var isActive = document.getElementById('gst-colcreate-active').checked;

        if (!key) {
            window.alert('Collection key is required.');
            return;
        }
        if (state.collections.some(function (c) {
            return (c.key || '').toLowerCase() === key.toLowerCase();
        })) {
            window.alert('A collection with that key already exists.');
            return;
        }

        var btn = document.getElementById('gst-colcreate-save');
        btn.disabled = true;
        var prev = btn.textContent;
        btn.textContent = 'Creating…';

        GST.postJson(API + '/CreateCollection', { key: key, isActive: isActive })
            .then(function (created) {
                state.collections.push(Object.assign({}, created, { __itemCount: 0 }));
                renderGrid();
                GST.flyout.close('colcreate');
            })
            .catch(function (err) {
                window.alert('Create failed: ' + (err && err.message || ''));
            })
            .then(function () {
                btn.disabled = false;
                btn.textContent = prev;
            });
    }

    // Delete the collection currently shown in the detail flyout. Destroys
    // both the collection and every pin inside it on the Graph side, so we
    // confirm with the live item count and a stronger warning when any
    // registered channel still resolves here — those channels would lose
    // their pinned slot until a replacement collection is created with the
    // same key.
    function onDeleteCollection() {
        var col = state.flyoutCollection;
        if (!col || !col.id) return;
        var itemCount = col.__itemCount || 0;
        var matches = state.channelMatches[col.key] || [];
        var channelCount = matches
            .map(function (m) { return m.channelKey; })
            .filter(function (v, i, all) { return all.indexOf(v) === i; })
            .length;

        var msg = 'Delete collection "' + (col.key || col.id) + '"?';
        if (itemCount > 0) {
            msg += '\n\nThis will also delete ' + itemCount + ' pin' + (itemCount === 1 ? '' : 's') + ' inside it.';
        }
        if (channelCount > 0) {
            msg += '\n\n' + channelCount + ' channel' + (channelCount === 1 ? '' : 's')
                + ' resolve to this collection and will lose their pinned slot.';
        }
        msg += '\n\nThis cannot be undone.';
        if (!window.confirm(msg)) return;

        var btn = document.getElementById('gst-colfly-delete');
        btn.disabled = true;
        var prev = btn.textContent;
        btn.textContent = 'Deleting…';

        fetch(API + '/DeleteCollection?id=' + encodeURIComponent(col.id), {
            method: 'DELETE',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        }).then(function (r) {
            if (!r.ok && r.status !== 204) throw new Error('HTTP ' + r.status);
            state.collections = state.collections.filter(function (c) { return c.id !== col.id; });
            state.flyoutCollection = null;
            renderGrid();
            GST.flyout.close('collection');
        }).catch(function (err) {
            window.alert('Delete failed: ' + (err && err.message || ''));
        }).then(function () {
            btn.disabled = false;
            btn.textContent = prev;
        });
    }
})();
