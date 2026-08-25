# Design canvas — GraphSearchtools in the CMS 13 shell

Source for the Claude Design canvas that mirrors the addon's current UI, used
as the grounded starting point when sketching new features.

- `build.mjs` — generates one `*.dc.html` artboard per screen plus `canvas.json`
  (layout) into `dist/` (gitignored). Screen content and the shell frame come
  from `../lib.mjs`, which reads `graphsearchtools.css` and the umage watermark
  straight from `src/GraphSearchtools/` — re-running after a CSS change
  refreshes every artboard.

Screens: Overview (`Main`), Search channels, Channel detail, Insights,
Pinned results, Synonyms. Sample data mirrors the Alloy demo site.

The Optimizely shell chrome (top bar, icon rail, secondary nav) is an
approximation drawn with `mock-*` classes; it is not part of this repo.
Everything to the right of the nav uses the real `gst-*` classes.

```bash
node design/canvas/build.mjs
```

Then re-seed the published canvas from `dist/` via `/design` in Claude Code.
