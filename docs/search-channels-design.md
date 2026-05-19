# Search Channels — Design Proposal

> **Status (2026-05-05):** Implemented as Phase 2.5. Two pieces of the original
> proposal were dropped during implementation: (a) per-channel synonym scoping
> (§4.2 below) — Graph admin synonyms are tenant-global, language-keyed only;
> (b) the Saved Queries → Diagnostics rename and per-channel "Try in production
> query" tab (§4.3 below) — Health is sufficient as the diagnostic surface.
> Sections marked ~~struck-through~~ describe the original proposal and are
> kept for context only.

The current addon exposes pinned results and synonyms as standalone surfaces that
mirror the Graph admin APIs. That works as a generic editor, but it does not
solve the marketer's actual problem: **a pinned result or synonym only takes
effect inside a specific GraphQL query the developer wrote**, and that query is
invisible to the marketer.

This proposal introduces **Search Channels** as the marketer-facing unit of
configuration. A channel is a developer-declared description of one search
surface in the customer solution (header search, product listing, knowledge
base, …). Pinned results are then scoped to channels instead of to the
tenant. (Synonyms remain tenant-global per the status note above; the
diagnostic playground was dropped — Health alone covers diagnostic needs.)

---

## 1. Why this changes the addon's shape

Today's flow:

> Marketer opens **Pinned** → enters a phrase → adds content → hopes the
> production query passes the right `_pinnedResults` argument so the pin actually
> fires.

Channel-based flow:

> Marketer opens **Header search** channel → "tune pinned results for English
> on the corporate site" → adds content → the addon shows whether the
> production query is wired up to consume it, and a **Try it** tab runs the
> developer's actual query so they can see the pin in place.

The marketer never has to know about pinned-result keys, synonym slot numbers,
or which `locale` enum the query accepts. The developer encodes that once at
registration time.

When no channels are registered the addon falls back to a single **Generic**
channel, which is identical to today's UX. Zero-config installs keep working.

---

## 2. Registration API

Code-first registration via DI. The fluent builder lives in
`Configuration/SearchChannelBuilder.cs` and is exposed off the existing
`AddGraphSearchtools` extension.

```csharp
services.AddGraphSearchtools(options =>
    {
        // Configure options here or in appsettings.json under "CodeArt:GraphSearchtools"
    })
    .AddSearchChannel("alloy-search", p => p
        .DisplayName("Alloy site search")
        .Description("Header search across the Alloy demo content.")
        .Locales("en")
        .SearchedFields("Name", "MetaDescription", "MainBody")
        .UsesPinnedKey("alloy-{locale}")
        .SemanticBlend(0.3, GraphRanking.Semantic)
        .GraphQLDocument("Queries/AlloySearch.graphql"));
```

> The example above mirrors what the Alloy CMS 12 sample site registers in
> `src/GraphSearchtools.SampleSite/Startup.cs`; the CMS 13 sample registers the
> same channel. Multi-site / multi-locale solutions add `Sites(...)` and
> more entries to `Locales(...)`.

### 2.1 Channel shape

```csharp
public sealed class SearchChannel
{
    public string Key { get; init; }                       // stable, e.g. "site-search"
    public LocalizedString DisplayName { get; init; }
    public LocalizedString? Description { get; init; }
    public IReadOnlyList<string> Sites { get; init; }      // SiteDefinition.Name; [] = all
    public IReadOnlyList<string> Locales { get; init; }    // ISO codes; [] = all
    public IReadOnlyList<string> SearchedFields { get; init; }
    public Func<string, string>? PinnedKeyForLocale { get; init; }   // null = no pin tuning
    public double SemanticWeight { get; init; }
    public GraphRanking Ranking { get; init; }
    public string? GraphQLDocumentPath { get; init; }      // resolved at runtime
    public IReadOnlyDictionary<string, object?> DefaultVariables { get; init; }
}
```

The fluent builder validates at startup:

- `Key` matches `[a-z0-9-]+` (used in URLs and storage keys).
- `Sites` resolve against `ISiteDefinitionRepository`. Unknown site names log a
  warning but don't throw — sample sites and Alloy installs evolve.
- `Locales` are normalized to lowercase BCP-47.
- `PinnedKeyForLocale` may be null; when null the channel is diagnostic-only
  (no marketer tuning surface). Synonym tuning is handled at the top-level
  Synonyms tool — see the supersede note on §4.2.
