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
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M9 12h6m-3-3v6m-7 4h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z"/></svg>
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
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>
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

    /** SVG icon helpers */
    icons: {
        search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>',
        edit: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>',
        link: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/></svg>',
        list: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
        download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>',
        tree: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 3h7v7H3zm11 0h7v7h-7zM3 14h7v7H3z"/><path d="M14 17.5h7M14 14v7"/></svg>',
        props: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M14 3v4a1 1 0 0 0 1 1h4"/></svg>',
        chevronRight: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>',
        chevronDown: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>',
        pin: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="17" x2="12" y2="22"/><path d="M5 17h14l-1.5-2.5V8.5h.5a2 2 0 0 0 0-4h-13a2 2 0 0 0 0 4h.5V14.5L5 17z"/></svg>',
        synonym: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/></svg>',
    },

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
        btn.innerHTML =
            '<svg class="gst-copybtn__icon" viewBox="0 0 14 14" width="12" height="12" aria-hidden="true">'
              + '<rect x="3.5" y="3.5" width="7" height="8" rx="1" fill="none" stroke="currentColor" stroke-width="1.2"/>'
              + '<path d="M5.5 1.5h5a1 1 0 0 1 1 1v6" fill="none" stroke="currentColor" stroke-width="1.2"/>'
            + '</svg>'
            + '<span class="gst-copybtn__label">' + GST.escHtml(label) + '</span>';
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
                        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>' +
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
     * Render a job status alert bar.
     * @param {{ isRunning: boolean, hasRun: boolean, lastRunUtc: string|null }} status
     * @param {string} startUrl  Full URL to POST to start the job
     * @returns {HTMLElement|null}  Alert element, or null if no alert needed
     */
    renderJobAlert: function(status, startUrl) {
        var STALE_HOURS = 24;
        var isStale = false;
        if (status.hasRun && status.lastRunUtc) {
            var diffH = (Date.now() - new Date(status.lastRunUtc).getTime()) / 3600000;
            isStale = diffH > STALE_HOURS;
        }

        if (!status.isRunning && status.hasRun && !isStale) return null;  // fresh — no alert

        var el = document.createElement('div');

        if (status.isRunning) {
            el.className = 'gst-alert gst-alert--info';
            el.innerHTML =
                '<span>' + GST.s('shared.jobalert_running', 'The analysis job is currently running.') + '</span>' +
                '<button class="gst-btn gst-btn--sm gst-job-alert-refresh">' +
                    GST.s('shared.refresh', 'Refresh') +
                '</button>';
            el.querySelector('.gst-job-alert-refresh').addEventListener('click', function() {
                window.location.reload();
            });
            return el;
        }

        // Not run yet, or stale
        el.className = 'gst-alert gst-alert--warning';
        var msg = status.hasRun && status.lastRunUtc
            ? GST.s('shared.jobalert_stale', 'Statistics were updated') + ' ' + GST.timeAgo(status.lastRunUtc) + '. ' + GST.s('shared.jobalert_consider', 'Consider running the aggregation job.')
            : GST.s('shared.jobalert_never', 'The aggregation job has not run yet. Run it to populate statistics.');
        el.innerHTML =
            '<span>' + GST.escHtml(msg) + '</span>' +
            '<button class="gst-btn gst-btn--sm gst-job-alert-run">' +
                GST.s('shared.runnow', 'Run now') +
            '</button>';

        el.querySelector('.gst-job-alert-run').addEventListener('click', function() {
            var btn = el.querySelector('.gst-job-alert-run');
            btn.disabled = true;
            fetch(startUrl, {
                method: 'POST',
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            }).then(function(resp) {
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                el.className = 'gst-alert gst-alert--info';
                el.innerHTML =
                    '<span>' + GST.s('shared.jobalert_started', 'Job started.') + '</span>' +
                    '<button class="gst-btn gst-btn--sm gst-job-alert-refresh">' +
                        GST.s('shared.refresh', 'Refresh') +
                    '</button>';
                el.querySelector('.gst-job-alert-refresh').addEventListener('click', function() {
                    window.location.reload();
                });
            }).catch(function() {
                btn.disabled = false;
            });
        });

        return el;
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

