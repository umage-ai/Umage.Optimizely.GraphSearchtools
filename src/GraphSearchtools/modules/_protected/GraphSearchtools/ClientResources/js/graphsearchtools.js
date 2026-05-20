/**
 * Graph Search Tools - Shared UI utilities
 */

// Derive module base URL for widgets running outside GST pages (no layout).
// require.toUrl('graphsearchtools/') resolves to e.g. /_protected/GraphSearchtools/ClientResources/js/
// Strip 'ClientResources/js/' to get the module root.
if (!window.GST_BASE_URL && typeof require !== 'undefined' && typeof require.toUrl === 'function') {
    try {
        window.GST_BASE_URL = require.toUrl('graphsearchtools/').replace(/ClientResources\/js\/?$/, '');
    } catch(e) {}
}

const GST = {
    /**
     * Fetch JSON from an API endpoint with error handling.
     */
    async fetchJson(url) {
        const resp = await fetch(url);
        if (!resp.ok) throw new Error(`HTTP ${resp.status}: ${resp.statusText}`);
        return resp.json();
    },

    async postJson(url, body) {
        const resp = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
            body: body ? JSON.stringify(body) : undefined
        });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}: ${resp.statusText}`);
        return resp.json();
    },

    /**
     * Show a loading indicator inside an element.
     */
    showLoading(el) {
        var msg = (window.GST_STRINGS && window.GST_STRINGS.graphsearchtools && window.GST_STRINGS.graphsearchtools.loading)
            ? window.GST_STRINGS.graphsearchtools.loading : 'Loading...';
        el.innerHTML = '<div class="gst-loading"><div class="gst-spinner"></div><p>' + msg + '</p></div>';
    },

    /**
     * Show an empty state inside an element.
     */
    showEmpty(el, message) {
        el.innerHTML = `<div class="gst-empty">
            ${GST.icons.info}
            <p>${message}</p>
        </div>`;
    },

    /**
     * Open a modal dialog. Returns the dialog body element.
     * Call the returned close() function to dismiss.
     */
    openDialog(title, opts = {}) {
        const container = document.getElementById('gst-dialog-container');
        const wide = opts.wide ? ' gst-dialog--wide' : '';
        const narrow = opts.narrow ? ' gst-dialog--narrow' : '';
        const flush = opts.flush ? ' gst-dialog__body--flush' : '';

        const backdrop = document.createElement('div');
        backdrop.className = 'gst-dialog-backdrop';
        backdrop.innerHTML = `
            <div class="gst-dialog${wide}${narrow}">
                <div class="gst-dialog__header">
                    <span class="gst-dialog__title">${title}</span>
                    <button class="gst-dialog__close" title="Close">
                        ${GST.icons.x}
                    </button>
                </div>
                <div class="gst-dialog__body${flush}"></div>
            </div>`;

        container.appendChild(backdrop);

        const body = backdrop.querySelector('.gst-dialog__body');
        const close = () => backdrop.remove();

        backdrop.querySelector('.gst-dialog__close').addEventListener('click', close);
        backdrop.addEventListener('click', (e) => {
            if (e.target === backdrop) close();
        });

        // ESC key
        const onKey = (e) => { if (e.key === 'Escape') { close(); document.removeEventListener('keydown', onKey); } };
        document.addEventListener('keydown', onKey);

        return { body, close };
    },

    /**
     * Create a sortable, searchable table.
     * columns: [{ key, label, sortable, align, render }]
     * data: array of row objects
     */
    createTable(columns, data, opts = {}) {
        const table = document.createElement('table');
        table.className = 'gst-table';

        // State
        let sortKey = opts.defaultSort || null;
        let sortDir = opts.defaultSortDir || 'asc';
        let filtered = [...data];

        const thead = document.createElement('thead');
        const headerRow = document.createElement('tr');
        columns.forEach(col => {
            const th = document.createElement('th');
            th.textContent = col.label;
            if (col.align === 'right') th.classList.add('num');
            if (col.sortable !== false) {
                th.addEventListener('click', () => {
                    if (sortKey === col.key) {
                        sortDir = sortDir === 'asc' ? 'desc' : 'asc';
                    } else {
                        sortKey = col.key;
                        sortDir = 'asc';
                    }
                    render();
                });
            }
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        table.appendChild(tbody);

        let lastFilterFn = null;

        function render(filterFn) {
            if (filterFn !== undefined) lastFilterFn = filterFn;
            // Sort indicators
            headerRow.querySelectorAll('th').forEach((th, i) => {
                th.removeAttribute('data-sort-dir');
                if (columns[i].key === sortKey) th.setAttribute('data-sort-dir', sortDir);
            });

            let rows = lastFilterFn ? data.filter(lastFilterFn) : [...data];

            if (sortKey) {
                rows.sort((a, b) => {
                    let va = a[sortKey], vb = b[sortKey];
                    if (va == null) va = '';
                    if (vb == null) vb = '';
                    if (typeof va === 'number' && typeof vb === 'number') return sortDir === 'asc' ? va - vb : vb - va;
                    va = String(va).toLowerCase();
                    vb = String(vb).toLowerCase();
                    return sortDir === 'asc' ? va.localeCompare(vb) : vb.localeCompare(va);
                });
            }

            tbody.innerHTML = '';
            if (rows.length === 0) {
                const tr = document.createElement('tr');
                var noResultsMsg = (window.GST_STRINGS && window.GST_STRINGS.graphsearchtools && window.GST_STRINGS.graphsearchtools.noresults)
                    ? window.GST_STRINGS.graphsearchtools.noresults : 'No results found';
                tr.innerHTML = '<td colspan="' + columns.length + '" class="gst-empty"><p>' + noResultsMsg + '</p></td>';
                tbody.appendChild(tr);
                return;
            }

            rows.forEach(row => {
                const tr = document.createElement('tr');
                if (opts.rowClass) {
                    const cls = opts.rowClass(row);
                    if (cls) tr.className = cls;
                }
                columns.forEach(col => {
                    const td = document.createElement('td');
                    if (col.align === 'right') td.classList.add('num');
                    if (col.render) {
                        const content = col.render(row);
                        if (typeof content === 'string') td.innerHTML = content;
                        else if (content instanceof Node) td.appendChild(content);
                    } else {
                        td.textContent = row[col.key] ?? '';
                    }
                    tr.appendChild(td);
                });
                if (opts.onRowClick) {
                    tr.style.cursor = 'pointer';
                    tr.addEventListener('click', () => opts.onRowClick(row));
                }
                tbody.appendChild(tr);
            });
        }

        render();
        return { table, render, getData: () => data };
    },

    /**
     * Download data as CSV file.
     */
    downloadCsv(filename, columns, data) {
        const escape = (v) => {
            if (v == null) return '';
            const s = String(v);
            return s.includes(',') || s.includes('"') || s.includes('\n')
                ? '"' + s.replace(/"/g, '""') + '"'
                : s;
        };

        const header = columns.map(c => escape(c.label)).join(',');
        const rows = data.map(row => columns.map(c => escape(row[c.key])).join(','));
        const csv = [header, ...rows].join('\r\n');

        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
    },

    /**
     * Load user preferences for a tool. Returns parsed JSON or empty object.
     */
    async loadPreferences(toolName) {
        try {
            return await this.fetchJson(`${window.GST_BASE_URL}PreferencesApi/Get?id=${encodeURIComponent(toolName)}`);
        } catch { return {}; }
    },

    /**
     * Save user preferences for a tool. Debounced - call freely on every change.
     */
    savePreferences(toolName, prefs) {
        if (this._prefTimers && this._prefTimers[toolName]) {
            clearTimeout(this._prefTimers[toolName]);
        }
        if (!this._prefTimers) this._prefTimers = {};
        this._prefTimers[toolName] = setTimeout(() => {
            fetch(`${window.GST_BASE_URL}PreferencesApi/Save?id=${encodeURIComponent(toolName)}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
                body: JSON.stringify(prefs)
            }).catch(() => {});
        }, 1000);
    },

    /**
     * SVG icon registry — Lucide line icons at stroke 1.5, 24×24 viewBox.
     * This is the single source for JS-rendered icons; the matching
     * Razor partial lives at Views/Shared/_Icon.cshtml. If you add an
     * entry here, add the same name + path data there too. See
     * design-system.md → Iconography for the visual rules.
     *
     * Consumers scale by setting width/height on the rendered <svg>
     * (the stroke scales with it, which is the Lucide convention).
     */
    /**
     * Convenience: fetch a registry icon with width/height + optional class
     * injected into the <svg>. Equivalent to the Razor `_Icon.cshtml`
     * partial — use this when the registry icon needs a size attribute
     * (e.g. inside an inline-flex span where the parent doesn't size it
     * via CSS).
     *
     *   GST.icon('search', { size: 16 })
     *   GST.icon('pin', { size: 14, class: 'gst-foo-icon' })
     */
    icon: function (name, opts) {
        opts = opts || {};
        var svg = (window.GST && GST.icons) ? GST.icons[name] : null;
        if (!svg) return '';
        var attrs = '';
        if (typeof opts.size === 'number') attrs += 'width="' + opts.size + '" height="' + opts.size + '" ';
        if (opts.class) attrs += 'class="' + opts.class + '" ';
        if (attrs) svg = svg.replace('<svg ', '<svg ' + attrs);
        return svg;
    },

    icons: (function () {
        function svg(body) {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true">' + body + '</svg>';
        }
        return {
            search:       svg('<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>'),
            chevronRight: svg('<polyline points="9 18 15 12 9 6"/>'),
            chevronDown:  svg('<polyline points="6 9 12 15 18 9"/>'),
            chevronLeft:  svg('<polyline points="15 18 9 12 15 6"/>'),
            pin:          svg('<line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14l-1.5-2.5V8.5h.5a2 2 0 0 0 0-4h-13a2 2 0 0 0 0 4h.5V14.5L5 17z"/>'),
            pinOff:       svg('<line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14l-1.5-2.5V8.5h.5a2 2 0 0 0 0-4h-13a2 2 0 0 0 0 4h.5V14.5L5 17z"/><line x1="4" y1="4" x2="20" y2="20"/>'),
            synonym:      svg('<polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/>'),
            synonymOff:   svg('<polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/><line x1="2" y1="2" x2="22" y2="22"/>'),
            channels:     svg('<rect x="3" y="4" width="18" height="6" rx="1"/><rect x="3" y="14" width="11" height="6" rx="1"/><path d="M17 17h4"/>'),
            insights:     svg('<line x1="18" y1="20" x2="18" y2="10"/><line x1="12" y1="20" x2="12" y2="4"/><line x1="6" y1="20" x2="6" y2="14"/>'),
            details:      svg('<rect x="3" y="3" width="18" height="18" rx="2"/><line x1="7" y1="9" x2="17" y2="9"/><line x1="7" y1="13" x2="17" y2="13"/><line x1="7" y1="17" x2="13" y2="17"/>'),
            trash:        svg('<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>'),
            plus:         svg('<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>'),
            x:            svg('<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>'),
            refresh:      svg('<polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>'),
            info:         svg('<circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>'),
            copy:         svg('<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>')
        };
    })(),

    /**
     * Safe accessor for window.GST_STRINGS.
     * path: dot-separated key e.g. 'contentaudit.col_name'
     * fallback: English string returned if key is missing
     */
    s: function(path, fallback) {
        try {
            var parts = path.split('.');
            var obj = window.GST_STRINGS;
            for (var i = 0; i < parts.length; i++) {
                if (obj === undefined || obj === null) return fallback || path;
                obj = obj[parts[i]];
            }
            return (obj && typeof obj === 'string') ? obj : (fallback || path);
        } catch (e) {
            return fallback || path;
        }
    },

    /**
     * Escape HTML special characters for safe insertion into innerHTML.
     */
    escHtml: function(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    },

    /**
     * Copy text to the clipboard. Prefers the async Clipboard API, falls back
     * to a hidden textarea + execCommand('copy') for older browsers and the
     * EPiServer admin shell which sometimes doesn't expose `navigator.clipboard`
     * over plain HTTP. Returns a Promise.
     */
    copyText: function(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function(resolve, reject) {
            try {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                ta.setAttribute('readonly', '');
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                document.body.removeChild(ta);
                resolve();
            } catch (e) { reject(e); }
        });
    },

    /**
     * Build a small "copy" button styled as `gst-copybtn`. Accepts either a
     * static string of `text` or a `getValue()` thunk so the button always
     * captures the current contents of a live-updated panel.
     *
     * Reads localized labels from `window.GST_STRINGS.shared.copy` /
     * `shared.copied` with English fallbacks, so callers don't have to thread
     * loc strings through.
     */
    copyButton: function(opts) {
        opts = opts || {};
        var label = opts.label || GST.s('shared.copy', 'Copy');
        var copied = opts.copiedLabel || GST.s('shared.copied', 'Copied');
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'gst-copybtn' + (opts.className ? ' ' + opts.className : '');
        btn.title = label;
        btn.setAttribute('aria-label', label);
        btn.innerHTML = GST.icons.copy + '<span class="gst-copybtn__label">' + GST.escHtml(label) + '</span>';
        btn.addEventListener('click', function(e) {
            e.preventDefault();
            var text = typeof opts.getValue === 'function'
                ? opts.getValue()
                : (opts.text || '');
            if (!text) return;
            GST.copyText(text).then(function() {
                btn.classList.add('is-copied');
                btn.querySelector('.gst-copybtn__label').textContent = copied;
                setTimeout(function() {
                    btn.classList.remove('is-copied');
                    btn.querySelector('.gst-copybtn__label').textContent = label;
                }, 1200);
            }).catch(function() { /* clipboard denied — keep label as-is */ });
        });
        return btn;
    },

    /**
     * Open the help drawer for the given tool key.
     * Reads the page h1 for the title and GST_STRINGS.help.{toolKey} for the body.
     */
    openHelp: function(toolKey) {
        GST.closeHelp();
        var h1 = document.querySelector('.gst-page-header h1');
        var title = h1 ? h1.textContent.replace('?', '').trim() : '';
        var body = GST.s('help.' + toolKey, '');

        var drawer = document.createElement('div');
        drawer.className = 'gst-help-drawer';
        drawer.id = 'gst-help-drawer';
        drawer.innerHTML =
            '<div class="gst-help-drawer__overlay"></div>' +
            '<div class="gst-help-drawer__panel">' +
                '<div class="gst-help-drawer__header">' +
                    '<span class="gst-help-drawer__title">' + GST.escHtml(title) + '</span>' +
                    '<button class="gst-help-drawer__close" aria-label="Close">' +
                        GST.icons.x +
                    '</button>' +
                '</div>' +
                '<div class="gst-help-drawer__body">' + GST.escHtml(body) + '</div>' +
            '</div>';

        document.body.appendChild(drawer);
        requestAnimationFrame(function() { drawer.classList.add('gst-help-drawer--open'); });

        drawer.querySelector('.gst-help-drawer__close').addEventListener('click', GST.closeHelp);
        drawer.querySelector('.gst-help-drawer__overlay').addEventListener('click', GST.closeHelp);

        var onKey = function(e) {
            if (e.key === 'Escape') {
                GST.closeHelp();
                document.removeEventListener('keydown', onKey);
            }
        };
        document.addEventListener('keydown', onKey);
    },

    /**
     * Close and remove the help drawer if open.
     */
    closeHelp: function() {
        var existing = document.getElementById('gst-help-drawer');
        if (existing) existing.remove();
    },

    /**
     * Format a date as a human-readable "X ago" string.
     * @param {Date|string} date
     * @returns {string}
     */
    timeAgo: function(date) {
        var d = (date instanceof Date) ? date : new Date(date);
        var diffMs = Date.now() - d.getTime();
        var diffMins = Math.floor(diffMs / 60000);
        if (diffMins < 1)  return GST.s('shared.justnow', 'just now');
        if (diffMins < 60) return diffMins + ' ' + GST.s('shared.minutesago', 'minutes ago');
        var diffHours = Math.floor(diffMins / 60);
        if (diffHours < 24) return diffHours + ' ' + GST.s('shared.hoursago', 'hours ago');
        var diffDays = Math.floor(diffHours / 24);
        return diffDays + ' ' + GST.s('shared.daysago', 'days ago');
    },

    /**
     * Tokenise GraphQL source and return HTML with span-wrapped tokens whose
     * classes mirror GraphiQL's CodeMirror token mode (cm-keyword, cm-property,
     * cm-attribute, etc.). The tokeniser is regex-based and intentionally
     * forgiving — it produces the right colour for the common shapes we
     * surface in code panels (registered query docs, directive snippets) and
     * degrades to plain text for anything it can't classify, so partial
     * matches never garble the output.
     *
     * Used by `applyGqlHighlight()` to colour any `<code data-lang="graphql">`
     * block on the page; safe to call directly with raw source strings if a
     * caller needs the highlighted HTML for some other surface.
     */
    gqlHighlight: function(code) {
        var TOKEN_RE = new RegExp([
            '(#[^\\n]*)',                                      // 1: # comment
            '(\\/\\*[\\s\\S]*?\\*\\/)',                        // 2: /* block */ (used in our snippets)
            '("""[\\s\\S]*?"""|"(?:\\\\.|[^"\\\\])*")',         // 3: string / block string
            '(\\$[A-Za-z_][\\w]*)',                            // 4: $variable
            '(-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)',          // 5: number
            '\\b(query|mutation|subscription|fragment|on|directive|schema|scalar|type|input|interface|union|enum|implements|extend|repeatable)\\b', // 6: keyword
            '\\b(true|false|null)\\b',                         // 7: atom (bool/null)
            '\\b([A-Z][A-Z0-9_]+)\\b',                         // 8: ENUM_LIKE atom
            '([A-Za-z_][\\w]*)(?=\\s*:)',                      // 9: argument/property name (lookahead colon)
            '([A-Za-z_][\\w]*)',                               // 10: ident → field name
            '(@[A-Za-z_][\\w]*)',                              // 11: directive ref
            '([{}()\\[\\]:,!=])'                               // 12: punctuation
        ].join('|'), 'g');
        var out = '';
        var lastIndex = 0;
        var src = String(code == null ? '' : code);
        var m;
        while ((m = TOKEN_RE.exec(src)) !== null) {
            if (m.index > lastIndex) out += GST.escHtml(src.slice(lastIndex, m.index));
            var cls;
            if (m[1] !== undefined || m[2] !== undefined) cls = 'cm';
            else if (m[3] !== undefined) cls = 'str';
            else if (m[4] !== undefined) cls = 'var';
            else if (m[5] !== undefined) cls = 'num';
            else if (m[6] !== undefined) cls = 'kw';
            else if (m[7] !== undefined || m[8] !== undefined) cls = 'atom';
            else if (m[9] !== undefined) cls = 'arg';
            else if (m[10] !== undefined) cls = 'field';
            else if (m[11] !== undefined) cls = 'dir';
            else if (m[12] !== undefined) cls = 'punct';
            out += '<span class="gst-gql-' + cls + '">' + GST.escHtml(m[0]) + '</span>';
            lastIndex = m.index + m[0].length;
        }
        if (lastIndex < src.length) out += GST.escHtml(src.slice(lastIndex));
        return out;
    },

    /**
     * Walk the document for any `<code data-lang="graphql">` (or `<pre data-lang>`)
     * and replace its contents with highlighted HTML. Idempotent — already-
     * highlighted blocks are skipped via a `data-gst-highlighted` flag so
     * re-running on dynamic mounts can't double-encode.
     */
    applyGqlHighlight: function(root) {
        var scope = root || document;
        var blocks = scope.querySelectorAll('[data-lang="graphql"]');
        for (var i = 0; i < blocks.length; i++) {
            var b = blocks[i];
            if (b.dataset.gstHighlighted === '1') continue;
            // Operate on textContent so we don't depend on whether the source
            // was already HTML-escaped by Razor or contains literal entities.
            var src = b.textContent;
            b.innerHTML = GST.gqlHighlight(src);
            b.dataset.gstHighlighted = '1';
        }
    },
};