- `GraphQLDocument` path is resolved against the host's `ContentRootPath`. If
  the file is missing, log a warning; the channel still works for tuning, just
  the **Try it** tab is disabled.

### 2.2 Why a fluent builder, not attributes or config

- **Discoverability**: developers writing `Startup.cs` already think in terms of
  DI; attributes require scanning conventions we'd have to invent.
- **Type-safe references**: `Sites(...)` and `Locales(...)` accept lambdas or
  string lists, easy to refactor.
- **Composition**: a feature module in the host project can call
  `AddSearchChannel(...)` independently of where the addon is registered.

JSON config remains a second-class option for hosts that want to stand up a
channel without recompiling — bound from `CodeArt:GraphSearchtools:Channels[]`
with the same shape, minus the `PinnedKeyForLocale` lambda (string template
only).

### 2.3 Channel registry

```csharp
public interface ISearchChannelRegistry
{
    IReadOnlyList<SearchChannel> All { get; }
    SearchChannel? Get(string key);
    IEnumerable<SearchChannel> ForSite(string siteName);
}
```

Singleton, populated at `AddGraphSearchtools(...)` call time. Read by every
tool (Pinned, Synonyms, SavedQueries) to scope its UI.

---

## 3. Data model

Today, pinned collections and synonym blobs are stored at the Graph tenant
level keyed by language (synonyms) or by an arbitrary collection name (pins).
The addon does not track which production query consumes them.

After this change, the addon writes pinned/synonym data through the same Graph
admin APIs but **with a deterministic naming convention** derived from the
channel, so it can be read back and grouped per channel in the UI.

### 3.1 Naming convention

| Concept | Graph admin key | Example |
|---|---|---|
| Pinned collection | `{pinnedKey}` (developer-declared, possibly per-locale) | `site-en` |
| ~~Synonym slot~~ | ~~`{synonymSlot}` per language~~ | ~~`site` (en/da/sv)~~ |
| Saved query (legacy) | `gst:{name}` (unchanged) | `gst:my-test` |

The channel knows the keys; the marketer never types them. Multiple channels
sharing a key (e.g. two front-end queries that both call `_pinnedResults: ["site-en"]`)
deliberately share the same data — that is the developer's intent.

> Synonyms are tenant-global in Graph admin and live outside channels — see
> §4.2 supersede note.

### 3.2 Local storage

Graph stays the source of truth for pinned collections and synonym slots —
channel registration doesn't change that.

Two pieces of local state get a small `SearchChannelEdit` DDS table keyed by
`(channelKey, site, locale, timestamp)`:

