/**
 * Graph Search Tools — Semantic Weight Tuner.
 *
 * Tier-based ranking policy editor. Each row defines a token-count range
 * mapping to a `_ranking` mode + `_semanticWeight`. The tool persists the
 * policy to DDS via /SemanticTunerApi/Save and live-emits the equivalent
 * appsettings.json snippet so editors can paste the same shape into the
 * host config for cold-start fallback.
 *
 * Pure vanilla JS, no build step. Namespace: GST.semanticTuner.
 */
(function () {
    'use strict';

    var BASE = window.GST_BASE_URL || '';
    var STRINGS = (window.GST_STRINGS && window.GST_STRINGS.semanticTuner) || {};
    var SHARED = (window.GST_STRINGS && window.GST_STRINGS.shared) || {};

    var RANKINGS = [
        { value: 'Relevance', labelKey: 'ranking_relevance', fallback: 'Relevance' },
        { value: 'Semantic', labelKey: 'ranking_semantic', fallback: 'Semantic' },
        { value: 'BoostOnly', labelKey: 'ranking_boostonly', fallback: 'Boost only' },
        { value: 'Doc', labelKey: 'ranking_doc', fallback: 'Doc' }
    ];

    function s(key, fallback) {
        return STRINGS[key] || fallback;
    }

    function ajax(url, opts) {
        opts = opts || {};
        var headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (opts.body) headers['Content-Type'] = 'application/json';
        return fetch(url, {
            method: opts.method || 'GET',
            headers: headers,
            credentials: 'same-origin',
            body: opts.body ? JSON.stringify(opts.body) : undefined
        }).then(function (resp) {
            if (!resp.ok) {
                return resp.text().then(function (t) {
                    var msg = SHARED.failed || 'Request failed';
                    try { var p = t ? JSON.parse(t) : null; if (p && p.message) msg = p.message; } catch (_) {}
                    var err = new Error(msg + ' (' + resp.status + ')');
                    err.status = resp.status;
                    throw err;
                });
            }
            return resp.status === 204 ? null : resp.json();
        });
    }

    function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function (resolve, reject) {
            try {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                document.body.removeChild(ta);
                resolve();
            } catch (e) { reject(e); }
        });
    }

    function escHtml(v) {
        return String(v == null ? '' : v)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function setAlert(elem, message, isError) {
        if (!elem) return;
        if (!message) {
            elem.hidden = true;
            elem.textContent = '';
            elem.classList.remove('gst-alert--danger');
            return;
        }
        elem.hidden = false;
        elem.textContent = message;
        elem.classList.toggle('gst-alert--danger', !!isError);
    }

    /**
     * Render the tier table from `state.tiers`. Each row is a self-contained
     * fieldset bound by index — we re-render on add/remove rather than
     * tracking DOM nodes. Re-rendering blurs focus, so `focusIndex` lets us
     * preserve focus on the row the user just acted on.
     */
    function renderTiers(rootEl, state, focusIndex) {
        var container = rootEl.querySelector('[data-st="tiers"]');
        if (!container) return;

        if (!state.tiers || state.tiers.length === 0) {
            container.innerHTML = '<div class="gst-stuner-empty">' + escHtml(s('empty', 'No tiers defined yet. Add a tier to start tuning.')) + '</div>';
            return;
        }

        var rankingOptions = RANKINGS.map(function (r) {
            return '<option value="' + r.value + '">' + escHtml(s(r.labelKey, r.fallback)) + '</option>';
        }).join('');

        container.innerHTML = state.tiers.map(function (tier, idx) {
            var maxValue = tier.maxTokens == null ? '' : String(tier.maxTokens);
            var weight = typeof tier.semanticWeight === 'number' ? tier.semanticWeight : 0;
            var ranking = tier.ranking || 'Relevance';
            return [
                '<fieldset class="gst-stuner-tier" data-tier-idx="' + idx + '">',
                '  <div class="gst-stuner-tier__row">',
                '    <label class="gst-stuner-field">',
                '      <span>' + escHtml(s('tier_min_tokens', 'Min tokens')) + '</span>',
                '      <input type="number" min="0" step="1" data-field="minTokens" value="' + escHtml(tier.minTokens != null ? tier.minTokens : 0) + '" />',
                '    </label>',
                '    <label class="gst-stuner-field">',
                '      <span>' + escHtml(s('tier_max_tokens', 'Max tokens')) + '</span>',
                '      <input type="number" min="0" step="1" data-field="maxTokens" value="' + escHtml(maxValue) + '" placeholder="' + escHtml(s('tier_max_unbounded', '∞')) + '" />',
                '    </label>',
                '    <label class="gst-stuner-field">',
                '      <span>' + escHtml(s('tier_ranking', 'Ranking')) + '</span>',
                '      <select class="gst-select" data-field="ranking">' + rankingOptions + '</select>',
                '    </label>',
                '    <label class="gst-stuner-field gst-stuner-field--weight">',
                '      <span>' + escHtml(s('tier_weight', 'Semantic weight')) + ' <output data-field="weight-out">' + weight.toFixed(2) + '</output></span>',
                '      <input type="range" min="0" max="1" step="0.05" data-field="semanticWeight" value="' + escHtml(weight) + '" />',
                '    </label>',
                '    <button type="button" class="gst-btn gst-btn--sm gst-stuner-tier__remove" data-action="remove-tier" title="' + escHtml(s('remove_tier', 'Remove tier')) + '" aria-label="' + escHtml(s('remove_tier', 'Remove tier')) + '">×</button>',
                '  </div>',
                '  <div class="gst-stuner-tier__row">',
                '    <label class="gst-stuner-field gst-stuner-field--full">',
                '      <span>' + escHtml(s('tier_description', 'Description (optional)')) + '</span>',
                '      <input type="text" data-field="description" value="' + escHtml(tier.description || '') + '" />',
                '    </label>',
                '  </div>',
                '</fieldset>'
            ].join('');
        }).join('');

        // Pre-select dropdowns (innerHTML doesn't honour `selected` attribute when set after).
        Array.prototype.forEach.call(container.querySelectorAll('fieldset.gst-stuner-tier'), function (fs) {
            var idx = parseInt(fs.getAttribute('data-tier-idx'), 10);
            var tier = state.tiers[idx];
            var sel = fs.querySelector('[data-field="ranking"]');
            if (sel && tier && tier.ranking) sel.value = tier.ranking;
        });

        // Restore focus to a sensible field after re-render.
        if (focusIndex != null) {
            var focusFs = container.querySelector('fieldset[data-tier-idx="' + focusIndex + '"]');
            if (focusFs) {
                var input = focusFs.querySelector('input[data-field="minTokens"]');
                if (input) input.focus();
            }
        }
    }

    /**
     * Build the appsettings.json snippet that mirrors the persisted policy.
     * Format aims to match what `dotnet user-secrets` / `appsettings.json`
     * conventionally store: PascalCase property names, ranking serialized as
     * its string-name (the host's JSON config binder accepts this for enums).
     */
    function buildSnippet(tiers) {
        var indent = '  ';
        var lines = [];
        lines.push('{');
        lines.push(indent + '"CodeArt": {');
        lines.push(indent + indent + '"GraphSearchtools": {');
        lines.push(indent + indent + indent + '"SemanticTuning": {');

        if (!tiers || tiers.length === 0) {
            lines.push(indent + indent + indent + indent + '"Tiers": []');
        } else {
            lines.push(indent + indent + indent + indent + '"Tiers": [');
            tiers.forEach(function (t, i) {
                var parts = [];
                parts.push('"MinTokens": ' + (t.minTokens || 0));
                parts.push('"MaxTokens": ' + (t.maxTokens == null ? 'null' : t.maxTokens));
                parts.push('"Ranking": "' + (t.ranking || 'Relevance') + '"');
                parts.push('"SemanticWeight": ' + (typeof t.semanticWeight === 'number' ? t.semanticWeight : 0));
                if (t.description) {
                    parts.push('"Description": ' + JSON.stringify(t.description));
                }
                var row = indent + indent + indent + indent + indent + '{ ' + parts.join(', ') + ' }';
                if (i < tiers.length - 1) row += ',';
                lines.push(row);
            });
            lines.push(indent + indent + indent + indent + ']');
        }

        lines.push(indent + indent + indent + '}');
        lines.push(indent + indent + '}');
        lines.push(indent + '}');
        lines.push('}');
        return lines.join('\n');
    }

    function readTierFromDom(fieldset) {
        var idx = parseInt(fieldset.getAttribute('data-tier-idx'), 10);
        var min = fieldset.querySelector('[data-field="minTokens"]').value;
        var max = fieldset.querySelector('[data-field="maxTokens"]').value;
        var ranking = fieldset.querySelector('[data-field="ranking"]').value;
        var weight = fieldset.querySelector('[data-field="semanticWeight"]').value;
        var desc = fieldset.querySelector('[data-field="description"]').value;
        return {
            idx: idx,
            tier: {
                minTokens: parseInt(min, 10) || 0,
                maxTokens: max === '' || max == null ? null : parseInt(max, 10),
                ranking: ranking || 'Relevance',
                semanticWeight: parseFloat(weight) || 0,
                description: desc || null
            }
        };
    }

    function syncStateFromDom(rootEl, state) {
        var container = rootEl.querySelector('[data-st="tiers"]');
        if (!container) return;
        Array.prototype.forEach.call(container.querySelectorAll('fieldset.gst-stuner-tier'), function (fs) {
            var pair = readTierFromDom(fs);
            if (state.tiers[pair.idx]) {
                state.tiers[pair.idx] = pair.tier;
            }
        });
    }

    function refreshSnippet(rootEl, state) {
        var pre = rootEl.querySelector('[data-st="snippet"]');
        if (pre) pre.textContent = buildSnippet(state.tiers);
    }

    function boot(opts) {
        var rootEl = (opts && opts.container) || document.body;
        var alertBox = document.getElementById('gst-alert');
        var saveBtn = rootEl.querySelector('[data-st="save"]');
        var addBtn = rootEl.querySelector('[data-st="add"]');
        var copyBtn = rootEl.querySelector('[data-st="copy"]');
        var tiersEl = rootEl.querySelector('[data-st="tiers"]');

        var state = { tiers: [] };

        function render(focusIndex) {
            renderTiers(rootEl, state, focusIndex);
            refreshSnippet(rootEl, state);
        }

        function load() {
            ajax(BASE + '/SemanticTunerApi/Get').then(function (policy) {
                state.tiers = (policy && Array.isArray(policy.tiers))
                    ? policy.tiers.map(normaliseTier)
                    : [];
                render();
            }).catch(function (err) {
                setAlert(alertBox, s('load_failed', 'Could not load policy.') + ' ' + (err.message || ''), true);
            });
        }

        function normaliseTier(t) {
            return {
                minTokens: typeof t.minTokens === 'number' ? t.minTokens : 0,
                maxTokens: t.maxTokens == null ? null : t.maxTokens,
                ranking: t.ranking || 'Relevance',
                semanticWeight: typeof t.semanticWeight === 'number' ? t.semanticWeight : 0,
                description: t.description || null
            };
        }

        function save() {
            syncStateFromDom(rootEl, state);
            saveBtn.disabled = true;
            ajax(BASE + '/SemanticTunerApi/Save', { method: 'POST', body: { tiers: state.tiers } })
                .then(function (policy) {
                    state.tiers = (policy && Array.isArray(policy.tiers))
                        ? policy.tiers.map(normaliseTier)
                        : state.tiers;
                    render();
                    setAlert(alertBox, s('saved', 'Policy saved.'), false);
                    setTimeout(function () { setAlert(alertBox, '', false); }, 1500);
                })
                .catch(function (err) {
                    setAlert(alertBox, s('save_failed', 'Could not save policy.') + ' ' + (err.message || ''), true);
                })
                .finally(function () { saveBtn.disabled = false; });
        }

        if (addBtn) {
            addBtn.addEventListener('click', function () {
                syncStateFromDom(rootEl, state);
                // New tier defaults: continue from the previous open-ended boundary.
                var last = state.tiers[state.tiers.length - 1];
                var nextMin = 1;
                if (last) {
                    if (last.maxTokens != null) nextMin = last.maxTokens + 1;
                    else nextMin = (last.minTokens || 0) + 1;
                }
                state.tiers.push({
                    minTokens: nextMin,
                    maxTokens: null,
                    ranking: 'Semantic',
                    semanticWeight: 0.3,
                    description: null
                });
                render(state.tiers.length - 1);
            });
        }

        if (saveBtn) saveBtn.addEventListener('click', save);

        if (copyBtn) {
            copyBtn.addEventListener('click', function () {
                var pre = rootEl.querySelector('[data-st="snippet"]');
                copyText(pre ? pre.textContent : '').then(function () {
                    var orig = copyBtn.textContent;
                    copyBtn.textContent = s('copied', 'Copied');
                    copyBtn.disabled = true;
                    setTimeout(function () { copyBtn.textContent = orig; copyBtn.disabled = false; }, 1200);
                });
            });
        }

        // Delegated handlers for tier rows: live snippet/weight-output update + remove.
        if (tiersEl) {
            tiersEl.addEventListener('input', function (e) {
                var fs = e.target.closest('fieldset.gst-stuner-tier');
                if (!fs) return;
                if (e.target.getAttribute('data-field') === 'semanticWeight') {
                    var out = fs.querySelector('[data-field="weight-out"]');
                    if (out) out.textContent = (parseFloat(e.target.value) || 0).toFixed(2);
                }
                syncStateFromDom(rootEl, state);
                refreshSnippet(rootEl, state);
            });
            tiersEl.addEventListener('change', function (e) {
                if (e.target.tagName === 'SELECT') {
                    syncStateFromDom(rootEl, state);
                    refreshSnippet(rootEl, state);
                }
            });
            tiersEl.addEventListener('click', function (e) {
                var btn = e.target.closest('[data-action="remove-tier"]');
                if (!btn) return;
                var fs = btn.closest('fieldset.gst-stuner-tier');
                if (!fs) return;
                var idx = parseInt(fs.getAttribute('data-tier-idx'), 10);
                syncStateFromDom(rootEl, state);
                state.tiers.splice(idx, 1);
                render();
            });
        }

        load();

        return { reload: load, save: save, getState: function () { return state; } };
    }

    window.GST = window.GST || {};
    window.GST.semanticTuner = { boot: boot };
})();
