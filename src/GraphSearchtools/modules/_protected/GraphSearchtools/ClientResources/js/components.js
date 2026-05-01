/**
 * Graph Search Tools - Reusable UI Components
 *
 * Content Picker:  GST.contentPicker(opts)  → Promise<{id, name}>
 * Content Type Picker: GST.contentTypePicker(opts) → Promise<{id, name, displayName}>
 */
(function () {
    const API = window.GST_BASE_URL + 'ComponentsApi';

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