- **Audit log** entries (one row per edit to the channel's pinned collections)
  feeding the per-channel Audit tab in §4.7. Originally deferred but pulled
  into v1 because the audit tab was promoted from "future enhancement" to a
  primary tab in the prototype.
- **Last-edited summary** (latest row per channel) feeding the index table's
  "Last edited" column and the stats row.

No other per-channel metadata is persisted in v1; channel registration itself
remains in-memory.

### 3.3 Site scoping

Graph itself does not partition pinned results or synonyms by CMS site —
they're tenant-global. Site scoping is therefore enforced **by convention in
the channel's keys**:

- A multi-site solution that wants per-site pins declares
  `UsesPinnedKey(locale => $"{siteName}-{locale}")` and produces unique keys.
- A single shared channel across sites uses one key and the marketer sees one
  configuration that applies everywhere.

The UI surfaces this honestly: the site picker is disabled when the channel
declares only one site, and labelled "Shared across sites" when the key
formula is site-independent.

### 3.4 Locale scoping

Synonyms in Graph already carry a language slot; the addon already maps slots
to languages (the recent locale-from-schema work). Pinned collections are
language-bound by convention via the key formula above. The channel's
`Locales` list drives the language picker — no free-form locale entry,
preventing typos that produce orphan data.

---

## 4. Tool integration

### 4.1 Pinned

**Today**: top-level menu entry; free-form list of pinned collections, marketer
types a phrase and collection name.

**With channels**: Pinned **stops being a top-level menu entry**. Pinned results
are always keyed (Graph has no "global pinned" surface), so they always belong
to a channel — there is no useful tenant-level Pinned view. Marketers reach
the Pinned editor via **Channels → {channel} → Pinned** tab.

The Pinned tab itself:

- **Site** picker (disabled if channel declares one site, or labelled
  "shared with {n} sites" if the key formula is site-independent).
- **Locale** picker constrained to the channel's locales.
- The resolved collection key is shown read-only as `Pinned key: site-en` with
  a deep-link hint to the GraphQL document line that wires it up.
- Phrase chips at the top, pinned content rows below, organic results shown
  inline below a divider for context (read-only).
- **Try-it side panel** (right rail of the Pinned tab) runs the registered
  GraphQL document and shows A/B columns: organic order vs. with-pins order.
  This is the live preview while editing — no need to leave the tab.

API change: `PinnedApiController` actions take a required `channelKey` query
param when at least one channel is registered. The controller resolves the
pinned key from the channel + locale combination instead of accepting an
arbitrary collection name. Generic-mode (no channels) keeps the free-form
shape for backwards compatibility.

### 4.2 ~~Synonyms~~ (superseded — see status note)

> **Superseded.** Optimizely Graph admin synonyms are tenant-global and
> language-keyed only — there is no per-channel synonym pool to scope. The
> scope switcher and per-channel Synonyms tab were dropped during
> implementation; Synonyms remains a top-level tool with the existing
> per-language editor, unchanged from Phase 1. The original proposal is kept
> below for context.

> ~~**Today**: top-level menu entry; per-language synonym blob editor, plus Global.~~
>
> ~~**With channels**: Synonyms **keeps a top-level menu entry**, unlike Pinned.
> Graph has a Global synonym slot that applies across every query regardless of
> which slot the query passes — that genuinely needs a tenant-level surface, so
> collapsing it under a single channel would hide it.~~
>
> ~~The top-level Synonyms tool gains a **scope switcher**:~~
>
> - ~~**Global** (the existing tenant-level editor, gated behind a warning that
>   changes affect every query that consumes the global slot).~~
> - ~~**Per channel** — picks a channel and edits its `SynonymSlot` for a chosen
>   locale. Equivalent to opening the Synonyms tab inside that channel's detail.~~
>
> ~~Each channel detail also has its own **Synonyms tab** for marketers who think
> in terms of "I'm tuning the header search" rather than "I'm editing the site
> synonym slot." Both surfaces edit the same data — the per-channel tab is just a
> filtered view.~~
>
> ~~A channel without a `SynonymSlot` declared hides its Synonyms tab —
> preventing marketers from editing data that won't be applied.~~

### 4.3 ~~Saved Queries → Diagnostics~~ (superseded — see status note)

> **Superseded.** Health is sufficient as the diagnostic surface for the
> tenant; Saved Queries stays a top-level playground tool under its original
> name. The per-channel "Try in production query" tab and the
> `Features.Diagnostics` flag were both reverted. The in-context Try-it side
> panel inside the Pinned tab (§4.7) is retained — it is framed as a live
> A/B preview of the editor, not a diagnostic surface. The original proposal
> is kept below for context.

> ~~The user is right that Saved Queries no longer pulls its weight as a
> top-level marketer feature. Reframe and demote:~~
>
> - ~~Rename the menu entry to **Diagnostics** (loc key
>   `/graphsearchtools/menu/diagnostics`).~~
> - ~~Hide from the menu by default; opt-in via
>   `Features.Diagnostics = true` (default `true` in dev/local, `false` in
>   production via convention or explicit config).~~
> - ~~Each channel gets a **Try it** tab inside its detail page that opens the
>   diagnostic runner pre-configured with the channel's GraphQL document and
>   default variables.~~
> - ~~Free-form ad-hoc query runner remains available under Diagnostics for
>   developers and support — but it is no longer the headline feature.~~
>
> ~~The existing `QueryRunnerService` keeps its current shape; what changes is who
> calls it and from which UI. The `SavedQueriesOptions.DefaultQuery` field
> becomes the **fallback for the Generic channel** when no channels are
> registered, preserving today's behavior.~~

### 4.4 Health

Unchanged. Health is tenant-level (gateway reachability, credentials, sync
freshness) and benefits no one by being scoped to a channel.

### 4.5 Autocomplete

Stays as a generic Graph-level tester. We could add a channel-bound mode
later, but autocomplete usually doesn't carry pinned/synonym semantics and the
generic tester is enough.

### 4.6 Overview

**Unchanged**. Overview keeps its existing role as the tools landing page (the
grid of `gst-tool-card`s linking to each tool). The channel dashboard lives
under its own top-nav entry — see §4.7. Keeping Overview separate avoids a
breaking change for hosts that link directly to it, and the channel dashboard
benefits from being its own URL anyway (saved in the marketer's tab).

### 4.7 Channels — new top-level surface

Channels becomes its own top-nav entry, sitting between Overview and Synonyms.
It has two pages:

**Channels index** (the landing). A `gst-card` containing:

1. A **stats row** above the card with four `gst-stat`s — channels registered,
   pinned phrases, synonym entries, % production traffic routed through a
   registered channel (the last metric requires telemetry from Phase 4 — show
   `—` until then).
2. A **filter toolbar** (search, site filter, locale filter).
3. A `gst-table` with one row per registered channel. Columns:
   - **Channel** — display name + sub-label, with the registration key and the
     GraphQL document path on a second line in `gst-mono`.
   - **Sites & locales** — two stacked rows of `gst-badge`s.
   - **Tuning** — three mini bars (pins, syns, semantic blend) with raw counts.
     Visual at-a-glance for "is this channel tuned at all?"
   - **Status** — `gst-badge` with semantic colour: success (tuned),
     warning (review pinned), danger (GraphQL document missing), default (cold
     / free-form).
   - **Last edited** — relative time + initials of last editor.
   - **→** — chevron to the channel detail.

A table beats cards once you have more than four channels — denser, scannable,
sortable. We chose the table over the per-channel-card layout originally
proposed.

**No "+ Register channel" UI button.** Registration is code-first via DI
(see §2). The page header instead carries a **Documentation** button that
links to the registration handbook.

**Channel detail** — `gst-prof-layout` 2-column grid:

- **Left rail**: a sticky `gst-card` with the full channel metadata as a `<dl>`
  (sites, locales, searched fields, pinned-key formula, ranking + semantic
  blend, GraphQL document path). Below the card, a `gst-alert--warning` shows
  when the pinned-key formula is shared across sites / locales — flagging that
  downstream edits affect more than the picker suggests.
- **Right column**: a `gst-card` with `gst-tabs`:
  1. **Overview** — wire-up snapshot (which fields, which controls, last
     touched). Quiet by default; useful for support cases.
  2. **Pinned results** — the editor described in §4.1, with the in-tab Try-it
     side panel (live A/B preview of organic vs. with-pins, not framed as
     Diagnostics).
  3. **Audit log** — chronological list of edits to this channel's pinned
     collections. Backed by the `SearchChannelEdit` DDS table mentioned in
     §3.2.

Tabs carry count badges where it adds signal (e.g. **Pinned results · 39**).

**Empty state** — when no channels are registered, the Channels index shows a
zero-state explaining what channels are and pointing at the registration
handbook. Pinned and Synonyms still work in their top-level / generic-mode
forms (see §5).

---

## 5. Generic channel (always present)

The addon **always** synthesises a `Generic` channel and lists it as the last
row in the Channels index. This isn't only an empty-state fallback — even with
five other channels registered, Generic remains the catchment for pinned
collections that don't match any registered channel's key formula.

```csharp
new SearchChannel {
    Key = "generic",
    DisplayName = "Generic",
    Sites = AllRegisteredSites,
    Locales = AllRegisteredLocales,
    PinnedKeyForLocale = null,    // free-form collection name editor
    GraphQLDocumentPath = null,   // Try-it side panel disabled
    ...
}
```

When `ISearchChannelRegistry.All` contains only Generic, the experience
collapses to today's behaviour: marketers see one channel in the index, the
Pinned tab shows the free-form collection-name editor, and the top-level
Synonyms / Saved Queries tools work as they did before. No regression for
current installs.

When other channels are registered, Generic shows a "Free-form" status and any
orphan pinned collections that the marketer or a previous admin tool created
outside the new naming convention.

---

## 6. Migration path

Existing installs that have already created pinned collections / synonym
groups via the current generic UI:

1. **Day 0**: developer adds `AddSearchChannel(...)` calls. Channels appear in
   the menu; existing collections still visible under the Generic channel.
2. **Day 1**: developer's channel declares `UsesPinnedKey("site-en")`. If a
   pinned collection already exists with that name, it is automatically picked
   up — no data move required, the convention is the link.
3. **Day 2**: marketer migrates entries that lived under non-conforming names
   into channel-keyed collections via a one-time **Move to channel** action in
   the Generic view (small new feature, not in v1 unless customers ask).

No destructive operations, no schema change, no offline migration tool.

---

## 7. What this means for the implementation plan

Insert as **Phase 2.5** between the playground (already shipped) and the
tuning power tools:

| Phase | Focus | Status |
|---|---|---|
| 0 | Framework fork | done |
| 1 | Pinned + Synonyms (generic) | done |
| 2 | Search playground / Saved Queries / Health / Autocomplete | done |
| **2.5** | **Search Channels registration + tool rescoping** | **proposed** |
| 3 | Tuning power tools (Decay, Semantic, Schema, Webhooks, Logs) | pending |
| 4 | Analytics & audits | pending |
| 5 | Relevancy Lab | pending |

Phase 2.5 deliverables (status reflects the 2026-05-05 amendment):

1. `SearchChannel`, `SearchChannelBuilder`, `ISearchChannelRegistry` types. **Shipped.**
2. Fluent `AddSearchChannel(...)` extension on `IGraphSearchtoolsBuilder`
   (introduce builder pattern; today's `AddGraphSearchtools` returns `void`).
   **Shipped.**
3. New **Channels** top-level menu entry (between Overview and Synonyms),
   with the index page (§4.7) and the channel detail page. **Shipped** —
   tabs reduced to **Overview, Pinned, Audit log** (Synonyms and
   Try-in-production tabs were dropped — see §4.2 / §4.3 supersede notes).
4. **Pinned removed from the top-level menu**; legacy `/Pinned` URL
   301-redirects to the Channels index. **Shipped.**
5. ~~**Synonyms keeps its top-level entry**, with a new Global vs. per-channel
   scope switcher (§4.2).~~ **Reverted** — Synonyms stays as the Phase 1
   per-language editor; no scope switcher.
6. ~~Saved Queries → Diagnostics rename + menu demotion + per-channel **Try it**
   tab.~~ **Reverted** — Saved Queries keeps its name and place. The Try-it
   side panel inside the Pinned tab is retained (in-context A/B preview, not
   a separate diagnostic surface).
7. New `SearchChannelEdit` DDS table for audit log + last-edited summary
   (§3.2). **Shipped.**
8. CSS additions to `graphsearchtools.css` under `gst-prof-*` and `gst-pin-*`
   prefixes. **Shipped.**
9. Localization keys for the new strings (English first, other 10 with
   `TODO_TRANSLATE`). **Shipped.**
10. Sample-site update: register two channels in the Alloy demo so reviewers
    can see the multi-channel UX without writing code. **Shipped.**
11. Tests:
    - Channel registration validation (key format, unknown sites). **Shipped.**
    - Pinned API rejects writes without a `channelKey` when at least one
      channel is registered (prevents stray writes). **Shipped.**
    - ~~Synonyms API accepts both Global and per-channel scopes.~~ **Dropped**
      (no per-channel synonym scope to test).
    - Generic-mode regression test: no channels registered → Channels index
      shows only the Generic row, the Generic channel's Pinned tab opens the
      free-form collection editor, the legacy `/Pinned` URL 301-redirects to
      the Channels index. **Shipped.**

Risks:

- **Existing customers' Phase 1/2 muscle memory** — Pinned and Synonyms
  re-organise. Mitigation: keep generic mode as a real fallback, document the
  upgrade in the README, and don't move data on disk.
- **Channel registration is opt-in** — many installs won't write channel code
  on day one. Mitigation: Overview's empty-state CTA and a working sample.
- **Lambda-based `PinnedKeyForLocale`** doesn't survive JSON config. Mitigation:
  JSON config supports a string template (`"site-{locale}"`); the lambda form
  is for code-first only.

---

## 8. Open questions

1. Does Optimizely's roadmap for Graph include a first-class "search
   configuration" object (fully managed by Graph, not synthesised by us via
   key conventions)? If yes, this design's `PinnedKeyForLocale` becomes a
   shim until that lands.
2. ~~Should channels support **inheritance** ("kb-search inherits from
   site-search but overrides synonym slot")?~~ **Resolved (2026-05-05):** moot
   now that synonym slots are not part of the channel shape. If a future need
   arises for inheriting `SearchedFields` / `SemanticBlend` defaults we'll
   revisit, but no concrete request yet.
3. Where does the **Relevancy Lab** (Phase 5) attach? Most likely to a
   channel (a channel's golden query set evaluated against itself), which
   strengthens the case for landing this phase before Phase 5.