/**
 * Permission helpers. window.GST_PERMS is seeded by the layout from the
 * server-side PermissionMap; if it's absent (e.g. an isolated test page),
 * default to "everything allowed" so the UI doesn't silently gate itself.
 */
GST.perms = window.GST_PERMS || {
    channels: true, insights: true,
    pinned: true, pinnedEdit: true, collections: true,
    synonyms: true, synonymsEdit: true
};
GST.can = function(scope) { return GST.perms[scope] !== false; };

/**
 * Renders a "read-only" amber banner above the host element. Idempotent:
 * if a banner with the same key already exists in the host, it's reused.
 */
GST.renderReadOnlyBanner = function(host, text) {
    if (!host || !text) return null;
    var existing = host.querySelector(':scope > .gst-readonly-banner[data-gst-readonly]');
    if (existing) return existing;
    var div = document.createElement('div');
    div.className = 'gst-readonly-banner';
    div.setAttribute('data-gst-readonly', '1');
    div.setAttribute('role', 'status');
    div.innerHTML =
        '<svg class="gst-readonly-banner__icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">' +
        '<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>' +
        '<span class="gst-readonly-banner__text">' + text + '</span>';
    host.insertBefore(div, host.firstChild);
    return div;
};

/**
 * Disable every element matching `selector` inside `root`. Sets the
 * disabled attribute (works on <button>) and a data-gst-disabled marker so
 * subsequent re-renders can skip elements we already touched.
 */
