/**
 * Graph Search Tools — Decay & Factor Sandbox.
 *
 * Pure client-side preview: input controls drive a tiny vanilla SVG plot and
 * emit a GraphQL fragment editors paste into a Saved Query (or any Graph
 * client). No server calls. No DI. Two boot functions, one per panel.
 *
 * GraphQL syntax — `decay` and `factor` are sub-objects on a per-field where
 * clause, per docs/research/relevancy-optimization.md §4 and §5. We emit the
 * shape used in that doc's tuned-query example (§11). The exact set of
 * arguments Graph accepts may evolve — see the `// TODO: verify exact syntax`
 * note below if `linear`/`exp` decay variants need adjusting (the docs only
 * showed Gaussian via `decay: { ... }`).
 */
(function () {
    'use strict';

    // ───────── Helpers ─────────

    /** Parse a "30d" / "1km" / "100" string. Returns { value, unit }. */
    function parseScale(raw) {
        var m = String(raw || '').trim().match(/^(-?\d*\.?\d+)\s*([a-zA-Z]*)$/);
        if (!m) return { value: 0, unit: '' };
        return { value: parseFloat(m[1]) || 0, unit: m[2] || '' };
    }

    /** Format a scalar as a GraphQL literal — strings get quoted, numbers don't. */
    function gqlScalar(raw, opts) {
        opts = opts || {};
        var s = String(raw == null ? '' : raw).trim();
        if (s === '') return opts.fallback != null ? opts.fallback : '""';
        // Pure number — emit unquoted.
        if (/^-?\d*\.?\d+$/.test(s)) return s;
        // Otherwise quote (origin = "now", scale = "30d", …).
        return '"' + s.replace(/"/g, '\\"') + '"';
    }

    /** Copy text to clipboard with a graceful fallback. */
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

    /** Tiny localized-string lookup with English fallback. */
    function s(path, fallback) {
        var ds = (window.GST_STRINGS && window.GST_STRINGS.decaySandbox) || {};
        return ds[path] || fallback;
    }

    /** SVG namespace shorthand. */
    var SVGNS = 'http://www.w3.org/2000/svg';
    function svg(tag, attrs) {
        var el = document.createElementNS(SVGNS, tag);
        if (attrs) for (var k in attrs) el.setAttribute(k, attrs[k]);
        return el;
    }

    // ───────── Math ─────────

    var SAMPLES = 80;

    /**
     * Gaussian decay used in Optimizely Graph's `decay` clause, as documented
     * in the relevancy playbook §4. Returns score ∈ [0,1] for a distance d
     * from origin (in scale-units).
     *
     *   sigma  = scale * sqrt(-2 * ln(decay))
     *   score  = exp( -((max(0, |d - origin| - offset))^2) / (2 * sigma^2) )
     *
     * In the sandbox we render distance-from-origin directly (origin → 0),
     * which matches how editors think about the curve.
     */
    function decayScore(fn, d, scale, offset, decay) {
        var dist = Math.max(0, Math.abs(d) - offset);
        if (scale <= 0) return d === 0 ? 1 : 0;
        var clampedDecay = Math.max(0.0001, Math.min(0.9999, decay));
        if (fn === 'gauss') {
            var sigma = scale * Math.sqrt(-2 * Math.log(clampedDecay));
            if (sigma === 0) return dist === 0 ? 1 : 0;
            return Math.exp(-(dist * dist) / (2 * sigma * sigma));
        }
        if (fn === 'linear') {
            return Math.max(0, 1 - (dist * (1 - clampedDecay)) / scale);
        }
        // exp
        return Math.exp((dist * Math.log(clampedDecay)) / scale);
    }

    /**
     * Score after a `factor` modifier, as documented in §5. `x` is the raw
     * field value, `factor` the multiplier, `modifier` one of the five
     * Optimizely Graph names. Output is the multiplicative score boost the
     * field contributes (NOT clamped — the Y axis on the plot auto-scales to
     * the curve's max so editors can see relative shape).
     */
    function factorScore(modifier, x, factor) {
        var v = x * factor;
        switch (modifier) {
            case 'SQRT':       return v <= 0 ? 0 : Math.sqrt(v);
            case 'LOG':        return v <= -1 ? 0 : Math.log(v + 1);
            case 'RECIPROCAL': return 1 / Math.max(v, 1e-9);
            case 'SQUARE':     return v * v;
            default:           return v; // NONE
        }
    }

    // ───────── Plot ─────────

    var PLOT_W = 320, PLOT_H = 160, PLOT_PAD_X = 8, PLOT_PAD_Y = 6;

    function drawGrid(gridEl) {
        gridEl.textContent = '';
        // 4 horizontal gridlines (y = 0.25, 0.5, 0.75, 1.0 of plot height).
        for (var i = 1; i <= 4; i++) {
            var y = PLOT_PAD_Y + (PLOT_H - 2 * PLOT_PAD_Y) * (i / 4);
            gridEl.appendChild(svg('line', {
                x1: PLOT_PAD_X, x2: PLOT_W - PLOT_PAD_X,
                y1: y, y2: y,
                'class': 'gst-decay-plot__grid'
            }));
        }
        // 3 vertical gridlines.
        for (var j = 1; j <= 3; j++) {
            var x = PLOT_PAD_X + (PLOT_W - 2 * PLOT_PAD_X) * (j / 4);
            gridEl.appendChild(svg('line', {
                x1: x, x2: x,
                y1: PLOT_PAD_Y, y2: PLOT_H - PLOT_PAD_Y,
                'class': 'gst-decay-plot__grid'
            }));
        }
    }

    /**
     * Build an SVG `path` `d` attribute from sample points.
     * `points` is an array of [x ∈ [0,1], y ∈ [0,1]] (already normalized).
     */
    function buildPath(points) {
        var parts = [];
        for (var i = 0; i < points.length; i++) {
            var x = PLOT_PAD_X + (PLOT_W - 2 * PLOT_PAD_X) * points[i][0];
            var y = PLOT_H - PLOT_PAD_Y - (PLOT_H - 2 * PLOT_PAD_Y) * points[i][1];
            parts.push((i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2));
        }
        return parts.join(' ');
    }

    // ───────── Decay panel ─────────

    function bootDecay(opts) {
        var root = opts.container;
        if (!root) return null;

        var inputs = {};
        root.querySelectorAll('[data-d]').forEach(function (el) {
            var key = el.getAttribute('data-d');
            // Plot/grid/curve/output elements get tracked too; the controls are
            // distinguished by being <input> / pill containers.
            if (!inputs[key]) inputs[key] = el;
        });

        var pillSet = root.querySelector('[data-d="function"]');
        var fnState = { value: 'gauss' };
        if (pillSet) {
            pillSet.addEventListener('click', function (e) {
                var btn = e.target.closest('[data-value]');
                if (!btn) return;
                fnState.value = btn.getAttribute('data-value');
                pillSet.querySelectorAll('[data-value]').forEach(function (b) {
                    b.setAttribute('aria-checked', b === btn ? 'true' : 'false');
                });
                render();
            });
        }

        var copyBtn = root.querySelector('[data-d="copy"]');
        if (copyBtn) copyBtn.addEventListener('click', function () { copyFragment(copyBtn); });

        var rangeOut = root.querySelector('[data-d="decay-out"]');
        var axisScale = root.querySelector('[data-d="axis-scale"]');
        var axisMax = root.querySelector('[data-d="axis-max"]');
        var grid = root.querySelector('[data-d="grid"]');
        var curve = root.querySelector('[data-d="curve"]');
        var marker = root.querySelector('[data-d="scale-marker"]');
        var fragmentEl = root.querySelector('[data-d="fragment"]');

        drawGrid(grid);

        // Live update on every input change.
        root.querySelectorAll('input').forEach(function (el) {
            el.addEventListener('input', render);
            el.addEventListener('change', render);
        });

        function readState() {
            return {
                fn: fnState.value,
                field: (inputs.field && inputs.field.value) || 'PublishedDate',
                origin: (inputs.origin && inputs.origin.value) || 'now',
                scaleRaw: (inputs.scale && inputs.scale.value) || '30d',
                offsetRaw: (inputs.offset && inputs.offset.value) || '0d',
                decay: parseFloat((inputs.decay && inputs.decay.value) || '0.5')
            };
        }

        function render() {
            var st = readState();
            var scale = parseScale(st.scaleRaw);
            var offset = parseScale(st.offsetRaw);
            var scaleNum = scale.value || 1;
            var offsetNum = offset.value || 0;

            if (rangeOut) rangeOut.textContent = st.decay.toFixed(2);
            if (axisScale) axisScale.textContent = String(scaleNum) + (scale.unit || '');
            if (axisMax) axisMax.textContent = String(scaleNum * 3) + (scale.unit || '');

            // Sample 0..3*scale (X axis is "distance from origin").
            var xMax = scaleNum * 3;
            if (xMax <= 0) xMax = 1;
            var pts = [];
            for (var i = 0; i <= SAMPLES; i++) {
                var x = (i / SAMPLES) * xMax;
                var score = decayScore(st.fn, x, scaleNum, offsetNum, st.decay);
                pts.push([i / SAMPLES, Math.max(0, Math.min(1, score))]);
            }
            curve.setAttribute('d', buildPath(pts));

            // Vertical marker at d = scale (where the curve crosses `decay`).
            var markerX = PLOT_PAD_X + (PLOT_W - 2 * PLOT_PAD_X) * (scaleNum / xMax);
            marker.setAttribute('x1', markerX);
            marker.setAttribute('x2', markerX);
            marker.setAttribute('y1', PLOT_PAD_Y);
            marker.setAttribute('y2', PLOT_H - PLOT_PAD_Y);

            fragmentEl.textContent = buildFragment(st);
        }

        function buildFragment(st) {
            // TODO: verify exact syntax once Optimizely Graph publishes a schema
            // for the `linear` and `exp` decay variants. The relevancy playbook
            // §4 documents only the Gaussian shape (decay: { origin, scale,
            // rate }); we emit the same key set for the other two with a
            // `function` discriminator so editors can post-edit if needed.
            var rate = st.decay.toFixed(3).replace(/\.?0+$/, '') || '0.5';
            var lines;
            if (st.fn === 'gauss') {
                lines = [
                    '# Gaussian decay — paste into a query\'s `where:` clause',
                    st.field + ': {',
                    '  decay: {',
                    '    origin: ' + gqlScalar(st.origin, { fallback: '"now"' }) + ',',
                    '    scale: ' + gqlScalar(st.scaleRaw) + ',',
                    '    offset: ' + gqlScalar(st.offsetRaw) + ',',
                    '    rate: ' + rate,
                    '  }',
                    '}'
                ];
            } else {
                // Approximation — see TODO above.
                lines = [
                    '# ' + st.fn + ' decay — paste into a query\'s `where:` clause',
                    '# NOTE: Optimizely Graph documents Gaussian; verify ' + st.fn + ' support before shipping.',
                    st.field + ': {',
                    '  decay: {',
                    '    function: ' + st.fn.toUpperCase() + ',',
                    '    origin: ' + gqlScalar(st.origin, { fallback: '"now"' }) + ',',
                    '    scale: ' + gqlScalar(st.scaleRaw) + ',',
                    '    offset: ' + gqlScalar(st.offsetRaw) + ',',
                    '    rate: ' + rate,
                    '  }',
                    '}'
                ];
            }
            return lines.join('\n');
        }

        function copyFragment(btn) {
            copyText(fragmentEl.textContent).then(function () {
                if (!btn) return;
                var orig = btn.textContent;
                btn.textContent = s('copied', 'Copied');
                btn.disabled = true;
                setTimeout(function () { btn.textContent = orig; btn.disabled = false; }, 1200);
            });
        }

        render();
        return { render: render, copyFragment: function () { copyFragment(copyBtn); } };
    }

    // ───────── Factor panel ─────────

    function bootFactor(opts) {
        var root = opts.container;
        if (!root) return null;

        var inputs = {};
        root.querySelectorAll('[data-f]').forEach(function (el) {
            var key = el.getAttribute('data-f');
            if (!inputs[key]) inputs[key] = el;
        });

        var pillSet = root.querySelector('[data-f="modifier"]');
        var modState = { value: 'SQRT' };
        if (pillSet) {
            pillSet.addEventListener('click', function (e) {
                var btn = e.target.closest('[data-value]');
                if (!btn) return;
                modState.value = btn.getAttribute('data-value');
                pillSet.querySelectorAll('[data-value]').forEach(function (b) {
                    b.setAttribute('aria-checked', b === btn ? 'true' : 'false');
                });
                render();
            });
        }

        var copyBtn = root.querySelector('[data-f="copy"]');
        if (copyBtn) copyBtn.addEventListener('click', function () { copyFragment(copyBtn); });

        var grid = root.querySelector('[data-f="grid"]');
        var curve = root.querySelector('[data-f="curve"]');
        var fragmentEl = root.querySelector('[data-f="fragment"]');
        var axisMax = root.querySelector('[data-f="axis-max"]');

        drawGrid(grid);

        root.querySelectorAll('input').forEach(function (el) {
            el.addEventListener('input', render);
            el.addEventListener('change', render);
        });

        function readState() {
            return {
                modifier: modState.value,
                field: (inputs.field && inputs.field.value) || 'NumClicks',
                factor: parseFloat((inputs.factor && inputs.factor.value) || '1') || 0,
                missing: parseFloat((inputs.missing && inputs.missing.value) || '1') || 0
            };
        }

        function render() {
            var st = readState();
            // X range: 0..100 for most modifiers — that's a sensible range for
            // click counts, ratings × 20, etc. RECIPROCAL has a singularity at
            // 0; LOG is steepest at low values; squared scales fast — none of
            // those need a different domain, just a per-modifier Y-rescale.
            var xMax = 100;
            if (axisMax) axisMax.textContent = String(xMax);

            // First pass — compute all scores so we can find the peak for Y
            // normalization. Skip the singularity at 0 for RECIPROCAL.
            var rawPts = [];
            var peak = 0;
            for (var i = 0; i <= SAMPLES; i++) {
                var x = (i / SAMPLES) * xMax;
                var y = factorScore(st.modifier, x, st.factor);
                if (!isFinite(y)) y = 0;
                if (st.modifier === 'RECIPROCAL' && x === 0) y = 0; // hide pole
                rawPts.push([i / SAMPLES, y]);
                if (y > peak) peak = y;
            }
            if (peak <= 0) peak = 1;

            var pts = rawPts.map(function (p) { return [p[0], Math.max(0, Math.min(1, p[1] / peak))]; });
            curve.setAttribute('d', buildPath(pts));

            fragmentEl.textContent = buildFragment(st);
        }

        function buildFragment(st) {
            // TODO: verify exact syntax. The relevancy playbook §5 shows the
            // shape `<field>: { gt: 0, factor: { value: N, modifier: SQRT } }`,
            // which is what we emit here. `missing` isn't documented in the
            // playbook; we tag it on with a `# missing:` comment so editors
            // who have the schema for it can uncomment.
            var lines = [
                '# Factor modifier — paste into a query\'s `where:` clause',
                st.field + ': {',
                '  gt: 0,',
                '  factor: {',
                '    value: ' + st.factor + ',',
                '    modifier: ' + st.modifier,
                '  }',
                '  # missing: ' + st.missing,
                '}'
            ];
            return lines.join('\n');
        }

        function copyFragment(btn) {
            copyText(fragmentEl.textContent).then(function () {
                if (!btn) return;
                var orig = btn.textContent;
                btn.textContent = s('copied', 'Copied');
                btn.disabled = true;
                setTimeout(function () { btn.textContent = orig; btn.disabled = false; }, 1200);
            });
        }

        render();
        return { render: render, copyFragment: function () { copyFragment(copyBtn); } };
    }

    // ───────── Public surface ─────────

    window.GST = window.GST || {};
    window.GST.decaySandbox = {
        decay: bootDecay,
        factor: bootFactor
    };
})();
