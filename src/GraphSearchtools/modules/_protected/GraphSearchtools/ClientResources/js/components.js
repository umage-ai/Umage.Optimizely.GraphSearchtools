/**
 * Graph Search Tools - Reusable UI Components
 *
 * Content Picker:  GST.contentPicker(opts)  → Promise<{id, name}>
 * Content Type Picker: GST.contentTypePicker(opts) → Promise<{id, name, displayName}>
 * Flyout:          GST.flyout.open(key, opts) / GST.flyout.close(key)
 * Row menu:        GST.rowMenu(anchor, [{ label, onSelect, danger? }, ...])
 */
(function () {
    const API = window.GST_BASE_URL + 'ComponentsApi';

    // ── Row menu (Aurora ⋯ popover) ────────────────────────────────────
    // Anchors a small popover beneath the ⋯ button and renders a list of
    // labelled actions. Closes on outside-click / Esc. One menu is open at
    // a time; opening a second auto-dismisses the first.
    let _openMenu = null;

    GST.rowMenu = function (anchor, items) {
        if (!anchor || !items || items.length === 0) return;
        if (_openMenu) { _openMenu.close(); _openMenu = null; }

        const menu = document.createElement('div');
        menu.className = 'gst-rowmenu__popover';
        menu.setAttribute('role', 'menu');
        items.forEach(function (it) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'gst-rowmenu__item' + (it.danger ? ' gst-rowmenu__item--danger' : '');
            btn.setAttribute('role', 'menuitem');
            btn.textContent = it.label;
            btn.addEventListener('click', function (e) {
                e.stopPropagation();
                close();
                if (typeof it.onSelect === 'function') it.onSelect();
            });
            menu.appendChild(btn);
        });

        // Position the popover under the anchor's right edge so the menu
        // hangs into the row's interior rather than off the right edge of
        // the table — which is where the anchor itself sits.
        const rect = anchor.getBoundingClientRect();
        menu.style.position = 'fixed';
        menu.style.top = (rect.bottom + 4) + 'px';
        menu.style.left = 'auto';
        menu.style.right = (window.innerWidth - rect.right) + 'px';
        document.body.appendChild(menu);

        function onDocClick(e) {
            if (menu.contains(e.target) || anchor.contains(e.target)) return;
            close();
        }
        function onKey(e) { if (e.key === 'Escape') close(); }

        function close() {
            if (menu.parentNode) menu.parentNode.removeChild(menu);
            document.removeEventListener('click', onDocClick, true);
            document.removeEventListener('keydown', onKey);
            _openMenu = null;
        }

        // Defer the outside-click listener to the next tick so the click
        // that opened the menu doesn't immediately close it.
        setTimeout(function () {
            document.addEventListener('click', onDocClick, true);
            document.addEventListener('keydown', onKey);
        }, 0);

        _openMenu = { close: close };
        return _openMenu;
    };

    // ── Flyout ─────────────────────────────────────────────────────
    // Aurora-style right-edge edit panel. The flyout's DOM is pre-rendered
    // on the page (typically by a Razor partial — _PinFlyout.cshtml /
    // _SynonymFlyout.cshtml) with these conventional IDs:
    //
    //     <div id="gst-flyout-{key}-backdrop" class="gst-flyout-backdrop" hidden></div>
    //     <aside id="gst-flyout-{key}" class="gst-flyout" hidden ...>...</aside>
    //
    // The helper reveals/hides them, traps focus, restores focus on close,
    // and dismisses on Esc or backdrop-click. Only one flyout per key.
    const flyoutState = {}; // key -> { lastFocus, onKey, onTab }

    function flyoutEls(key) {
        return {
            panel: document.getElementById('gst-flyout-' + key),
            backdrop: document.getElementById('gst-flyout-' + key + '-backdrop')
        };
    }

    function focusables(panel) {
        return Array.prototype.filter.call(
            panel.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'),
            function (el) { return el.offsetParent !== null || el === document.activeElement; }
        );
    }

    GST.flyout = {
        /**
         * Open the flyout identified by `key`. Markup must already exist
         * on the page. Options:
         *   onOpen({ panel, key }):  called after the panel is revealed,
         *                            before focus moves to the first field.
         *                            Use this to populate fields from a row.
         *   focus:                   selector inside the panel to focus
         *                            first. Defaults to the first
         *                            textarea/input/select.
         *   onClose({ panel, key }): called after the panel is hidden,
         *                            before focus restores.
         */
        open: function (key, opts) {
            opts = opts || {};
            const els = flyoutEls(key);
            if (!els.panel || !els.backdrop) {
                console.warn('GST.flyout.open: no markup for key', key);
                return;
            }
            // Already open: re-run onOpen (so callers can refresh fields)
            // but don't re-bind listeners or re-record lastFocus.
            const reopening = !els.panel.hidden;
            if (!reopening) {
                flyoutState[key] = flyoutState[key] || {};
                flyoutState[key].lastFocus = document.activeElement;
            }

            els.backdrop.hidden = false;
            els.panel.hidden = false;

            if (typeof opts.onOpen === 'function') {
                opts.onOpen({ panel: els.panel, key: key });
            }

            // Focus the first interactive control in the panel so keyboard
            // users land where they can type immediately.
            setTimeout(function () {
                let target = null;
                if (opts.focus) target = els.panel.querySelector(opts.focus);
                if (!target) target = els.panel.querySelector('textarea, input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]), select');
                if (target) target.focus();
            }, 0);

            if (reopening) return;

            const state = flyoutState[key];
            const onBackdrop = function () { GST.flyout.close(key, opts); };
            const onKey = function (e) {
                if (e.key === 'Escape') {
                    e.stopPropagation();
                    GST.flyout.close(key, opts);
                }
            };
            const onTab = function (e) {
                if (e.key !== 'Tab') return;
                // Focus trap — only redirect if focus would leave the panel.
                const items = focusables(els.panel);
                if (!items.length) return;
                const first = items[0];
                const last = items[items.length - 1];
                if (e.shiftKey && document.activeElement === first) {
                    e.preventDefault();
                    last.focus();
                } else if (!e.shiftKey && document.activeElement === last) {
                    e.preventDefault();
                    first.focus();
                }
            };

            state.onBackdrop = onBackdrop;
            state.onKey = onKey;
            state.onTab = onTab;
            state.opts = opts;
            els.backdrop.addEventListener('click', onBackdrop);
            document.addEventListener('keydown', onKey);
            els.panel.addEventListener('keydown', onTab);
        },

        close: function (key, opts) {
            const els = flyoutEls(key);
            if (!els.panel || els.panel.hidden) return;
            els.backdrop.hidden = true;
            els.panel.hidden = true;

            const state = flyoutState[key];
            if (state) {
                if (state.onBackdrop) els.backdrop.removeEventListener('click', state.onBackdrop);
                if (state.onKey) document.removeEventListener('keydown', state.onKey);
                if (state.onTab) els.panel.removeEventListener('keydown', state.onTab);
            }

            const closeOpts = opts || (state && state.opts) || {};
            if (typeof closeOpts.onClose === 'function') {
                closeOpts.onClose({ panel: els.panel, key: key });
            }

            if (state && state.lastFocus && typeof state.lastFocus.focus === 'function') {
                state.lastFocus.focus();
            }
            if (state) {
                state.onBackdrop = null;
                state.onKey = null;
                state.onTab = null;
                state.lastFocus = null;
                state.opts = null;
            }
        },

        isOpen: function (key) {
            const els = flyoutEls(key);
            return !!(els.panel && !els.panel.hidden);
        }
    };

    // ── Content Picker ─────────────────────────────────────────────
    /**
     * Opens a dialog with a content tree browser + search.
     * Returns a promise that resolves with the selected content item, or null if cancelled.
     *
     * Options:
     *   rootId: number (default 0 = RootPage)
     *   title: string (default "Select Content")
     */
    GST.contentPicker = function (opts = {}) {
        return new Promise((resolve) => {
            const rootId = opts.rootId || 0;
            const { body, close } = GST.openDialog(opts.title || GST.s('components.picker_title', 'Select Content'), { wide: false });

            let selectedItem = null;
            let mode = 'tree'; // 'tree' or 'search'

            // Build UI
            body.innerHTML = `
                <div class="gst-search gst-mb-md">
                    <span class="gst-search__icon">${GST.icons.search}</span>
                    <input type="text" class="gst-picker-search" placeholder="${GST.s('components.picker_search', 'Search content by name...')}" style="width:100%" />
                </div>
                <div class="gst-picker-tree" style="max-height:400px;overflow-y:auto"></div>
                <div class="gst-picker-results gst-hidden" style="max-height:400px;overflow-y:auto"></div>
                <div class="gst-flex gst-mt-md" style="justify-content:flex-end;gap:8px">
                    <button class="gst-btn gst-picker-cancel">${GST.s('components.btn_cancel', 'Cancel')}</button>
                    <button class="gst-btn gst-btn--primary gst-picker-select" disabled>${GST.s('components.btn_select', 'Select')}</button>
                </div>
            `;

            const searchInput = body.querySelector('.gst-picker-search');
            const treeContainer = body.querySelector('.gst-picker-tree');
            const resultsContainer = body.querySelector('.gst-picker-results');
            const selectBtn = body.querySelector('.gst-picker-select');
            const cancelBtn = body.querySelector('.gst-picker-cancel');

            // Cancel
            cancelBtn.addEventListener('click', () => { close(); resolve(null); });

            // Select
            selectBtn.addEventListener('click', () => { close(); resolve(selectedItem); });

            // Search
            let searchTimeout;
            searchInput.addEventListener('input', () => {
                clearTimeout(searchTimeout);
                const q = searchInput.value.trim();
                if (q.length < 2) {
                    mode = 'tree';
                    treeContainer.classList.remove('gst-hidden');
                    resultsContainer.classList.add('gst-hidden');
                    return;
                }
                searchTimeout = setTimeout(() => searchContent(q), 300);
            });

            async function searchContent(q) {
                mode = 'search';
                treeContainer.classList.add('gst-hidden');
                resultsContainer.classList.remove('gst-hidden');
                GST.showLoading(resultsContainer);

                try {
                    const items = await GST.fetchJson(`${API}/SearchContent?q=${encodeURIComponent(q)}&rootId=${rootId}`);
                    if (items.length === 0) {
                        GST.showEmpty(resultsContainer, GST.s('components.picker_noresults', 'No results found'));
                        return;
                    }
                    renderSearchResults(items);
                } catch (err) {
                    resultsContainer.innerHTML = `<div class="gst-empty"><p>Error: ${err.message}</p></div>`;
                }
            }

            function renderSearchResults(items) {
                resultsContainer.innerHTML = '';
                const list = document.createElement('div');
                items.forEach(item => {
                    const row = document.createElement('div');
                    row.className = 'gst-picker-item';
                    row.innerHTML = `<strong>${esc(item.name)}</strong> <span class="gst-muted">(${esc(item.typeName)})</span>`;
                    row.addEventListener('click', () => selectItem(item, row));
                    list.appendChild(row);
                });
                resultsContainer.appendChild(list);
            }

            function selectItem(item, el) {
                body.querySelectorAll('.gst-picker-item--selected').forEach(e => e.classList.remove('gst-picker-item--selected'));
                el.classList.add('gst-picker-item--selected');
                selectedItem = item;
                selectBtn.disabled = false;
            }

            // Load tree
            loadTreeNode(treeContainer, rootId);

            async function loadTreeNode(container, parentId) {
                GST.showLoading(container);
                try {
                    const children = await GST.fetchJson(`${API}/GetChildren/${parentId}`);
                    container.innerHTML = '';
                    if (children.length === 0) {
                        container.innerHTML = '<div class="gst-muted" style="padding:8px">No children</div>';
                        return;
                    }

                    const ul = document.createElement('ul');
                    ul.className = 'gst-tree';

                    children.forEach(child => {
                        const li = document.createElement('li');
                        li.className = 'gst-tree__item';

                        const line = document.createElement('div');
                        line.className = 'gst-flex';

                        if (child.hasChildren) {
                            const toggle = document.createElement('button');
                            toggle.className = 'gst-tree__toggle';
                            toggle.innerHTML = GST.icons.chevronRight;
                            let expanded = false;
                            let childContainer = null;

                            toggle.addEventListener('click', (e) => {
                                e.stopPropagation();
                                expanded = !expanded;
                                toggle.innerHTML = expanded ? GST.icons.chevronDown : GST.icons.chevronRight;
                                if (expanded && !childContainer) {
                                    childContainer = document.createElement('div');
                                    childContainer.style.marginLeft = '20px';
                                    li.appendChild(childContainer);
                                    loadTreeNode(childContainer, child.id);
                                } else if (childContainer) {
                                    childContainer.style.display = expanded ? '' : 'none';
                                }
                            });
                            line.appendChild(toggle);
                        } else {
                            const spacer = document.createElement('span');
                            spacer.style.width = '24px';
                            spacer.style.display = 'inline-block';
                            line.appendChild(spacer);
                        }

                        const label = document.createElement('span');
                        label.className = 'gst-picker-item';
                        label.innerHTML = `${esc(child.name)} <span class="gst-muted" style="font-size:11px">${esc(child.typeName)}</span>`;
                        label.addEventListener('click', (e) => {
                            e.stopPropagation();
                            selectItem(child, label);
                        });
                        line.appendChild(label);

                        li.appendChild(line);
                        ul.appendChild(li);
                    });

                    container.appendChild(ul);
                } catch (err) {
                    container.innerHTML = `<div class="gst-empty"><p>Error: ${err.message}</p></div>`;
                }
            }
        });
    };

    // ── Content Type Picker ────────────────────────────────────────
    /**
     * Opens a dialog with a filterable content type list.
     * Returns a promise that resolves with the selected type, or null if cancelled.
     *
     * Options:
     *   title: string (default "Select Content Type")
     *   includeSystem: boolean (default false)
     */
    GST.contentTypePicker = function (opts = {}) {
        return new Promise(async (resolve) => {
            const { body, close } = GST.openDialog(opts.title || GST.s('components.typepicker_title', 'Select Content Type'));

            let selectedType = null;
            let allTypes = [];

            body.innerHTML = `
                <div class="gst-search gst-mb-md">
                    <span class="gst-search__icon">${GST.icons.search}</span>
                    <input type="text" class="gst-picker-search" placeholder="${GST.s('components.typepicker_search', 'Search content types...')}" style="width:100%" />
                </div>
                <div class="gst-picker-list" style="max-height:400px;overflow-y:auto"></div>
                <div class="gst-flex gst-mt-md" style="justify-content:flex-end;gap:8px">
                    <button class="gst-btn gst-picker-cancel">${GST.s('components.btn_cancel', 'Cancel')}</button>
                    <button class="gst-btn gst-btn--primary gst-picker-select" disabled>${GST.s('components.btn_select', 'Select')}</button>
                </div>
            `;

            const searchInput = body.querySelector('.gst-picker-search');
            const listContainer = body.querySelector('.gst-picker-list');
            const selectBtn = body.querySelector('.gst-picker-select');
            const cancelBtn = body.querySelector('.gst-picker-cancel');

            cancelBtn.addEventListener('click', () => { close(); resolve(null); });
            selectBtn.addEventListener('click', () => { close(); resolve(selectedType); });

            // Load types
            GST.showLoading(listContainer);
            try {
                allTypes = await GST.fetchJson(`${API}/GetContentTypes`);
                if (!opts.includeSystem) {
                    allTypes = allTypes.filter(t => !t.isSystemType);
                }
                renderTypes(allTypes);
            } catch (err) {
                listContainer.innerHTML = `<div class="gst-empty"><p>Error: ${err.message}</p></div>`;
            }

            // Filter
            searchInput.addEventListener('input', () => {
                const q = searchInput.value.trim().toLowerCase();
                const filtered = q
                    ? allTypes.filter(t => t.displayName.toLowerCase().includes(q) || t.name.toLowerCase().includes(q))
                    : allTypes;
                renderTypes(filtered);
            });

            function renderTypes(types) {
                listContainer.innerHTML = '';
                if (types.length === 0) {
                    GST.showEmpty(listContainer, GST.s('components.typepicker_noresults', 'No content types found'));
                    return;
                }

                // Group by GroupName
                const groups = {};
                types.forEach(t => {
                    const g = t.groupName || 'Other';
                    if (!groups[g]) groups[g] = [];
                    groups[g].push(t);
                });

                Object.keys(groups).sort().forEach(groupName => {
                    const header = document.createElement('div');
                    header.className = 'gst-muted';
                    header.style.cssText = 'font-size:11px;font-weight:600;padding:8px 12px 4px;text-transform:uppercase;letter-spacing:.5px';
                    header.textContent = groupName;
                    listContainer.appendChild(header);

                    groups[groupName].forEach(type => {
                        const row = document.createElement('div');
                        row.className = 'gst-picker-item';
                        row.innerHTML = `<strong>${esc(type.displayName)}</strong>
                            ${type.displayName !== type.name ? `<span class="gst-muted">(${esc(type.name)})</span>` : ''}
                            ${type.base ? `<span class="gst-badge gst-badge--default">${esc(type.base)}</span>` : ''}`;
                        row.addEventListener('click', () => {
                            body.querySelectorAll('.gst-picker-item--selected').forEach(e => e.classList.remove('gst-picker-item--selected'));
                            row.classList.add('gst-picker-item--selected');
                            selectedType = type;
                            selectBtn.disabled = false;
                        });
                        listContainer.appendChild(row);
                    });
                });
            }
        });
    };

    function esc(s) {
        if (!s) return '';
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }
})();
