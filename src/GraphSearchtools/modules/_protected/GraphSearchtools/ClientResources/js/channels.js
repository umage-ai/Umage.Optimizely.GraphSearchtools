/**
 * Graph Search Tools — Search Channels UI (Phase 2.5)
 *
 * Two entry points:
 *   GST.channels.index({ detailUrlBase })   — wires up the index table.
 *   GST.channels.detail({ channelKey })     — wires up the detail page tabs.
 *
 * Read-only against the JSON API at /EPiServer/cms/graphsearchtools/api/channels.
 */
(function() {
    'use strict';

    var API_BASE = '/EPiServer/cms/graphsearchtools/api/channels';

    function s(path, fallback) {
        return GST.s(path, fallback);
    }

    function escHtml(v) {
        return GST.escHtml(v);
    }

    /** "2 hrs ago", "—", etc. */
    function relativeTime(iso) {
        if (!iso) return '—';
        var d = new Date(iso);
        if (isNaN(d.getTime())) return '—';
        var diff = (Date.now() - d.getTime()) / 1000;
        if (diff < 60)        return s('channels.time.justNow', 'just now');
        if (diff < 3600)      return Math.floor(diff / 60) + ' ' + s('channels.time.minutesAgo', 'min ago');
        if (diff < 86400)     return Math.floor(diff / 3600) + ' ' + s('channels.time.hoursAgo', 'hrs ago');
        return Math.floor(diff / 86400) + ' ' + s('channels.time.daysAgo', 'days ago');
    }

    /** Render Sites & locales cell. */
    function scopeCell(p) {
        var sitesHtml = (p.sites && p.sites.length)
            ? p.sites.map(function(x) { return '<span class="gst-badge gst-badge--default">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('channels.detail.meta.allSites', 'all sites')) + '</span>';
        var localesHtml = (p.locales && p.locales.length)
            ? p.locales.map(function(x) { return '<span class="gst-badge gst-badge--primary">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('channels.detail.meta.allLocales', 'all locales')) + '</span>';
        return '<div class="gst-prof-scope">' + sitesHtml + '</div>'
             + '<div class="gst-prof-scope" style="margin-top: 4px">' + localesHtml + '</div>';
    }

    /**
     * Skeleton cell for the per-row search-activity sparkline. Real data
     * lands in `populateActivityCells` once the InsightsApi.SearchKpis
     * fan-out resolves; until then the host is empty and the total reads
     * as an em-dash, matching the rest of the row's "not yet loaded" tone.
     */
    function activityCell(channelKey) {
        return '<div class="gst-prof-activity" data-key="' + escHtml(channelKey || '') + '">'
            +     '<div class="gst-prof-activity__spark"></div>'
            +     '<div class="gst-prof-activity__total">—</div>'
            +  '</div>';
    }

    function channelCell(p) {
        var subPath = p.graphQLDocPath
            ? p.key + ' · ' + p.graphQLDocPath
            : p.key;
        return '<div class="gst-prof-name">'
            +     '<div class="gst-prof-name__title">' + escHtml(p.displayName || p.key) + '</div>'
            +     '<div class="gst-prof-name__key">' + escHtml(subPath) + '</div>'
            +  '</div>';
    }

    function lastEditedCell(p) {
        if (!p.lastEditedAt) {
            return '<span class="gst-prof-status__line">—</span>';
        }
        var byPart = p.lastEditedBy ? ' · ' + escHtml(p.lastEditedBy) : '';
        return '<span class="gst-prof-status__line">' + escHtml(relativeTime(p.lastEditedAt)) + byPart + '</span>';
    }

    function chevronCell() {
        // The .gst-prof-arrow class drives the row-hover transform
        // (translateX 2px) plus the muted-to-primary color shift.
        return GST.icon('chevronRight', { class: 'gst-prof-arrow' });
    }

    /** ---------------- INDEX ---------------- */
    function index(opts) {
        opts = opts || {};
        // Detail URL is the index URL with a `?key=...` query so the CMS
        // shell maps both surfaces to the same registered menu item.
        var detailUrlBase = opts.detailUrlBase || '/EPiServer/cms/graphsearchtools/channels?key=';

        var tableHost  = document.getElementById('gst-prof-table-host');
        var emptyEl    = document.getElementById('gst-prof-empty');
        var searchEl   = document.getElementById('gst-prof-search');
        var siteEl     = document.getElementById('gst-prof-site-filter');
        var localeEl   = document.getElementById('gst-prof-locale-filter');
        var countEl    = document.getElementById('gst-prof-count');

        if (!tableHost) return;

        GST.showLoading(tableHost);

        GST.fetchJson(API_BASE).then(function(channels) {
            if (!channels || channels.length === 0) {
                tableHost.innerHTML = '';
                if (emptyEl) emptyEl.hidden = false;
                if (countEl) countEl.textContent = '';
                return;
            }
            populateFilters(channels);
            renderTable(channels);
            loadActivitySparklines(channels);
        }).catch(function(err) {
            tableHost.innerHTML = '';
            GST.alert(s('channels.requestFailed', 'Failed to load channels.'), 'danger');
            console.error('Channels list failed', err);
        });

        function populateFilters(channels) {
            var sites = new Set();
            var locales = new Set();
            channels.forEach(function(p) {
                (p.sites || []).forEach(function(x) { sites.add(x); });
                (p.locales || []).forEach(function(x) { locales.add(x); });
            });
            fillSelect(siteEl, sites);
            fillSelect(localeEl, locales);
        }

        function fillSelect(el, values) {
            if (!el) return;
            // Preserve the first "All …" option, drop and rebuild the rest.
            var first = el.querySelector('option');
            el.innerHTML = '';
            if (first) el.appendChild(first);
            Array.from(values).sort().forEach(function(v) {
                var o = document.createElement('option');
                o.value = v;
                o.textContent = v;
                el.appendChild(o);
            });
        }

        function renderTable(channels) {
            var table = document.createElement('table');
            table.className = 'gst-table gst-nav-table';
            // Column widths live in <colgroup> rather than per-th inline
            // styles so the schema is declarative and Razor consumers
            // could later swap to a server-rendered <thead> without
            // chasing widths through the JS render path.
            table.innerHTML =
                '<colgroup>'
                + '<col style="width: 26%"/>'
                + '<col style="width: 22%"/>'
                + '<col style="width: 32%"/>'
                + '<col/>'
                + '<col style="width: 32px"/>'
                + '</colgroup>'
                + '<thead><tr>'
                + '<th>' + escHtml(s('channels.cols.channel', 'Channel')) + '</th>'
                + '<th class="col-scope">' + escHtml(s('channels.cols.scope', 'Sites & locales')) + '</th>'
                + '<th class="col-activity">' + escHtml(s('channels.cols.activity', 'Activity (30d)')) + '</th>'
                + '<th>' + escHtml(s('channels.cols.lastEdited', 'Last edited')) + '</th>'
                + '<th></th>'
                + '</tr></thead><tbody></tbody>';
            tableHost.innerHTML = '';
            tableHost.appendChild(table);

            var tbody = table.querySelector('tbody');
            channels.forEach(function(p) {
                var tr = document.createElement('tr');
                tr.className = 'is-selectable';
                tr.dataset.key = p.key || '';
                tr.dataset.search = ((p.displayName || '') + ' ' + (p.key || '') + ' ' + (p.descriptionResolved || '')).toLowerCase();
                tr.dataset.sites = (p.sites || []).join('|');
                tr.dataset.locales = (p.locales || []).join('|');

                tr.innerHTML =
                    '<td>' + channelCell(p) + '</td>'
                    + '<td class="col-scope">' + scopeCell(p) + '</td>'
                    + '<td class="col-activity">' + activityCell(p.key) + '</td>'
                    + '<td>' + lastEditedCell(p) + '</td>'
                    + '<td>' + chevronCell() + '</td>';

                tr.addEventListener('click', function() {
                    if (!p.key) return;
                    window.location.href = detailUrlBase + encodeURIComponent(p.key);
                });
                tbody.appendChild(tr);
            });

            applyFilters();
        }

        /**
         * Fan-out: one InsightsApi.SearchKpis call per channel, with the
         * sparkline drawn into the row as each response lands. N+1 by design
         * — the alternative is a bespoke batch endpoint we don't yet need at
         * prototype scale. allSettled keeps a slow/failing channel from
         * stalling the rest.
         */
        function loadActivitySparklines(channels) {
            if (!tableHost || !window.GST || typeof GST.sparkline !== 'function') return;
            var BASE = window.GST_BASE_URL || '';

            channels.forEach(function(p) {
                if (!p.key) return;
                var host = tableHost.querySelector('.gst-prof-activity[data-key="' + cssEscape(p.key) + '"]');
                if (!host) return;
                var url = BASE + '/InsightsApi/SearchKpis?channelKey=' + encodeURIComponent(p.key);
                GST.fetchJson(url).then(function(k) {
                    renderActivityCell(host, k);
                }).catch(function() {
                    // Telemetry off / endpoint disabled — keep the row tidy
                    // by collapsing to a single em-dash rather than a noisy
                    // error state.
                    var total = host.querySelector('.gst-prof-activity__total');
                    if (total) total.textContent = '—';
                });
            });
        }

        function renderActivityCell(host, kpis) {
            if (!host || !kpis) return;
            var spark = host.querySelector('.gst-prof-activity__spark');
            var total = host.querySelector('.gst-prof-activity__total');
            var series = (kpis.sparkSearches && kpis.sparkSearches.length) ? kpis.sparkSearches : [];
            if (spark) {
                GST.sparkline(spark, series, {
                    label: s('channels.cols.activity', 'Activity (30d)'),
                    formatTooltip: function(v, i) {
                        var daysAgo = (series.length - 1) - i;
                        return v + ' · ' + daysAgo + 'd ago';
                    }
                });
            }
            if (total) {
                var n = kpis.totalSearches || 0;
                total.textContent = (typeof GST.formatCompactInt === 'function')
                    ? GST.formatCompactInt(n)
                    : String(n);
            }
        }

        function cssEscape(v) {
            if (window.CSS && typeof CSS.escape === 'function') return CSS.escape(v);
            return String(v).replace(/["\\]/g, '\\$&');
        }

        function applyFilters() {
            if (!tableHost) return;
            var q = (searchEl && searchEl.value || '').toLowerCase().trim();
            var site = siteEl && siteEl.value || '';
            var locale = localeEl && localeEl.value || '';
            var rows = tableHost.querySelectorAll('tbody tr');
            var visible = 0;
            rows.forEach(function(tr) {
                var matchQ = !q || tr.dataset.search.indexOf(q) >= 0;
                var matchS = !site || tr.dataset.sites.split('|').indexOf(site) >= 0 || tr.dataset.sites === '';
                var matchL = !locale || tr.dataset.locales.split('|').indexOf(locale) >= 0 || tr.dataset.locales === '';
                var show = matchQ && matchS && matchL;
                tr.hidden = !show;
                if (show) visible++;
            });
            if (countEl) {
                countEl.textContent = visible + ' ' + s('channels.cols.channel', 'channels').toLowerCase();
            }
        }

        if (searchEl) searchEl.addEventListener('input', applyFilters);
        if (siteEl)   siteEl.addEventListener('change', applyFilters);
        if (localeEl) localeEl.addEventListener('change', applyFilters);
    }

    /** ---------------- DETAIL ---------------- */
    /*
     * The detail page is a 50/50 workspace: the live preview lives on the
     * left and persists across right-side panel switches; the right side has
     * a three-way segmented switcher (Pinned / Synonyms / Details). The
     * pinned editor mounts up-front so the SERP preview's pin overlay reflects
     * the same data the user is editing without having to flip panels.
     */
    function detail(opts) {
        opts = opts || {};
        var key = opts.channelKey || '';
        var synonymsMounted = false;
        var insightsMounted = false;

        // Editor handles surfaced from each panel — the Insights tab uses
        // these to seed draft pin / synonym rows from a phrase signal. The
        // post-Aurora wiring narrows these to a minimal contract: clicking
        // an Insights row's pin / synonym icon opens the matching Aurora
        // flyout prefilled. The richer inline-edit panel is a phase-2 task.
        //
        // Shims are populated immediately (not behind the lazy `mountPinned` /
        // `mountSynonyms`) so the Insights icons work even when the marketer
        // never switches tabs — clicking them will trigger the mount.
        var editors = {
            pinned: makePinnedEditorShim(),
            synonyms: makeSynonymsEditorShim()
        };

        // Set when mountInsights() actually runs mountInsightsPanel and
        // captures its returned control surface. The KPI card's
        // onDateSelect callback drives setDateFilter / clearDateFilter
        // through this handle.
        var insightsCtl = null;

        // Mount the Insights tab eagerly — it's the default-visible panel,
        // so its lanes need to populate on first paint without a user click.
        // The Pinned / Synonyms Aurora grids are lazy-mounted on first tab
        // activation: the Aurora module owns its own DOM and binding it
        // up-front would slow down the initial Insights paint.
        mountInsights();
        loadKpis(key, {
            // Sparkline click → activate Insights tab (if not already
            // visible) and scope the lanes to that day. A null `date`
            // arrives when the user re-clicks the same day or the chip's
            // × button — clear the filter so the lanes revert to the
            // pill-driven sliding window.
            onDateSelect: function (date /*, index */) {
                if (!insightsCtl) return;
                if (date) {
                    activateTab('insights');
                    insightsCtl.setDateFilter(date);
                } else {
                    insightsCtl.clearDateFilter();
                }
            }
        });

        wireLivePreview();

        // Inject copy buttons into any code blocks marked [data-gst-copy].
        // The Razor markup wraps the GraphQL doc <pre> in such a block; this
        // keeps the wireup co-located with the panel that owns the code so
        // we don't have to reach back into Razor for the button DOM.
        document.querySelectorAll('[data-gst-copy]').forEach(function(block) {
            if (block.querySelector('.gst-copybtn')) return;
            if (!window.GST || typeof window.GST.copyButton !== 'function') return;
            block.appendChild(window.GST.copyButton({
                getValue: function() {
                    var pre = block.querySelector('pre, code, textarea');
                    return pre ? pre.textContent : '';
                },
                className: 'gst-copybtn--overlay'
            }));
        });

        // Panel switching. The buttons use the shared .gst-tabs__btn class
        // (see design-system.md → Component: Tabs); we scope the lookup to
        // the surrounding .gst-prof-switcher so we don't pick up unrelated
        // tab strips elsewhere on the page (e.g. a Pinned/Synonyms tab
        // bar inside a tools-console panel).
        var switcherBtns = document.querySelectorAll('.gst-prof-switcher .gst-tabs__btn');
        switcherBtns.forEach(function(btn) {
            btn.addEventListener('click', function() {
                var target = btn.dataset.panel;
                switcherBtns.forEach(function(x) {
                    var on = x === btn;
                    x.classList.toggle('is-active', on);
                    x.setAttribute('aria-selected', on ? 'true' : 'false');
                });
                document.querySelectorAll('.gst-prof-panel').forEach(function(p) {
                    p.hidden = p.dataset.panel !== target;
                    p.classList.toggle('is-active', p.dataset.panel === target);
                });
                if (target === 'pinned') mountPinned();
                if (target === 'synonyms') mountSynonyms();
                if (target === 'insights') mountInsights();
            });
        });

        function activateTab(name) {
            var btn = document.getElementById('gst-prof-tab-' + name);
            if (btn) btn.click();
        }

        // Aurora Pinned grid — channel-scoped via the JS module's `init({scope})`
        // entry point. Resolves the channel's collection id up-front via
        // /api/channels/{key}/pinned so the Aurora grid only walks the
        // matching collection. Returns a Promise that resolves after init
        // has run so callers (the Insights "open pin flyout" shim) can wait
        // before reaching for the create button's wired-up click handler.
        var pinnedMountPromise = null;
        function mountPinned() {
            if (pinnedMountPromise) return pinnedMountPromise;
            if (!window.GST || !window.GST.pinned || !window.GST.pinned.aurora
                || typeof window.GST.pinned.aurora.init !== 'function') return Promise.resolve();
            if (!document.getElementById('gst-pin-aurora-rows')) return Promise.resolve(); // unwired panel

            // Resolve a {locale → collectionId|null} map by probing each
            // declared locale's scoped endpoint in parallel. Locales whose
            // backing collection doesn't exist yet stay null in the map; the
            // Aurora grid lazily EnsureCollection's them on first save.
            var localesToProbe = (opts.locales && opts.locales.length) ? opts.locales : [''];
            var firstSite = (opts.sites && opts.sites[0]) || '';
            pinnedMountPromise = Promise.all(localesToProbe.map(function (l) {
                var url = API_BASE + '/' + encodeURIComponent(key) + '/pinned'
                    + '?site=' + encodeURIComponent(firstSite)
                    + '&locale=' + encodeURIComponent(l);
                return GST.fetchJson(url).then(function (resp) {
                    return { locale: l, collectionId: (resp && resp.collectionId) || null };
                }).catch(function () { return { locale: l, collectionId: null }; });
            })).then(function (results) {
                var collectionsByLocale = {};
                results.forEach(function (r) { collectionsByLocale[r.locale] = r.collectionId; });
                window.GST.pinned.aurora.init({
                    scope: {
                        channelKey: key,
                        collectionsByLocale: collectionsByLocale,
                        locales: opts.locales || []
                    }
                });
            });
            return pinnedMountPromise;
        }

        // Aurora Synonyms grid — channel-scoped via `init({scope.locales})`.
        // Synonyms are tenant-global in Optimizely Graph so there's no
        // collection narrowing; the scope only filters which locale pools
        // are surfaced in the Scope filter dropdown.
        function mountSynonyms() {
            if (synonymsMounted) return;
            if (!window.GST || !window.GST.synonyms || !window.GST.synonyms.aurora
                || typeof window.GST.synonyms.aurora.init !== 'function') return;
            if (!document.getElementById('gst-syn-aurora-rows')) return; // unwired panel, nothing to mount
            synonymsMounted = true;
            window.GST.synonyms.aurora.init({
                scope: {
                    locales: opts.locales || []
                }
            });
        }

        // Minimal "editor" shim that the Insights inline pin/synonym editors
        // use to read state + create/update entries. Post-Aurora the inline
        // editor only needs: whenReady (no async loading happens now —
        // Aurora's `init` is synchronous), canCreatePins, and a path to
        // open the matching Aurora flyout prefilled. The richer
        // findPinForPhrase / updatePin / appendRule contract from the old
        // pinned.js / synonyms-grid.js is phase-2 — for now the icon click
        // opens the create flyout with the phrase prefilled and the marketer
        // confirms / picks content there. See PR description for the
        // phase-2 candidates this leaves behind.
        function makePinnedEditorShim() {
            return {
                whenReady: function () { return Promise.resolve(); },
                canCreatePins: function () { return !!opts.queryAppliesPinned; },
                // No client-side index of pins exists post-Aurora — return null
                // so the Insights inline editor always offers "create" rather
                // than "update".
                findPinForPhrase: function () { return null; },
                // Open the Aurora pin flyout in create mode with the phrase
                // prefilled. The marketer picks content + locale + saves
                // inside the flyout — the same surface the Pinned tab uses.
                createPin: function (phrase) {
                    return openPinFlyoutForPhrase(phrase);
                },
                updatePin: function () { return openPinFlyoutForPhrase(); },
                // Content typeahead lives inside the flyout now — surface
                // an empty result here so the Insights inline editor's
                // typeahead degrades to "use the flyout to pick content".
                lookupContent: function () { return Promise.resolve([]); }
            };
        }
        function makeSynonymsEditorShim() {
            return {
                whenReady: function () { return Promise.resolve(); },
                findRuleForPhrase: function () { return null; },
                appendRule: function (lhs, rhs) {
                    return openSynFlyoutForRule(lhs + ' => ' + rhs);
                },
                updateRule: function (_obj, lhs, rhs) {
                    return openSynFlyoutForRule(lhs + ' => ' + rhs);
                }
            };
        }

        // Open the Aurora pin flyout in create mode and seed the phrase
        // field. Returns a resolved promise — the actual save happens inside
        // the flyout, not via this shim. The Insights row's "Saved" toast
        // accordingly reads as "Open flyout to confirm" rather than "Saved".
        // Awaits the mount promise so the create button's click handler is
        // wired up before we synthesise the click.
        function openPinFlyoutForPhrase(phrase) {
            // Activate the Pinned tab so the flyout overlays the correct
            // surface (the flyout's backdrop scopes to the page, but the tab
            // switch keeps the user oriented for follow-on edits).
            activateTab('pinned');
            return Promise.resolve(mountPinned()).then(function () {
                var createBtn = document.getElementById('gst-pin-create');
                if (createBtn) createBtn.click();
                var phraseEl = document.getElementById('gst-pinfly-phrase');
                if (phraseEl && phrase) phraseEl.value = phrase;
            });
        }
        function openSynFlyoutForRule(rule) {
            activateTab('synonyms');
            mountSynonyms();
            return Promise.resolve().then(function () {
                var createBtn = document.getElementById('gst-syn-create');
                if (createBtn) createBtn.click();
                var ruleEl = document.getElementById('gst-synfly-rule');
                if (ruleEl && rule) {
                    ruleEl.value = rule;
                    // Fire input event so the parse hint updates.
                    ruleEl.dispatchEvent(new Event('input', { bubbles: true }));
                }
            });
        }

        function mountInsights() {
            if (insightsMounted) return;
            insightsMounted = true;
            insightsCtl = mountInsightsPanel({
                channelKey: key,
                hasGraphQLDoc: !!opts.hasGraphQLDoc,
                queryAppliesPinned: typeof opts.queryAppliesPinned === 'boolean' ? opts.queryAppliesPinned : !!opts.hasGraphQLDoc,
                queryAppliesSynonyms: typeof opts.queryAppliesSynonyms === 'boolean' ? opts.queryAppliesSynonyms : true,
                getEditors: function () { return editors; },
                ensureSynonymsMounted: mountSynonyms,
                activateTab: activateTab
            }) || null;
        }

        // SERP-style live preview. Listens to the try-it input + locale chip
        // and renders /api/channels/{key}/preview into #gst-pin-tryit-results.
        // The Aurora migration deleted the legacy wiring from pinned.js but
        // left the markup + endpoint + CSS in place; this is the minimum
        // wireup that brings the preview back to life. The richer synonym-
        // chip strip and JSON inspect toggle from the legacy module are
        // phase-2 candidates.
        function wireLivePreview() {
            if (!opts.hasGraphQLDoc) return;
            var qInput = document.getElementById('gst-pin-tryit-q');
            var resultsEl = document.getElementById('gst-pin-tryit-results');
            var statsEl = document.getElementById('gst-pin-tryit-stats');
            if (!qInput || !resultsEl) return;

            var localeSel = document.getElementById('gst-pin-locale');
            var inputWrap = qInput.parentNode;
            var debounceTimer = null;
            var lastQuery = '';

            function setLoading(on) {
                resultsEl.classList.toggle('is-loading', !!on);
                if (inputWrap) inputWrap.classList.toggle('is-loading', !!on);
            }
            function setStats(text, isError) {
                if (!statsEl) return;
                statsEl.textContent = text || '';
                statsEl.classList.toggle('gst-serp__stats--err', !!isError);
            }
            function formatStats(shown, total, ms) {
                if (!total && !shown) return s('channels.detail.pinned.serpEmpty', 'No matches.') + ' · ' + ms + ' ms';
                if (!total || total === shown) return shown + ' hits · ' + ms + ' ms';
                return shown + ' of ' + total + ' hits · ' + ms + ' ms';
            }
            function formatScore(score) {
                if (!score) return '';
                if (score >= 100) return Math.round(score).toString();
                if (score >= 10)  return score.toFixed(1);
                return score.toFixed(2);
            }

            function escapeRegex(t) { return t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

            // Wrap case-insensitive matches of `phrase` tokens in <mark>.
            function highlightFragment(text, phrase) {
                var frag = document.createDocumentFragment();
                if (!text) return frag;
                var tokens = (phrase || '').split(/\s+/).filter(function (t) { return t.length >= 2; });
                if (!tokens.length) { frag.appendChild(document.createTextNode(text)); return frag; }
                var pattern = new RegExp('(' + tokens.map(escapeRegex).join('|') + ')', 'gi');
                var lastIdx = 0;
                text.replace(pattern, function (match, _g, offset) {
                    if (offset > lastIdx) frag.appendChild(document.createTextNode(text.slice(lastIdx, offset)));
                    var mark = document.createElement('mark');
                    mark.className = 'gst-serp__mark';
                    mark.textContent = match;
                    frag.appendChild(mark);
                    lastIdx = offset + match.length;
                    return match;
                });
                if (lastIdx < text.length) frag.appendChild(document.createTextNode(text.slice(lastIdx)));
                return frag;
            }
            // Snippet arrives with Graph's native highlight markers
            // (U+0001 / U+0002 around matched tokens). Split on them so
            // synonym-expanded matches highlight correctly, not just the
            // user's typed phrase.
            function markedFragment(text) {
                var frag = document.createDocumentFragment();
                if (!text) return frag;
                var i = 0;
                while (i < text.length) {
                    var open = text.indexOf('', i);
                    if (open < 0) { frag.appendChild(document.createTextNode(text.slice(i))); break; }
                    if (open > i) frag.appendChild(document.createTextNode(text.slice(i, open)));
                    var close = text.indexOf('', open + 1);
                    if (close < 0) { frag.appendChild(document.createTextNode(text.slice(open + 1))); break; }
                    var mark = document.createElement('mark');
                    mark.className = 'gst-serp__mark';
                    mark.textContent = text.slice(open + 1, close);
                    frag.appendChild(mark);
                    i = close + 1;
                }
                return frag;
            }

            function buildHitCard(hit, phrase, idx) {
                var isPinned = !!hit.pinned;
                var li = document.createElement('li');
                li.className = 'gst-serp__hit' + (isPinned ? ' is-pinned' : '');
                li.style.setProperty('--gst-serp-stagger', (idx * 28) + 'ms');

                if (isPinned) {
                    var ribbon = document.createElement('span');
                    ribbon.className = 'gst-serp__pin-ribbon';
                    ribbon.setAttribute('aria-hidden', 'true');
                    ribbon.innerHTML = GST.icons.pin;
                    li.appendChild(ribbon);
                }

                var head = document.createElement('div');
                head.className = 'gst-serp__hit-head';
                var title = document.createElement('a');
                title.className = 'gst-serp__title';
                title.href = hit.url || '#';
                if (!hit.url) title.classList.add('is-disabled');
                title.target = hit.url ? '_blank' : '_self';
                title.rel = 'noopener noreferrer';
                title.appendChild(highlightFragment(hit.name || s('channels.detail.pinned.serpUntitled', '(untitled)'), phrase));
                head.appendChild(title);
                if (hit.score) {
                    var score = document.createElement('span');
                    score.className = 'gst-serp__score';
                    score.title = s('channels.detail.pinned.serpScoreTooltip', 'Graph relevance score');
                    score.textContent = formatScore(hit.score);
                    head.appendChild(score);
                }
                li.appendChild(head);

                if (hit.url) {
                    var urlLine = document.createElement('div');
                    urlLine.className = 'gst-serp__url';
                    var glyph = document.createElement('span');
                    glyph.className = 'gst-serp__url-glyph';
                    glyph.textContent = '›';
                    urlLine.appendChild(glyph);
                    urlLine.appendChild(document.createTextNode(' ' + hit.url));
                    li.appendChild(urlLine);
                }

                if (hit.fullTextSnippet) {
                    var snippet = document.createElement('p');
                    snippet.className = 'gst-serp__snippet';
                    snippet.appendChild(markedFragment(hit.fullTextSnippet));
                    li.appendChild(snippet);
                }

                var meta = document.createElement('div');
                meta.className = 'gst-serp__meta';
                if (isPinned) {
                    var pinChip = document.createElement('span');
                    pinChip.className = 'gst-serp__chip gst-serp__chip--pinned';
                    pinChip.textContent = s('channels.detail.pinned.serpPinnedBadge', 'Pinned');
                    pinChip.title = s('channels.detail.pinned.serpPinnedTooltip',
                        'This result is locked to the top by a pin in this channel.');
                    meta.appendChild(pinChip);
                }
                if (hit.contentType) {
                    var chip = document.createElement('span');
                    chip.className = 'gst-serp__chip';
                    chip.textContent = hit.contentType;
                    meta.appendChild(chip);
                }
                if (hit.language) {
                    var lang = document.createElement('span');
                    lang.className = 'gst-serp__lang';
                    lang.textContent = hit.language;
                    meta.appendChild(lang);
                }
                li.appendChild(meta);
                return li;
            }

            function renderHits(hits, phrase) {
                resultsEl.innerHTML = '';
                if (!hits.length) {
                    var empty = document.createElement('li');
                    empty.className = 'gst-serp__empty';
                    empty.textContent = s('channels.detail.pinned.serpEmptyHelp',
                        'No content matched this phrase. Try a different term, or pin a target above.');
                    resultsEl.appendChild(empty);
                    return;
                }
                var frag = document.createDocumentFragment();
                hits.forEach(function (hit, idx) { frag.appendChild(buildHitCard(hit, phrase, idx)); });
                resultsEl.appendChild(frag);
            }

            function run() {
                var q = qInput.value.trim();
                if (q.length < 2) {
                    resultsEl.innerHTML = '';
                    setStats('');
                    setLoading(false);
                    lastQuery = '';
                    return;
                }
                lastQuery = q;
                setLoading(true);
                var loc = (localeSel && !localeSel.disabled) ? (localeSel.value || '') : '';
                var url = API_BASE + '/' + encodeURIComponent(key)
                    + '/preview?phrase=' + encodeURIComponent(q)
                    + '&locale=' + encodeURIComponent(loc);
                GST.fetchJson(url).then(function (result) {
                    if (qInput.value.trim() !== lastQuery) return; // stale
                    setLoading(false);
                    var hits  = (result && result.hits) || [];
                    var total = (result && result.totalCount) || 0;
                    var ms    = (result && result.durationMs) || 0;
                    setStats(formatStats(hits.length, total, ms));
                    renderHits(hits, q);
                }).catch(function (err) {
                    if (qInput.value.trim() !== lastQuery) return;
                    setLoading(false);
                    resultsEl.innerHTML = '';
                    setStats((err && err.message) || s('channels.detail.pinned.previewFailed', 'preview failed'), true);
                });
            }

            qInput.addEventListener('input', function () {
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(run, 320);
            });
            // Locale switch should re-run the preview without forcing the
            // marketer to retype. lastQuery reset so the staleness guard
            // accepts the re-issued call.
            if (localeSel) {
                localeSel.addEventListener('change', function () {
                    if (qInput.value.trim().length >= 2) {
                        lastQuery = '';
                        run();
                    }
                });
            }
            // Pin / synonym mutations elsewhere on the page dispatch
            // `gst:preview-refresh` so the SERP repaints against the new
            // state without forcing the marketer to retype. Delay gives
            // Graph's read replica a beat after the admin API write;
            // resetting lastQuery bypasses the staleness guard so an
            // identical-text re-run still goes through (the server-side
            // per-call nonce handles Graph's response-byte cache).
            document.addEventListener('gst:preview-refresh', function () {
                if (qInput.value.trim().length < 2) return;
                clearTimeout(debounceTimer);
                debounceTimer = setTimeout(function () {
                    lastQuery = '';
                    run();
                }, 300);
            });
        }
    }

    /** ---------------- INSIGHTS PANEL ---------------- */
    /*
     * Three lanes of phrase-level signal scoped to the active channel:
     * top phrases, zero-result phrases, low-CTR phrases. Backed by the
     * Search Logs API with a channelKey filter (the controller adds the
     * filter when the param is present, so global Search Logs UI is
     * unaffected).
     *
     * Interactions:
     *  - Click a phrase → drop it into the live preview's input (the
     *    existing input listener handles the debounce + fetch).
     *  - Click "Preview" → same as clicking the row.
     *  - Click "Draft pin" → switch to Pinned tab and seed a new draft
     *    row with the phrase. A small toast confirms the seed so the
     *    marketer doesn't have to flip tabs to confirm.
     *  - Click "Draft synonym" → switch to Synonyms tab and seed a new
     *    draft row with `phrase => ` typed in.
     */
    function mountInsightsPanel(opts) {
        opts = opts || {};
        var BASE = window.GST_BASE_URL || '';
        var SEARCHLOGS_API = BASE + '/SearchLogsApi';
        var channelKey = opts.channelKey || '';
        if (!channelKey) return;

        var root = document.getElementById('gst-prof-ins');
        var refreshBtn = document.getElementById('gst-prof-ins-refresh');
        var pillEls = root ? root.querySelectorAll('.gst-segmented__btn') : [];
        if (!root) return;

        // Time-window pills map to a since-millis offset. The ISO string is
        // recomputed at fetch time so the window is always anchored to "now"
        // rather than going stale across long-lived sessions.
        var WINDOWS = { '1h': 3600e3, '24h': 86400e3, '7d': 7 * 86400e3, '30d': 30 * 86400e3 };

        // Lane-local state. Each lane starts at INITIAL_TAKE rows and grows
        // by SHOW_MORE_STEP per "show more" click. Resets back to INITIAL_TAKE
        // whenever the window or locale changes — a fresh slice is a fresh
        // surface, no point preserving an expanded view across a context flip.
        var INITIAL_TAKE = 5;
        var SHOW_MORE_STEP = 10;
        var LANES = ['top', 'zero', 'lowctr'];
        var LANE_API = { top: 'Top', zero: 'ZeroResults', lowctr: 'LowCtr' };
        var LANE_DOM = {
            top:    { listId: 'gst-prof-ins-top',    countId: 'gst-prof-ins-top-count' },
            zero:   { listId: 'gst-prof-ins-zero',   countId: 'gst-prof-ins-zero-count' },
            lowctr: { listId: 'gst-prof-ins-lowctr', countId: 'gst-prof-ins-lowctr-count' }
        };

        var state = {
            window: '24h',
            inflight: null,
            takes: { top: INITIAL_TAKE, zero: INITIAL_TAKE, lowctr: INITIAL_TAKE },
            // When set (UTC midnight), overrides the window pill — the
            // three lanes fetch only that 24h slice. Cleared by the pill,
            // the refresh button, the chip × button, or a same-day re-click
            // on the KPI sparkline.
            dateFilter: null,
            // Aurora Phase 3B: auto-fire the preview with the most-searched
            // phrase on first paint so the marketer lands on "what people
            // actually search for, and what they get back" rather than an
            // empty preview pane. Only seeded once per page-load — afterwards
            // the user's typing / row-click drives the preview.
            previewSeeded: false
        };

        function resetTakes() {
            LANES.forEach(function (l) { state.takes[l] = INITIAL_TAKE; });
        }

        // The Pinned editor's locale chip (`#gst-pin-locale`) is the page's
        // single source of truth for which language branch the editor is
        // looking at. Mirroring it here means a marketer who narrows the
        // preview to "sv" sees only Swedish search activity in the lanes,
        // and switching back to "en" reflects English-only data without a
        // separate pill on the Insights surface.
        var localeSel = document.getElementById('gst-pin-locale');
        function activeLocale() {
            return (localeSel && !localeSel.disabled) ? (localeSel.value || '') : '';
        }

        function activeWindowMs() {
            return WINDOWS[state.window] || WINDOWS['24h'];
        }

        function setAlert(msg) {
            GST.alert(msg || null, msg ? 'danger' : null, { host: '#gst-prof-ins-alert' });
        }

        // Window pill click → state change → refetch. Reset per-lane takes
        // so a fresh window opens compact rather than carrying over a
        // previously-expanded row count. Picking a pill also clears any
        // active date filter — the gesture says "I want a range view
        // again".
        pillEls.forEach(function (pill) {
            pill.addEventListener('click', function () {
                if (pill.classList.contains('is-active') && !state.dateFilter) return;
                pillEls.forEach(function (p) { p.classList.remove('is-active'); });
                pill.classList.add('is-active');
                state.window = pill.dataset.window || '24h';
                clearDateFilter(/* silent: */ true);
                resetTakes();
                fetchAll();
            });
        });

        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () {
                resetTakes();
                fetchAll();
            });
        }

        // Locale switch on the Pinned editor → re-fetch insights for the new
        // branch. Pinned listens to the same event to reload its rows; both
        // mutations land on the page in lockstep so the preview, the pinned
        // table, and the analytics lanes always agree on which locale is
        // being inspected.
        if (localeSel) {
            localeSel.addEventListener('change', function () {
                resetTakes();
                fetchAll();
            });
        }

        function fetchLane(lane) {
            // When a sparkline day is selected, scope every lane to that
            // 24h UTC window. Otherwise use the pill-driven sliding window
            // ending now.
            var sinceMs, untilMs;
            if (state.dateFilter) {
                sinceMs = state.dateFilter.getTime();
                untilMs = sinceMs + 86400000;
            } else {
                untilMs = Date.now();
                sinceMs = untilMs - activeWindowMs();
            }
            var url = SEARCHLOGS_API + '/' + LANE_API[lane]
                + '?since=' + encodeURIComponent(new Date(sinceMs).toISOString())
                + '&until=' + encodeURIComponent(new Date(untilMs).toISOString())
                + '&take=' + state.takes[lane]
                + '&channelKey=' + encodeURIComponent(channelKey);
            var loc = activeLocale();
            if (loc) url += '&locale=' + encodeURIComponent(loc);
            return GST.fetchJson(url);
        }

        // Show-more bumps just one lane's take and re-renders that lane.
        // The reader caches the underlying aggregate per (window, channel,
        // locale) for 30s, so the bigger take re-runs only the in-memory
        // sort-and-take — no DB roundtrip on the hot path.
        function showMore(lane) {
            state.takes[lane] += SHOW_MORE_STEP;
            paintLoading(lane);
            var stamp = state.inflight = {};
            fetchLane(lane).then(function (rows) {
                if (state.inflight !== stamp) return;
                paintLane(lane, rows);
            }).catch(function (err) {
                if (state.inflight !== stamp) return;
                paintLane(lane, { _err: err });
            });
        }

        function fetchAll() {
            // Cancel-isolation: stamp this run so a slower in-flight request
            // can't paint over a fresher one (window pill switching is fast).
            var stamp = state.inflight = {};
            setAlert(null);
            if (refreshBtn) refreshBtn.classList.add('is-spinning');

            paintLoading('top');
            paintLoading('zero');
            paintLoading('lowctr');

            Promise.all([
                fetchLane('top').catch(function (e) { return { _err: e }; }),
                fetchLane('zero').catch(function (e) { return { _err: e }; }),
                fetchLane('lowctr').catch(function (e) { return { _err: e }; })
            ]).then(function (results) {
                if (state.inflight !== stamp) return;
                if (refreshBtn) refreshBtn.classList.remove('is-spinning');
                paintLane('top',    results[0]);
                paintLane('zero',   results[1]);
                paintLane('lowctr', results[2]);
                seedPreviewFromTop(results[0]);
            });
        }

        // Aurora Phase 3B — on first successful paint of the Top lane, mirror
        // its #1 phrase into the live preview's query field if the marketer
        // hasn't already typed something. Subsequent fetches don't re-seed
        // (the user-driven `applyToPreview` and row-click stay authoritative).
        function seedPreviewFromTop(topResult) {
            if (state.previewSeeded) return;
            if (!topResult || topResult._err) return;
            var rows = Array.isArray(topResult) ? topResult : [];
            if (rows.length === 0) return;
            var input = document.getElementById('gst-pin-tryit-q');
            // Don't overwrite a query the marketer already typed (or that
            // was deep-linked into the page via ?q=... / saved-state).
            if (!input || input.value && input.value.length > 0) {
                state.previewSeeded = true;
                return;
            }
            applyToPreview(rows[0].phrase);
            state.previewSeeded = true;
        }

        function paintLoading(lane) {
            var listEl = document.getElementById(LANE_DOM[lane].listId);
            if (!listEl) return;
            removeShowMore(lane);
            // Skeleton lives inside an <li> so the <ol> stays valid.
            listEl.innerHTML = '<li class="gst-prof-ins-lane__loading"><span></span></li>';
        }

        function removeShowMore(lane) {
            var btn = document.getElementById('gst-prof-ins-' + lane + '-more');
            if (btn) btn.remove();
        }

        function paintLane(lane, payload) {
            var dom = LANE_DOM[lane];
            var listEl = document.getElementById(dom.listId);
            var countEl = document.getElementById(dom.countId);
            if (!listEl) return;
            removeShowMore(lane);

            if (payload && payload._err) {
                listEl.innerHTML = '<li class="gst-prof-ins-lane__error">'
                    + escHtml(s('channels.detail.insights.loadFailed', 'Failed to load insights.'))
                    + '</li>';
                if (countEl) {
                    countEl.textContent = '—';
                    countEl.removeAttribute('data-window');
                }
                return;
            }

            var rows = Array.isArray(payload) ? payload : [];
            if (countEl) {
                countEl.textContent = String(rows.length);
                countEl.setAttribute('data-window', state.window);
            }

            if (rows.length === 0) {
                var emptyKey = lane === 'zero' ? 'channels.detail.insights.emptyZero'
                    : lane === 'lowctr' ? 'channels.detail.insights.emptyLowCtr'
                    : 'channels.detail.insights.empty';
                var defaultEmpty = lane === 'zero' ? 'No zero-result phrases — every search found something.'
                    : lane === 'lowctr' ? 'Not enough sessions to score CTR yet.'
                    : 'No traffic in this window yet.';
                listEl.innerHTML = '<li class="gst-prof-ins-lane__empty">'
                    + escHtml(s(emptyKey, defaultEmpty)) + '</li>';
                return;
            }

            // Bar widths are normalized against the lane's maximum hits — the
            // top row is always 100% wide; everything else scales linearly so
            // the second-glance read maps to row position.
            var maxHits = rows.reduce(function (m, r) { return r.hits > m ? r.hits : m; }, 0) || 1;

            listEl.innerHTML = '';
            var frag = document.createDocumentFragment();
            rows.forEach(function (row) {
                frag.appendChild(buildRow(lane, row, maxHits));
            });
            listEl.appendChild(frag);

            // "Show more" only when the lane returned exactly its requested
            // take — that's the signal there might be additional rows. When
            // the server returns fewer than asked for, we've reached the end
            // of the available data and the button stays hidden.
            if (rows.length >= state.takes[lane]) {
                appendShowMore(lane, listEl);
            }
        }

        function appendShowMore(lane, listEl) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.id = 'gst-prof-ins-' + lane + '-more';
            btn.className = 'gst-prof-ins-lane__more';
            btn.textContent = s('channels.detail.insights.showMore', 'Show more');
            btn.addEventListener('click', function () {
                showMore(lane);
            });
            // Drop the button after the <ol>; it sits in the lane's flow but
            // outside the list so screen readers don't announce it as an item.
            listEl.parentNode.appendChild(btn);
        }

        function buildRow(lane, row, maxHits) {
            var li = document.createElement('li');
            li.className = 'gst-prof-ins-row';
            li.dataset.phrase = row.phrase || '';
            li.tabIndex = 0;
            li.setAttribute('role', 'button');

            // 1: phrase
            var phraseEl = document.createElement('span');
            phraseEl.className = 'gst-prof-ins-row__phrase';
            phraseEl.textContent = row.phrase || '';
            phraseEl.title = row.phrase || '';
            li.appendChild(phraseEl);

            // 2: bar / miss-dots / CTR chip
            var barEl = document.createElement('span');
            barEl.className = 'gst-prof-ins-row__bar';
            if (lane === 'zero') {
                // Four little severity ticks — eye-readable, no axis math.
                for (var i = 0; i < 4; i++) {
                    var dot = document.createElement('span');
                    dot.className = 'gst-prof-ins-row__miss';
                    barEl.appendChild(dot);
                }
            } else if (lane === 'lowctr') {
                var ctrEl = document.createElement('span');
                ctrEl.className = 'gst-prof-ins-row__ctr';
                var pct = Math.round((row.ctr || 0) * 100);
                ctrEl.innerHTML = '<span class="gst-prof-ins-row__ctr-num">' + pct + '%</span>'
                    + ' ' + escHtml(s('channels.detail.insights.ctrLabel', 'CTR'));
                barEl.appendChild(ctrEl);
            } else {
                var w = Math.max(4, Math.round(((row.hits || 0) / maxHits) * 100));
                barEl.style.setProperty('--w', w + '%');
            }
            li.appendChild(barEl);

            // 3: count
            var countEl = document.createElement('span');
            countEl.className = 'gst-prof-ins-row__count';
            countEl.innerHTML = '<strong>' + escHtml(String(row.hits || 0)) + '</strong>'
                + '<small>' + escHtml(s('channels.detail.insights.hitsLabel', 'hits')) + '</small>';
            li.appendChild(countEl);

            // 4: actions — three icon buttons (preview / pin / synonym) on
            //    every row regardless of lane. Pin and synonym are disabled
            //    when the active channel's GraphQL doc doesn't apply them
            //    (so the buttons are still visible for affordance, but a
            //    tooltip explains why they can't be used here).
            var actEl = document.createElement('span');
            actEl.className = 'gst-prof-ins-row__actions';

            actEl.appendChild(makeIconButton(
                'preview',
                GST.icons.search,
                s('channels.detail.insights.actionPreview', 'Preview this phrase'),
                false,
                function (ev) { ev.stopPropagation(); applyToPreview(row.phrase); }
            ));

            actEl.appendChild(makeIconButton(
                'pin',
                GST.icons.pin,
                opts.queryAppliesPinned
                    ? s('channels.detail.insights.actionPin', 'Pin a result for this phrase')
                    : s('channels.detail.insights.actionPinDisabled', 'This channel doesn\'t apply pinned results.'),
                !opts.queryAppliesPinned,
                function (ev) { ev.stopPropagation(); toggleInlineEditor(li, row, 'pin'); }
            ));

            actEl.appendChild(makeIconButton(
                'synonym',
                GST.icons.synonym,
                opts.queryAppliesSynonyms
                    ? s('channels.detail.insights.actionSynonym', 'Add a synonym for this phrase')
                    : s('channels.detail.insights.actionSynonymDisabled', 'This channel doesn\'t apply synonyms.'),
                !opts.queryAppliesSynonyms,
                function (ev) { ev.stopPropagation(); toggleInlineEditor(li, row, 'synonym'); }
            ));

            li.appendChild(actEl);

            // Whole row is the click target → loads into preview.
            li.addEventListener('click', function () {
                applyToPreview(row.phrase);
            });
            li.addEventListener('keydown', function (ev) {
                if (ev.key === 'Enter' || ev.key === ' ') {
                    ev.preventDefault();
                    applyToPreview(row.phrase);
                }
            });

            return li;
        }

        function makeIconButton(kind, svg, title, disabled, onClick) {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'gst-prof-ins-row__icon gst-prof-ins-row__icon--' + kind;
            btn.title = title;
            btn.setAttribute('aria-label', title);
            btn.disabled = !!disabled;
            btn.innerHTML = svg;
            btn.addEventListener('click', onClick);
            return btn;
        }

        // Aurora migration: the inline pin/synonym editor used to render
        // below an Insights row with its own typeahead / save flow. Post-
        // Aurora the canonical edit surface is the right-edge flyout, so
        // clicking the row's pin / synonym icon now opens that flyout in
        // create mode with the phrase prefilled. The richer inline editor
        // (with existing-pin detection + in-place updates) is a phase-2
        // candidate: it needs an Aurora "compact create" variant that the
        // design system doesn't yet have an entry for.
        function toggleInlineEditor(rowEl, row, kind) {
            // Mirror the row's phrase into the live preview so the marketer
            // sees the current SERP while the flyout is open.
            applyToPreview(row.phrase);
            var ed = (opts.getEditors() || {})[kind === 'pin' ? 'pinned' : 'synonyms'];
            if (!ed) return;
            if (kind === 'pin') {
                if (typeof ed.createPin === 'function') ed.createPin(row.phrase);
            } else if (kind === 'synonym') {
                if (opts.ensureSynonymsMounted) opts.ensureSynonymsMounted();
                if (typeof ed.appendRule === 'function') ed.appendRule(row.phrase, '');
            }
        }

        function applyToPreview(phrase) {
            if (!phrase) return;
            // Mark this row as active so the user can see which phrase the
            // preview is showing, even after they scroll.
            root.querySelectorAll('.gst-prof-ins-row.is-active').forEach(function (r) {
                r.classList.remove('is-active');
            });
            var match = root.querySelector('.gst-prof-ins-row[data-phrase="' + cssEscape(phrase) + '"]');
            if (match) match.classList.add('is-active');

            var input = document.getElementById('gst-pin-tryit-q');
            if (!input) return;
            input.value = phrase;
            // Synthesize an input event so the live preview's debounce
            // handler picks it up — the path is the same one a typed-in
            // phrase takes. Note: post-Aurora migration, the live preview
            // wiring is deferred to phase 2 — the input still receives the
            // value but the SERP below stays inert until the preview module
            // is reintroduced.
            input.dispatchEvent(new Event('input', { bubbles: true }));
            // Don't steal focus; leaving focus inside the row keeps keyboard
            // navigation working.
        }

        // CSS.escape polyfill — old Edge / quiet selector edge cases.
        function cssEscape(v) {
            if (window.CSS && typeof window.CSS.escape === 'function') return window.CSS.escape(v);
            return String(v).replace(/[^a-zA-Z0-9_-]/g, function (c) {
                return '\\' + c.charCodeAt(0).toString(16) + ' ';
            });
        }

        // Filter chip — surfaces the active date filter next to the pill
        // group. Re-rendered whenever state.dateFilter changes; absent when
        // null. Inserted into the existing toolbar so it shares the bar's
        // vertical alignment with the pills and refresh button.
        function renderFilterChip() {
            var bar = root && root.querySelector('.gst-prof-ins__bar');
            if (!bar) return;
            var existing = bar.querySelector('.gst-filter-chip');
            if (!state.dateFilter) {
                if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
                return;
            }
            var label = formatDateLabel(state.dateFilter);
            if (existing) {
                existing.querySelector('.gst-filter-chip__text').textContent = label;
                return;
            }
            var chip = document.createElement('span');
            chip.className = 'gst-filter-chip';
            chip.innerHTML = '<span class="gst-filter-chip__text"></span>' +
                '<button type="button" class="gst-filter-chip__clear" aria-label="Clear filter">×</button>';
            chip.querySelector('.gst-filter-chip__text').textContent = label;
            chip.querySelector('.gst-filter-chip__clear').addEventListener('click', function () {
                clearDateFilter();
            });
            // Place after the pill group, before the spacer. Querying the
            // spacer rather than appending keeps the refresh button on the
            // right edge.
            var spacer = bar.querySelector('.gst-prof-ins__bar-spacer');
            if (spacer) bar.insertBefore(chip, spacer);
            else bar.appendChild(chip);
        }

        // "May 12" / locale-aware month-day. Year omitted because the spark
        // covers a 30d window; the year is unambiguous from context.
        function formatDateLabel(d) {
            try {
                return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' });
            } catch (e) {
                return d.toISOString().slice(0, 10);
            }
        }

        function setDateFilter(d) {
            // Same-day re-click toggles off (the sparkline already does
            // this for us, but defending here keeps the API symmetric).
            if (!d) { clearDateFilter(); return; }
            if (state.dateFilter && state.dateFilter.getTime() === d.getTime()) {
                clearDateFilter();
                return;
            }
            state.dateFilter = d;
            renderFilterChip();
            resetTakes();
            fetchAll();
        }

        function clearDateFilter(silent) {
            if (!state.dateFilter && !silent) return;
            state.dateFilter = null;
            renderFilterChip();
            // Also clear the KPI card's highlighted dot so the visuals
            // agree with the data.
            if (window.GST && typeof GST.clearKpiCardSelection === 'function') {
                GST.clearKpiCardSelection('#gst-prof-kpis');
            }
            if (!silent) {
                resetTakes();
                fetchAll();
            }
        }

        // Initial load.
        fetchAll();

        return {
            setDateFilter: setDateFilter,
            clearDateFilter: clearDateFilter
        };
    }

    /**
     * Load the 30-day search-activity KPIs for this channel and render them
     * into the #gst-prof-kpis host. Shares the renderer (and therefore the
     * visual treatment) with the global Insights tool — the only difference
     * is the ?channelKey scope on the API call.
     */
    function loadKpis(channelKey, opts) {
        if (!window.GST || typeof window.GST.renderKpiCard !== 'function') return;
        var host = document.getElementById('gst-prof-kpis');
        if (!host) return;
        opts = opts || {};
        var BASE = window.GST_BASE_URL || '';
        var url = BASE + '/InsightsApi/SearchKpis?channelKey=' + encodeURIComponent(channelKey);
        GST.renderKpiCardLoading(host);
        GST.fetchJson(url)
            .then(function (k) { GST.renderKpiCard(host, k, { onDateSelect: opts.onDateSelect }); })
            .catch(function () { GST.renderKpiCardError(host); });
    }

    window.GST = window.GST || {};
    window.GST.channels = { index: index, detail: detail };
})();