GST.disableAll = function(root, selector, tooltip) {
    if (!root) return;
    var nodes = root.querySelectorAll(selector);
    for (var i = 0; i < nodes.length; i++) {
        var n = nodes[i];
        if (n.dataset && n.dataset.gstDisabled === '1') continue;
        try { n.disabled = true; } catch (e) {}
        n.setAttribute('aria-disabled', 'true');
        n.classList.add('gst-disabled');
        if (tooltip) n.setAttribute('title', tooltip);
        if (n.dataset) n.dataset.gstDisabled = '1';
    }
};

/**
 * Shared editor-grid helpers. The Pinned editor and the channel-detail
 * Synonyms panel both render the same `gst-pinedit__*` table-grid shape
 * (filter + count chip + sortable headers + paged body + dirty drawer),
 * so the boilerplate around paging, sort header wiring, and the unsaved
 * drawer is factored out here. Call sites still own the data shape and
 * the per-row builder — these helpers are pure layout glue.
 */
GST.editGrid = {
    /**
     * Compute pageCount given a total count and page size; floors at 1 so
     * an empty list still has "Page 1 of 1" for status clarity.
     */
    pageCount: function (total, pageSize) {
        return Math.max(1, Math.ceil((total || 0) / pageSize));
    },

    /**
     * Render the prev/status/next strip into the supplied DOM nodes. Hides
     * the entire pager when total ≤ pageSize — the chrome is noise when
     * everything fits on one page.
     */
    renderPager: function (opts) {
        var pagerEl = opts.pagerEl, statusEl = opts.statusEl;
        var prevBtn = opts.prevBtn, nextBtn = opts.nextBtn;
        if (!pagerEl) return;
        if (opts.total <= opts.pageSize) { pagerEl.hidden = true; return; }
        var pc = GST.editGrid.pageCount(opts.total, opts.pageSize);
        pagerEl.hidden = false;
        if (statusEl) {
            var tpl = GST.s('shared.pagerStatus', 'Page %1 of %2');
            statusEl.textContent = tpl.replace('%1', opts.page).replace('%2', pc);
        }
        if (prevBtn) prevBtn.disabled = opts.page <= 1;
        if (nextBtn) nextBtn.disabled = opts.page >= pc;
    },

    /**
     * Wire prev/next buttons. `getTotal` is a callback so the helper can
     * recompute on click without forcing the caller to keep a stale total.
     */
    wirePager: function (opts) {
        var prevBtn = opts.prevBtn, nextBtn = opts.nextBtn;
        if (prevBtn) prevBtn.addEventListener('click', function () {
            if (opts.getPage() > 1) { opts.setPage(opts.getPage() - 1); opts.onChange(); }
        });
        if (nextBtn) nextBtn.addEventListener('click', function () {
            var pc = GST.editGrid.pageCount(opts.getTotal(), opts.pageSize);
            if (opts.getPage() < pc) { opts.setPage(opts.getPage() + 1); opts.onChange(); }
        });
    },

    /**
     * Wire a NodeList of `.gst-pinedit__sortbtn` buttons. Each button's
     * parent `<th>` carries `data-sort="<field>"`; clicking toggles
     * direction or switches the active field. The helper updates the
     * `is-asc` / `is-desc` classes on the column headers and calls
     * `onChange()` to let the caller re-render.
     */
    wireSortHeaders: function (sortBtns, sortState, onChange) {
        sortBtns.forEach(function (btn) {
            var th = btn.parentElement;
            var field = th.getAttribute('data-sort');
            btn.addEventListener('click', function () {
                if (sortState.field === field) {
                    sortState.dir = sortState.dir === 'asc' ? 'desc' : 'asc';
                } else {
                    sortState.field = field;
                    sortState.dir = 'asc';
                }
                onChange();
            });
        });
    },

    /**
     * Push the active sort indicator to the column headers. Call inside
     * any render function that may have changed `state.sort`.
     */
    refreshSortCarets: function (sortBtns, sortState) {
        sortBtns.forEach(function (btn) {
            var th = btn.parentElement;
            var field = th.getAttribute('data-sort');
            th.classList.remove('is-asc', 'is-desc');
            if (field === sortState.field) {
                th.classList.add(sortState.dir === 'desc' ? 'is-desc' : 'is-asc');
            }
        });
    }
};

