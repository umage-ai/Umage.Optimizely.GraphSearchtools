/**
 * Graph Search Tools - Reusable UI Components
 *
 * Alert:           GST.alert(msg, level, opts)
 * Content Picker:  GST.contentPicker(opts)  → Promise<{id, name}>
 * Content Type Picker: GST.contentTypePicker(opts) → Promise<{id, name, displayName}>
 * Flyout:          GST.flyout.open(key, opts) / GST.flyout.close(key)
 * Row menu:        GST.rowMenu(anchor, [{ label, onSelect, danger? }, ...])
 */
(function () {
    const API = window.GST_BASE_URL + '/ComponentsApi';

    // ── Alert ──────────────────────────────────────────────────────────
    // Single page-level alert region. Callers don't manage the host's
    // hidden/textContent/class state themselves — they just call
    // GST.alert('msg', 'danger') to show and GST.alert(null) to clear.
    //
    // The default host is `#gst-alert`, rendered once per tool page by the
    // layout (design-system.md → "The tool page"). Surfaces that need a
    // scoped alert region (e.g. an Insights window inside Channel detail)
    // pass `opts.host` to override.
    //
    // Levels map to the four `.gst-alert--{level}` modifiers defined in
    // graphsearchtools.css.
    const ALERT_LEVELS = { info: 1, success: 1, warning: 1, danger: 1 };
    const _alertTimers = new WeakMap();

    GST.alert = function (msg, level, opts) {
        opts = opts || {};
        const host = typeof opts.host === 'string'
            ? document.querySelector(opts.host)
            : (opts.host || document.getElementById('gst-alert'));
        if (!host) return null;

        const prev = _alertTimers.get(host);
        if (prev) { clearTimeout(prev); _alertTimers.delete(host); }

        if (!msg) {
            host.hidden = true;
            host.textContent = '';
            host.className = 'gst-alert';
            return host;
        }

        const lvl = ALERT_LEVELS[level] ? level : 'info';
        host.className = 'gst-alert gst-alert--' + lvl;
        host.textContent = msg;
        host.hidden = false;

        if (typeof opts.autoDismiss === 'number' && opts.autoDismiss > 0) {
            const t = setTimeout(function () {
                _alertTimers.delete(host);
                GST.alert(null, null, { host: host });
            }, opts.autoDismiss);
            _alertTimers.set(host, t);
        }
        return host;
    };

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

    // The CMS shell's platform-nav has a higher z-index than us, so the
    // flyout has to anchor below it. Measure the actual bottom edge on
    // every open — nav height varies between CMS 12 (#epi-navigation-root)
    // and CMS 13 (<platform-navigation>), and the static 48px fallback
    // overshoots most shells. Falls back if no nav element is found.
    function syncFlyoutTopOffset() {
        // The inner <header> is the actual fixed top-app-bar; the outer
        // #epi-navigation-root wraps open dropdown menus too, so its
        // bounding-rect overshoots when any menu is open.
        var sels = [
            '#epi-navigation-root > header',  // CMS 12 (Axiom shell)
            'header.epi-pn-navigation',
            'platform-navigation-wrapper',    // CMS 13
            'platform-navigation',
            '.epi-globalNavigation'           // older CMS 12 shells
        ];
        for (var i = 0; i < sels.length; i++) {
            var el = document.querySelector(sels[i]);
            if (!el) continue;
            var rect = el.getBoundingClientRect();
            if (rect.height > 0) {
                document.documentElement.style.setProperty('--gst-flyout-top', Math.round(rect.bottom) + 'px');
                return;
            }
        }
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

            syncFlyoutTopOffset();
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

    // ── Sparkline ──────────────────────────────────────────────────
    // Tiny inline line chart for KPI tiles. Renders an SVG <polyline>
    // sized by its container (the host's CSS sets width + height —
    // typically inside .gst-kpi__spark which is 100% × 36px). One
    // invisible <rect> hit-area per data point carries the per-day
    // <title> tooltip.
    //
    // Usage:
    //   GST.sparkline(host, [12, 45, 0, …], { label: 'Daily searches' });
    //
    // Options:
    //   max:           normalize point heights to this. Defaults to
    //                  Math.max(...series, 1) — the peak is the series
    //                  max. Pass an explicit max (e.g. 100 for
    //                  percentages) when you want a fixed scale.
    //   label:         aria-label for the <svg>. Recommended; without it
    //                  the chart is invisible to screen readers.
    //   formatTooltip: fn(value, index) → string. Drives the per-day
    //                  <title> tooltip on hover. When omitted, no tooltips.
    //   onClick:       fn(value, index) → void. When provided, hit-areas
    //                  become clickable (cursor + click handler).
    //   selectedIndex: int. Draws a vertical cursor bar at that data
    //                  point — full chart height, on top of the line.
    //                  -1 / undefined = none.
    GST.sparkline = function (host, series, opts) {
        const el = typeof host === 'string' ? document.querySelector(host) : host;
        if (!el) return;
        opts = opts || {};
        const data = Array.isArray(series) ? series : [];
        const n = data.length;
        if (n === 0) {
            el.innerHTML = '';
            return;
        }

        const max = typeof opts.max === 'number' && opts.max > 0
            ? opts.max
            : Math.max.apply(null, data.concat([0])) || 1;
        const allZero = data.every(function (v) { return !v; });

        // viewBox is (n-1) units wide × 100 tall — one unit per segment
        // between adjacent points. preserveAspectRatio=none lets the SVG
        // stretch to fill its container; vector-effect=non-scaling-stroke
        // (in CSS) keeps the line at a stable pixel width regardless.
        const svgNs = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(svgNs, 'svg');
        svg.setAttribute('class', 'gst-sparkline' + (allZero ? ' gst-sparkline--empty' : ''));
        svg.setAttribute('viewBox', '0 0 ' + Math.max(n - 1, 1) + ' 100');
        svg.setAttribute('preserveAspectRatio', 'none');
        svg.setAttribute('role', 'img');
        if (opts.label) svg.setAttribute('aria-label', opts.label);

        if (!allZero) {
            const points = [];
            for (let i = 0; i < n; i++) {
                const v = data[i] || 0;
                const h = max > 0 ? (v / max) * 100 : 0;
                points.push(i + ',' + (100 - h));
            }
            const line = document.createElementNS(svgNs, 'polyline');
            line.setAttribute('class', 'gst-sparkline__line');
            line.setAttribute('points', points.join(' '));
            svg.appendChild(line);

            // Invisible hit-area columns: one per data point, half a unit on
            // either side so the entire surface is covered. Each carries the
            // optional <title> tooltip and click handler.
            const wantHits = typeof opts.formatTooltip === 'function' || typeof opts.onClick === 'function';
            if (wantHits && n > 1) {
                const onClick = typeof opts.onClick === 'function' ? opts.onClick : null;
                for (let i = 0; i < n; i++) {
                    const hit = document.createElementNS(svgNs, 'rect');
                    hit.setAttribute('class', 'gst-sparkline__hit' + (onClick ? ' is-clickable' : ''));
                    hit.setAttribute('x', String(i - 0.5));
                    hit.setAttribute('y', '0');
                    hit.setAttribute('width', '1');
                    hit.setAttribute('height', '100');
                    if (typeof opts.formatTooltip === 'function') {
                        const title = document.createElementNS(svgNs, 'title');
                        title.textContent = opts.formatTooltip(data[i] || 0, i);
                        hit.appendChild(title);
                    }
                    if (onClick) {
                        (function (idx) {
                            hit.addEventListener('click', function () { onClick(data[idx] || 0, idx); });
                        })(i);
                    }
                    svg.appendChild(hit);
                }
            }

            // Selected-date cursor — a vertical bar spanning the chart
            // height at the chosen x. Drawn last so it sits above the line.
            // Stroke width is held constant by vector-effect; bar position
            // is always exactly on the data point's x slot.
            const sel = (typeof opts.selectedIndex === 'number') ? opts.selectedIndex : -1;
            if (sel >= 0 && sel < n) {
                const cursor = document.createElementNS(svgNs, 'line');
                cursor.setAttribute('class', 'gst-sparkline__cursor');
                cursor.setAttribute('x1', String(sel));
                cursor.setAttribute('y1', '0');
                cursor.setAttribute('x2', String(sel));
                cursor.setAttribute('y2', '100');
                svg.appendChild(cursor);
            }
        }

        el.innerHTML = '';
        el.appendChild(svg);
    };

    // ── Number formatters ──────────────────────────────────────────
    // Compact integer for KPI headlines. 999 → "999", 1,234 → "1K",
    // 12,345 → "12K", 1,234,567 → "1M". Rounds to nearest unit; no
    // decimal places. The boundary cases (e.g. 999,500) bump to the
    // next unit so we never render "1000K".
    GST.formatCompactInt = function (n) {
        if (n == null || isNaN(n)) return '0';
        const v = Math.round(Number(n));
        const absv = Math.abs(v);
        const sign = v < 0 ? '-' : '';
        if (absv >= 999500) return sign + Math.round(absv / 1e6) + 'M';
        if (absv >= 1000)   return sign + Math.round(absv / 1000) + 'K';
        return sign + absv;
    };

    // Percentage rounded to nearest integer. 12.7 → "13%", 0.4 → "0%".
    GST.formatCompactPct = function (p) {
        if (p == null || isNaN(p)) return '0%';
        return Math.round(Number(p)) + '%';
    };

    // ── KPI card renderer ──────────────────────────────────────────
    // Fills a .gst-kpis host with three tiles (Searches / CTR / Zero
    // results) sourced from an InsightsSearchKpis payload. Both the
    // global Insights tool and the per-channel detail surface call this
    // — same wire format, same visual treatment, only the channelKey on
    // the API call differs.
    //
    // Usage:
    //   GST.renderKpiCard('#gst-insights-kpis', kpisFromApi);
    //   GST.renderKpiCard(hostEl, kpisFromApi, { strings: customStrings });
    //
    // The helper reads localized labels from window.GST_STRINGS.insights
    // by default; pass `opts.strings` to override (e.g. a channel-scoped
    // namespace if one is added later).
    GST.renderKpiCard = function (host, kpis, opts) {
        const el = typeof host === 'string' ? document.querySelector(host) : host;
        if (!el) return;
        opts = opts || {};
        const k = kpis || {};
        const days = k.windowDays || 30;
        const strings = opts.strings || (window.GST_STRINGS && window.GST_STRINGS.insights) || {};

        // Resolve a per-index UTC date so callers can filter by day. Index
        // 0 is the oldest entry in the spark series; the last index lands
        // on the UTC day of `windowEndUtc`. Returns a Date at UTC midnight.
        function dateAtIndex(i) {
            if (!k.windowEndUtc) return null;
            const end = new Date(k.windowEndUtc);
            if (isNaN(end.getTime())) return null;
            const endDay = Date.UTC(end.getUTCFullYear(), end.getUTCMonth(), end.getUTCDate());
            return new Date(endDay - (days - 1 - i) * 86400000);
        }

        // Cross-sparkline selection state lives on the host. Repeat calls
        // to renderKpiCard preserve the selection across data refreshes,
        // and a click on one sparkline highlights the same day on all three.
        if (!el.__kpiState) el.__kpiState = { selectedIndex: -1 };
        const state = el.__kpiState;
        // If the new payload has a different window size, selection becomes
        // ambiguous — clear it rather than highlight a different date.
        if (state.lastDays != null && state.lastDays !== days) state.selectedIndex = -1;
        state.lastDays = days;

        // Headline figures use the compact forms (12K / 1M / 13%). The
        // tooltip uses the same compact forms — at this precision the
        // hover popup is a sanity check, not a forensic readout.
        const formatInt = GST.formatCompactInt;
        const formatPct = GST.formatCompactPct;
        function tmpl(t, value, fallback) {
            const s = t || fallback || '%1';
            if (Array.isArray(value)) {
                return s.replace(/%(\d+)/g, function (_, n) {
                    const idx = parseInt(n, 10) - 1;
                    return idx >= 0 && idx < value.length ? String(value[idx]) : '';
                });
            }
            return s.replace('%1', String(value));
        }
        function daysAgoLabel(d, i) {
            const ago = d - 1 - i;
            if (ago === 0) {
                return (window.GST_STRINGS && window.GST_STRINGS.shared && window.GST_STRINGS.shared.today) || 'today';
            }
            return tmpl(strings.kpi_days_ago, ago, '%1d ago');
        }
        function esc(s) {
            if (s == null) return '';
            const d = document.createElement('div');
            d.textContent = s;
            return d.innerHTML;
        }

        const tiles = [
            {
                key: 'searches',
                label: strings.kpi_searches || 'Searches',
                value: formatInt(k.totalSearches || 0),
                series: k.sparkSearches || [],
                fmt: function (v) { return formatInt(v) + ' searches'; }
            },
            {
                key: 'ctr',
                label: strings.kpi_ctr || 'Click-through rate',
                value: formatPct(k.ctrPct || 0),
                series: k.sparkCtr || [],
                fmt: function (v) { return formatPct(v); },
                // CTR scale is [0,100] regardless of the data — fixed max
                // means a quiet day doesn't look like a 100%-day visually.
                max: 100
            },
            {
                key: 'zero',
                label: strings.kpi_zero || 'Zero-result searches',
                value: formatInt(k.totalZero || 0),
                series: k.sparkZero || [],
                fmt: function (v) { return formatInt(v) + ' zero-result'; }
            }
        ];

        el.innerHTML = '';
        const sparkHosts = [];
        const tilesRendered = [];
        tiles.forEach(function (t) {
            const tile = document.createElement('div');
            tile.className = 'gst-kpi';
            tile.setAttribute('data-kpi', t.key);
            tile.innerHTML =
                '<div class="gst-kpi__label">' + esc(t.label) +
                ' <span class="gst-muted">(' + esc(tmpl(strings.kpi_window, days, 'last %1 days')) + ')</span></div>' +
                '<div class="gst-kpi__value">' + esc(t.value) + '</div>' +
                '<div class="gst-kpi__spark"></div>';
            el.appendChild(tile);
            sparkHosts.push(tile.querySelector('.gst-kpi__spark'));
            tilesRendered.push(t);
        });

        // Render all three sparklines, sharing the selectedIndex. Click on
        // any one toggles selection across the trio and fires onDateSelect.
        function paintAll() {
            tilesRendered.forEach(function (t, tileIdx) {
                GST.sparkline(sparkHosts[tileIdx], t.series, {
                    label: t.label,
                    max: t.max,
                    selectedIndex: state.selectedIndex,
                    formatTooltip: function (v, i) {
                        return tmpl(strings.kpi_tooltip, [daysAgoLabel(days, i), t.fmt(v)], '%1: %2');
                    },
                    onClick: typeof opts.onDateSelect === 'function'
                        ? function (_v, i) {
                            const next = state.selectedIndex === i ? -1 : i;
                            state.selectedIndex = next;
                            paintAll();
                            opts.onDateSelect(next < 0 ? null : dateAtIndex(next), next);
                        }
                        : null
                });
            });
        }
        paintAll();
    };

    // Programmatically clear the cross-sparkline selection on a KPI card.
    // Used by the surrounding page when the user picks a window pill or
    // clicks the chip's × — both fire onDateSelect(null) via the helper
    // so the caller doesn't need to know the internal state shape.
    GST.clearKpiCardSelection = function (host) {
        const el = typeof host === 'string' ? document.querySelector(host) : host;
        if (!el || !el.__kpiState || el.__kpiState.selectedIndex < 0) return false;
        el.__kpiState.selectedIndex = -1;
        // Re-paint each sparkline without selectedIndex. Cheapest path:
        // walk the rendered tiles and drop the dot. Re-paint is simpler.
        const spans = el.querySelectorAll('.gst-kpi__spark');
        spans.forEach(function (s) {
            const cursor = s.querySelector('.gst-sparkline__cursor');
            if (cursor && cursor.parentNode) cursor.parentNode.removeChild(cursor);
        });
        return true;
    };

    // Loading + error placeholders for the KPI card. Three blank tiles
    // so layout doesn't reflow when the real data arrives.
    GST.renderKpiCardLoading = function (host) {
        const el = typeof host === 'string' ? document.querySelector(host) : host;
        if (!el) return;
        const msg = (window.GST_STRINGS && window.GST_STRINGS.shared && window.GST_STRINGS.shared.loading) || 'Loading...';
        el.innerHTML = (
            '<div class="gst-kpi"><div class="gst-kpi__label">' + escText(msg) + '</div></div>'
        ).repeat(3);
    };

    GST.renderKpiCardError = function (host, message) {
        const el = typeof host === 'string' ? document.querySelector(host) : host;
        if (!el) return;
        const msg = message ||
            (window.GST_STRINGS && window.GST_STRINGS.insights && window.GST_STRINGS.insights.load_failed) ||
            'Could not load.';
        el.innerHTML = '<div class="gst-kpi"><div class="gst-kpi__label">' + escText(msg) + '</div></div>';
    };

    function escText(s) {
        if (s == null) return '';
        const d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    }

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
            const { body, close } = GST.openDialog(opts.title || GST.s('components.picker_title', 'Select Content'), { narrow: true });

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
