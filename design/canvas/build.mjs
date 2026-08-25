// Generates the Claude Design canvas artboards (*.dc.html + canvas.json) for the
// GraphSearchtools admin UI as it renders inside the CMS 13 shell.
//
//   node design/canvas/build.mjs        → writes dist/*.dc.html + dist/canvas.json
//
// Content comes from ../lib.mjs (shared with the design-system bundle). Each
// artboard embeds graphsearchtools.css verbatim because artboards are
// self-contained documents. See README.md for how the canvas is assembled.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { css, SHELL_CSS, screens, shellFrame } from '../lib.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const dist = join(here, 'dist');
mkdirSync(dist, { recursive: true });

function artboard(screen) {
  return `<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <script src="./support.js"></script>
</head>
<body>
<x-dc>
<helmet>
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&amp;display=swap">
  <style>
${SHELL_CSS}
    a { color: #0070ee; } a:hover { color: #0055b8; }
  </style>
  <style>
/* ── graphsearchtools.css — verbatim copy from the repo (build.mjs) ── */
${css}
  </style>
</helmet>
${shellFrame({ active: screen.active, body: screen.body, height: screen.height })}
</x-dc>
</body>
</html>
`;
}

const list = Object.values(screens);
for (const s of list) writeFileSync(join(dist, `${s.file}.dc.html`), artboard(s), 'utf8');

const GAP_X = 120, GAP_Y = 160, W = 1600;
const row1 = list.slice(0, 3), row2 = list.slice(3);
const row1H = Math.max(...row1.map(s => s.height));
const place = (row, y) => row.map((s, i) => ({ file: `${s.file}.dc.html`, title: s.title, x: i * (W + GAP_X), y, w: W, h: s.height }));

const canvas = {
  artboards: [...place(row1, 0), ...place(row2, row1H + GAP_Y)],
  annotations: [
    {
      id: 'source-note', x: 0, y: -260, w: 560,
      text: 'GraphSearchtools · CMS 13 — current UI, rebuilt from repo source.\n\nThe addon area (everything right of the grey nav) uses graphsearchtools.css verbatim and the real gst-* class names, so new features sketched here already match the codebase vocabulary. Sample data mirrors the Alloy demo site.\n\nThe Optimizely shell (top bar, icon rail, secondary nav) is an approximation drawn as a frame — it is not part of this repo.\n\nSource: design/canvas/build.mjs',
    },
  ],
  launch: { view: 'canvas' },
};
writeFileSync(join(dist, 'canvas.json'), JSON.stringify(canvas, null, 2) + '\n', 'utf8');

console.log(`wrote ${list.length} artboards + canvas.json to ${dist}`);
