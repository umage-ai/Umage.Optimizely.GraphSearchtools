# Design-system bundle — "Graph Search Tools" on claude.ai/design

Source for the Claude Design **design-system project** that lets designers (and
Claude Design) build new features for the addon from its real vocabulary.

- `build.mjs` — generates the bundle into `dist/` (gitignored):
  `styles.css` (graphsearchtools.css verbatim + the mock CMS shell frame),
  `foundations/*.html` and `components/*.html` preview cards, `screens/*.html`
  (the six current screens), `readme.md`, `SKILL.md`, `_ds_manifest.json`.
  Shared markup lives in `../lib.mjs`.
- Each card's first line is the `@dsCard` marker the Design System pane indexes.

```bash
node design/ds/build.mjs
```

Then push `dist/` to the project with Claude Code's `DesignSync` tool
(project "Graph Search Tools", id `2c4a5d94-0851-48cb-bc89-3bf8251cb397`):
list files → finalize a plan over `dist/` → write files. Sync incrementally;
never wholesale-replace what someone edited in the app.
