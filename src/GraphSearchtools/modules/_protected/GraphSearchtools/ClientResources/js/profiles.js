/**
 * Graph Search Tools — Search Profiles UI (Phase 2.5)
 *
 * Two entry points:
 *   GST.profiles.index({ detailUrlBase })   — wires up the index table.
 *   GST.profiles.detail({ profileKey })     — wires up the detail page tabs +
 *                                              fetches the audit log.
 *
 * Read-only against the JSON API at /EPiServer/cms/graphsearchtools/api/profiles.
 */
(function() {
    'use strict';

    var API_BASE = '/EPiServer/cms/graphsearchtools/api/profiles';

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
        if (diff < 60)        return s('profiles.time.justNow', 'just now');
        if (diff < 3600)      return Math.floor(diff / 60) + ' ' + s('profiles.time.minutesAgo', 'min ago');
        if (diff < 86400)     return Math.floor(diff / 3600) + ' ' + s('profiles.time.hoursAgo', 'hrs ago');
        return Math.floor(diff / 86400) + ' ' + s('profiles.time.daysAgo', 'days ago');
    }

    function statusBadge(status) {
        var key, klass;
        switch (status) {
            case 'Tuned':       key = 'profiles.status.tuned';       klass = 'gst-badge--success'; break;
            case 'NeedsReview': key = 'profiles.status.needsReview'; klass = 'gst-badge--warning'; break;
            case 'DocMissing':  key = 'profiles.status.docMissing';  klass = 'gst-badge--danger';  break;
            case 'FreeForm':    key = 'profiles.status.freeForm';    klass = 'gst-badge--default'; break;
            case 'Cold':        key = 'profiles.status.cold';        klass = 'gst-badge--default'; break;
            default:            key = 'profiles.status.cold';        klass = 'gst-badge--default';
        }
        // Server may also return integer enum values from JSON serializer config —
        // map those defensively.
        return '<span class="gst-badge ' + klass + '"><span class="gst-badge__dot"></span>'
            + escHtml(s(key, status)) + '</span>';
    }

    /** Render Sites & locales cell. */
    function scopeCell(p) {
        var sitesHtml = (p.sites && p.sites.length)
            ? p.sites.map(function(x) { return '<span class="gst-badge gst-badge--default">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('profiles.detail.meta.allSites', 'all sites')) + '</span>';
        var localesHtml = (p.locales && p.locales.length)
            ? p.locales.map(function(x) { return '<span class="gst-badge gst-badge--primary">' + escHtml(x) + '</span>'; }).join('')
            : '<span class="gst-badge gst-badge--default">' + escHtml(s('profiles.detail.meta.allLocales', 'all locales')) + '</span>';
        return '<div class="gst-prof-scope">' + sitesHtml + '</div>'
             + '<div class="gst-prof-scope" style="margin-top: 4px">' + localesHtml + '</div>';
    }

    /** Render the three-bar tuning column. */
    function tuningCell(p) {
        // v1: actual pin / synonym counts require live Graph calls we haven't
        // wired yet. Bars show the semantic-blend weight only; pins/syns rows
        // collapse to "—" until the counts arrive.
        var sw = (typeof p.semanticWeight === 'number') ? p.semanticWeight : 0;
        var swPct = Math.max(0, Math.min(100, Math.abs(sw) * 100));
        var swDisplay = (sw === 0) ? '—' : sw.toFixed(2);
        return '<div class="gst-prof-bars">'
            +     '<span class="gst-prof-bars__name">pins</span>'
            +     '<span class="gst-prof-bars__bar" style="--w: 0%"></span>'
            +     '<span class="gst-prof-bars__num">—</span>'
            +     '<span class="gst-prof-bars__name">syns</span>'
            +     '<span class="gst-prof-bars__bar muted" style="--w: 0%"></span>'
            +     '<span class="gst-prof-bars__num">—</span>'
            +     '<span class="gst-prof-bars__name">sem.</span>'
            +     '<span class="gst-prof-bars__bar" style="--w: ' + swPct + '%"></span>'
            +     '<span class="gst-prof-bars__num">' + swDisplay + '</span>'
            +  '</div>';
    }

    function profileCell(p) {
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
        return '<svg class="gst-prof-arrow" viewBox="0 0 24 24" fill="none" stroke="currentColor" '
            + 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"/></svg>';
    }

    /** ---------------- INDEX ---------------- */
    function index(opts) {
        opts = opts || {};
        // Detail URL is the index URL with a `?key=...` query so the CMS
        // shell maps both surfaces to the same registered menu item.
        var detailUrlBase = opts.detailUrlBase || '/EPiServer/cms/graphsearchtools/profiles?key=';

        var tableHost  = document.getElementById('gst-prof-table-host');
        var emptyEl    = document.getElementById('gst-prof-empty');
        var alertEl    = document.getElementById('gst-alert');
        var searchEl   = document.getElementById('gst-prof-search');
        var siteEl     = document.getElementById('gst-prof-site-filter');
        var localeEl   = document.getElementById('gst-prof-locale-filter');
        var countEl    = document.getElementById('gst-prof-count');

        if (!tableHost) return;

        GST.showLoading(tableHost);

        GST.fetchJson(API_BASE).then(function(profiles) {
            if (!profiles || profiles.length === 0) {
                tableHost.innerHTML = '';
                if (emptyEl) emptyEl.hidden = false;
                renderStats([]);
                if (countEl) countEl.textContent = '';
                return;
            }
            renderStats(profiles);
            populateFilters(profiles);
            renderTable(profiles);
        }).catch(function(err) {
            tableHost.innerHTML = '';
            showAlert(s('profiles.requestFailed', 'Failed to load profiles.'), 'danger');
            console.error('Profiles list failed', err);
        });

        function showAlert(msg, kind) {
            if (!alertEl) return;
            alertEl.className = 'gst-alert gst-alert--' + (kind || 'warning');
            alertEl.textContent = msg;
            alertEl.hidden = false;
        }

        function renderStats(profiles) {
            var sites = new Set();
            var locales = new Set();
            profiles.forEach(function(p) {
                (p.sites || []).forEach(function(x) { sites.add(x); });
                (p.locales || []).forEach(function(x) { locales.add(x); });
            });

            setText('gst-prof-stat-count', profiles.length);
            var subParts = [];
            if (sites.size)   subParts.push(sites.size + ' ' + s('profiles.stats.sites', 'sites'));
            if (locales.size) subParts.push(locales.size + ' ' + s('profiles.stats.locales', 'locales'));
            setText('gst-prof-stat-count-sub', subParts.join(' · '));

            // Pinned + synonym counts require Graph calls — scaffolded with em
            // dashes until the counts arrive (see ProfilesService note).
            setText('gst-prof-stat-pinned', '—');
            setText('gst-prof-stat-synonyms', '—');
        }

        function setText(id, value) {
            var el = document.getElementById(id);
            if (el) el.textContent = value == null ? '' : String(value);
        }

        function populateFilters(profiles) {
            var sites = new Set();
            var locales = new Set();
            profiles.forEach(function(p) {
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

        function renderTable(profiles) {
            var table = document.createElement('table');
            table.className = 'gst-table gst-prof-table';
            table.innerHTML =
                '<thead><tr>'
                + '<th style="width: 28%">' + escHtml(s('profiles.cols.profile', 'Profile')) + '</th>'
                + '<th style="width: 18%" class="col-scope">' + escHtml(s('profiles.cols.scope', 'Sites & locales')) + '</th>'
                + '<th style="width: 22%" class="col-tuning">' + escHtml(s('profiles.cols.tuning', 'Tuning')) + '</th>'
                + '<th style="width: 18%">' + escHtml(s('profiles.cols.status', 'Status')) + '</th>'
                + '<th>' + escHtml(s('profiles.cols.lastEdited', 'Last edited')) + '</th>'
                + '<th style="width: 32px"></th>'
                + '</tr></thead><tbody></tbody>';
            tableHost.innerHTML = '';
            tableHost.appendChild(table);

            var tbody = table.querySelector('tbody');
            profiles.forEach(function(p) {
                var tr = document.createElement('tr');
                tr.dataset.key = p.key || '';
                tr.dataset.search = ((p.displayName || '') + ' ' + (p.key || '') + ' ' + (p.descriptionResolved || '')).toLowerCase();
                tr.dataset.sites = (p.sites || []).join('|');
                tr.dataset.locales = (p.locales || []).join('|');

                tr.innerHTML =
                    '<td>' + profileCell(p) + '</td>'
                    + '<td class="col-scope">' + scopeCell(p) + '</td>'
                    + '<td class="col-tuning">' + tuningCell(p) + '</td>'
                    + '<td>' + statusBadge(typeof p.status === 'number' ? statusFromInt(p.status) : p.status) + '</td>'
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

        function statusFromInt(i) {
            return ['Tuned', 'NeedsReview', 'DocMissing', 'FreeForm', 'Cold'][i] || 'Cold';
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
                countEl.textContent = visible + ' ' + s('profiles.cols.profile', 'profiles').toLowerCase();
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
        var key = opts.profileKey || '';
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

        // Mount the Insights tab eagerly — it's the default-visible panel,
        // so its lanes need to populate on first paint without a user click.
        // The Pinned / Synonyms Aurora grids are lazy-mounted on first tab
        // activation: the Aurora module owns its own DOM and binding it
        // up-front would slow down the initial Insights paint.
        mountInsights();
        loadKpis(key);

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

        // Panel switching.
        var switcherBtns = document.querySelectorAll('.gst-prof-switcher__btn');
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
                // Audit lives in the Activity tab now (Aurora Phase 3C);
                // keep loading it for `details` too so deep-links via the
                // legacy tab name still work for at least one release.
                if (target === 'activity' || target === 'details') loadAudit(key);
            });
        });

        function activateTab(name) {
            var btn = document.getElementById('gst-prof-tab-' + name);
            if (btn) btn.click();
        }

        // Aurora Pinned grid — profile-scoped via the JS module's `init({scope})`
        // entry point. Resolves the profile's collection id up-front via
        // /api/profiles/{key}/pinned so the Aurora grid only walks the
        // matching collection (generic profiles fall through to the unscoped
        // view, which is what /api/profiles returns for them anyway).
        // Returns a Promise that resolves after init has run so callers
        // (the Insights "open pin flyout" shim) can wait before reaching
        // for the create button's wired-up click handler.
        var pinnedMountPromise = null;
        function mountPinned() {
            if (pinnedMountPromise) return pinnedMountPromise;
            if (!window.GST || !window.GST.pinned || !window.GST.pinned.aurora
                || typeof window.GST.pinned.aurora.init !== 'function') return Promise.resolve();
            if (!document.getElementById('gst-pin-aurora-rows')) return Promise.resolve(); // unwired panel

            // Generic profiles have no PinnedKey formula → no single collection
            // to narrow to. Mount the Aurora grid against all collections so
            // free-form pins are still surfaced — matches the legacy behaviour.
            if (opts.isGeneric) {
                window.GST.pinned.aurora.init({
                    scope: {
                        profileKey: key,
                        collectionId: null,
                        locales: opts.locales || []
                    }
                });
                pinnedMountPromise = Promise.resolve();
                return pinnedMountPromise;
            }

            // Resolve the profile's collection id via the existing scoped
            // endpoint. The first declared locale is enough for the formula
            // (PinnedKeyForLocale is stable per-locale; we only need a key
            // to look up the collection, not to filter rows).
            var firstLocale = (opts.locales && opts.locales[0]) || '';
            var firstSite = (opts.sites && opts.sites[0]) || '';
            var url = API_BASE + '/' + encodeURIComponent(key) + '/pinned'
                + '?site=' + encodeURIComponent(firstSite)
                + '&locale=' + encodeURIComponent(firstLocale);
            pinnedMountPromise = GST.fetchJson(url).then(function (resp) {
                window.GST.pinned.aurora.init({
                    scope: {
                        profileKey: key,
                        collectionId: (resp && resp.collectionId) || null,
                        locales: opts.locales || []
                    }
                });
            }).catch(function (err) {
                console.error('Profile pinned scope resolution failed', err);
                // Fall through to an unscoped mount so the grid still loads.
                window.GST.pinned.aurora.init({
                    scope: {
                        profileKey: key,
                        collectionId: null,
                        locales: opts.locales || []
                    }
                });
            });
            return pinnedMountPromise;
        }

        // Aurora Synonyms grid — profile-scoped via `init({scope.locales})`.
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
            mountInsightsPanel({
                profileKey: key,
                hasGraphQLDoc: !!opts.hasGraphQLDoc,
                queryAppliesPinned: typeof opts.queryAppliesPinned === 'boolean' ? opts.queryAppliesPinned : !!opts.hasGraphQLDoc,
                queryAppliesSynonyms: typeof opts.queryAppliesSynonyms === 'boolean' ? opts.queryAppliesSynonyms : true,
                getEditors: function () { return editors; },
                ensureSynonymsMounted: mountSynonyms,
                activateTab: activateTab
            });
        }
    }

    /** ---------------- INSIGHTS PANEL ---------------- */
    /*
     * Three lanes of phrase-level signal scoped to the active profile:
     * top phrases, zero-result phrases, low-CTR phrases. Backed by the
     * Search Logs API with a profileKey filter (the controller adds the
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
        var profileKey = opts.profileKey || '';
        if (!profileKey) return;

        var root = document.getElementById('gst-prof-ins');
        var alertEl = document.getElementById('gst-prof-ins-alert');
        var refreshBtn = document.getElementById('gst-prof-ins-refresh');
        var pillEls = root ? root.querySelectorAll('.gst-prof-ins__pill') : [];
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
            if (!alertEl) return;
            if (!msg) { alertEl.hidden = true; alertEl.textContent = ''; alertEl.classList.remove('gst-alert--danger'); return; }
            alertEl.hidden = false;
            alertEl.textContent = msg;
            alertEl.classList.add('gst-alert--danger');
        }

        // Window pill click → state change → refetch. Reset per-lane takes
        // so a fresh window opens compact rather than carrying over a
        // previously-expanded row count.
        pillEls.forEach(function (pill) {
            pill.addEventListener('click', function () {
                if (pill.classList.contains('is-active')) return;
                pillEls.forEach(function (p) { p.classList.remove('is-active'); });
                pill.classList.add('is-active');
                state.window = pill.dataset.window || '24h';
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
            var since = new Date(Date.now() - activeWindowMs()).toISOString();
            var url = SEARCHLOGS_API + '/' + LANE_API[lane]
                + '?since=' + encodeURIComponent(since)
                + '&take=' + state.takes[lane]
                + '&profileKey=' + encodeURIComponent(profileKey);
            var loc = activeLocale();
            if (loc) url += '&locale=' + encodeURIComponent(loc);
            return GST.fetchJson(url);
        }

        // Show-more bumps just one lane's take and re-renders that lane.
        // The reader caches the underlying aggregate per (window, profile,
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
                    + escHtml(s('profiles.detail.insights.loadFailed', 'Failed to load insights.'))
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
                var emptyKey = lane === 'zero' ? 'profiles.detail.insights.emptyZero'
                    : lane === 'lowctr' ? 'profiles.detail.insights.emptyLowCtr'
                    : 'profiles.detail.insights.empty';
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
            btn.textContent = s('profiles.detail.insights.showMore', 'Show more');
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
                    + ' ' + escHtml(s('profiles.detail.insights.ctrLabel', 'CTR'));
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
                + '<small>' + escHtml(s('profiles.detail.insights.hitsLabel', 'hits')) + '</small>';
            li.appendChild(countEl);

            // 4: actions — three icon buttons (preview / pin / synonym) on
            //    every row regardless of lane. Pin and synonym are disabled
            //    when the active profile's GraphQL doc doesn't apply them
            //    (so the buttons are still visible for affordance, but a
            //    tooltip explains why they can't be used here).
            var actEl = document.createElement('span');
            actEl.className = 'gst-prof-ins-row__actions';

            actEl.appendChild(makeIconButton(
                'preview',
                GST.icons.search,
                s('profiles.detail.insights.actionPreview', 'Preview this phrase'),
                false,
                function (ev) { ev.stopPropagation(); applyToPreview(row.phrase); }
            ));

            actEl.appendChild(makeIconButton(
                'pin',
                GST.icons.pin,
                opts.queryAppliesPinned
                    ? s('profiles.detail.insights.actionPin', 'Pin a result for this phrase')
                    : s('profiles.detail.insights.actionPinDisabled', 'This profile doesn\'t apply pinned results.'),
                !opts.queryAppliesPinned,
                function (ev) { ev.stopPropagation(); toggleInlineEditor(li, row, 'pin'); }
            ));

            actEl.appendChild(makeIconButton(
                'synonym',
                GST.icons.synonym,
                opts.queryAppliesSynonyms
                    ? s('profiles.detail.insights.actionSynonym', 'Add a synonym for this phrase')
                    : s('profiles.detail.insights.actionSynonymDisabled', 'This profile doesn\'t apply synonyms.'),
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

        // Initial load.
        fetchAll();
    }

    var _auditLoaded = false;

    /**
     * Load the 30-day search-activity KPIs for this profile and render them
     * into the #gst-prof-kpis host. Shares the renderer (and therefore the
     * visual treatment) with the global Insights tool — the only difference
     * is the ?profileKey scope on the API call.
     */
    function loadKpis(profileKey) {
        if (!window.GST || typeof window.GST.renderKpiCard !== 'function') return;
        var host = document.getElementById('gst-prof-kpis');
        if (!host) return;
        var BASE = window.GST_BASE_URL || '';
        var url = BASE + '/InsightsApi/SearchKpis?profileKey=' + encodeURIComponent(profileKey);
        GST.renderKpiCardLoading(host);
        GST.fetchJson(url)
            .then(function (k) { GST.renderKpiCard(host, k); })
            .catch(function () { GST.renderKpiCardError(host); });
    }

    function loadAudit(key) {
        if (_auditLoaded) return;
        _auditLoaded = true;

        var host = document.getElementById('gst-prof-audit-host');
        var badge = document.getElementById('gst-prof-audit-badge');
        if (!host) return;

        GST.showLoading(host);

        GST.fetchJson(API_BASE + '/' + encodeURIComponent(key) + '/audit?take=100').then(function(rows) {
            if (!rows || rows.length === 0) {
                GST.showEmpty(host, s('profiles.detail.audit.empty', 'No edits recorded yet.'));
                if (badge) badge.hidden = true;
                return;
            }
            renderAudit(host, rows);
            if (badge) {
                badge.hidden = false;
                badge.textContent = rows.length;
            }
        }).catch(function(err) {
            host.innerHTML = '<p class="gst-muted">' + escHtml(s('profiles.requestFailed', 'Failed to load audit log.')) + '</p>';
            console.error('Audit log failed', err);
        });
    }

    function renderAudit(host, rows) {
        var html = '<table class="gst-table gst-prof-audit-table"><thead><tr>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.when', 'When')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.who', 'Who')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.kind', 'Kind')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.action', 'Action')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.subject', 'Subject')) + '</th>'
            + '<th>' + escHtml(s('profiles.detail.audit.col.locale', 'Locale')) + '</th>'
            + '</tr></thead><tbody>';
        rows.forEach(function(r) {
            html += '<tr>'
                + '<td>' + escHtml(relativeTime(r.at)) + '</td>'
                + '<td>' + escHtml(r.actorName || r.actorId || '—') + '</td>'
                + '<td>' + escHtml(r.kind || '—') + '</td>'
                + '<td>' + escHtml(r.action || '—') + '</td>'
                + '<td>' + escHtml(r.subject || '—') + '</td>'
                + '<td>' + escHtml(r.locale || '—') + '</td>'
                + '</tr>';
        });
        html += '</tbody></table>';
        host.innerHTML = html;
    }

    window.GST = window.GST || {};
    window.GST.profiles = { index: index, detail: detail };
})();