// Expose GST on window so other tool scripts (pinned.js, channels.js, etc.)
// can reach the shared helpers via `window.GST.*`. Top-level `const` doesn't
// attach to window in a classic script context — without this assignment
// helpers like GST.s and GST.copyButton are only reachable via the lexical
// `GST` identifier, which trips up runtime feature-detect guards.
window.GST = GST;

// Event delegation: open help drawer for any [data-gst-help] button.
document.addEventListener('click', function(e) {
    var btn = e.target.closest('[data-gst-help]');
    if (btn) GST.openHelp(btn.getAttribute('data-gst-help'));
});

// Auto-highlight any GraphQL code panel on first paint. Tools that mount
// code blocks dynamically can call GST.applyGqlHighlight(root) themselves.
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function() { GST.applyGqlHighlight(); });
} else {
    GST.applyGqlHighlight();
}

// Dojo's BorderContainer calculates a narrower applicationContainer on initial load
// due to a brief layout artifact. Watch for the wrong width and correct it.
(function() {
    var _gstLayoutFixed = false;
    function doFix() {
        if (_gstLayoutFixed) return;
        if (typeof dijit === 'undefined' || !dijit.registry) return;
        var rc = dijit.registry.byId('rootContainer');
        if (!rc || !rc.resize) return;
        _gstLayoutFixed = true;
        // Dojo caches the wrong size; clear it to force a full recompute.
        var parent = rc.domNode && rc.domNode.parentElement;
        var w = parent ? parent.clientWidth : null;
        var h = rc.domNode ? rc.domNode.clientHeight : null;
        rc._borderBox = null;
        rc._contentBox = null;
        if (w && h) rc.resize({ w: w, h: h });
        setTimeout(function() {
            rc._borderBox = null;
            rc._contentBox = null;
            if (w && h) rc.resize({ w: w, h: h });
        }, 0);
    }
    // Watch applicationContainer for when Dojo sets a narrow width, then correct it
    var observer = new MutationObserver(function(mutations) {
        var appContainer = document.getElementById('applicationContainer');
        if (!appContainer) return;
        var parent = appContainer.parentElement;
        if (!parent) return;
        var available = parent.clientWidth;
        var current = appContainer.offsetWidth;
        // If there's more than 20px unaccounted for, layout is wrong
        if (available > 0 && (available - current) > 20) {
            observer.disconnect();
            setTimeout(doFix, 0);
            setTimeout(doFix, 50);
        }
    });
    // Start observing once applicationContainer exists
    function startObserving() {
        var appContainer = document.getElementById('applicationContainer');
        if (appContainer) {
            observer.observe(appContainer, { attributes: true, attributeFilter: ['style'] });
        } else {
            setTimeout(startObserving, 50);
        }
    }
    startObserving();
    // Fallback: also try after a delay in case the mutation is missed
    setTimeout(function() {
        observer.disconnect();
        doFix();
    }, 2000);
})();

