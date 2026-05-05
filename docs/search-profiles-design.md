# Search Profiles — Design Proposal

The current addon exposes pinned results and synonyms as standalone surfaces that
mirror the Graph admin APIs. That works as a generic editor, but it does not
solve the marketer's actual problem: **a pinned result or synonym only takes
effect inside a specific GraphQL query the developer wrote**, and that query is
invisible to the marketer.

This proposal introduces **Search Profiles** as the marketer-facing unit of
configuration. A profile is a developer-declared description of one search
surface in the customer solution (header search, product listing, knowledge
base, …). Pinned results, synonyms, and the diagnostic playground are then
scoped to profiles instead of to the tenant.

---

## 1. Why this changes the addon's shape

Today's flow:

> Marketer opens **Pinned** → enters a phrase → adds content → hopes the
> production query passes the right `_pinnedResults` argument so the pin actually
> fires.

Profile-based flow:

> Marketer opens **Header search** profile → "tune pinned results for English
> on the corporate site" → adds content → the addon shows whether the
> production query is wired up to consume it, and a **Try it** tab runs the
> developer's actual query so they can see the pin in place.

The marketer never has to know about pinned-result keys, synonym slot numbers,
or which `locale` enum the query accepts. The developer encodes that once at
registration time.

When no profiles are registered the addon falls back to a single **Generic**
profile, which is identical to today's UX. Zero-config installs keep working.

---

## 2. Registration API

Code-first registration via DI. The fluent builder lives in
`Configuration/SearchProfileBuilder.cs` and is exposed off the existing
`AddGraphSearchtools` extension.

```csharp
services.AddGraphSearchtools(o =>
    {
        Configuration.GetSection("CodeArt:GraphSearchtools").Bind(o);
    })
    .AddSearchProfile("site-search", p => p
        .DisplayName("/graphsearchtools/profiles/site-search/name")     // loc key
        .Description("/graphsearchtools/profiles/site-search/desc")
        .Sites("corporate", "blog")                                      // SiteDefinition.Name
        .Locales("en", "da", "sv")
        .SearchedFields("Name", "TeaserText", "MainBody", "Tags")
        .UsesSynonymSlot("site")                                         // matches `synonymSlot:`
        .UsesPinnedKey(locale => $"site-{locale}")                       // dynamic per locale
        .SemanticBlend(weight: 0.3, ranking: GraphRanking.Semantic)
        .GraphQLDocument("Queries/SiteSearch.graphql")                   // optional
        .Variables(new {                                                 // defaults
            limit = 20
        }))
    .AddSearchProfile("kb-search", p => p
        .DisplayName("Knowledge base")
        .Sites("support")
        .Locales("en")
        .SearchedFields("Title", "Body")
        .UsesSynonymSlot("kb")
        .UsesPinnedKey("kb")
        .GraphQLDocument("Queries/KbSearch.graphql"));
```

### 2.1 Profile shape

```csharp
public sealed class SearchProfile
{
    public string Key { get; init; }                       // stable, e.g. "site-search"
    public LocalizedString DisplayName { get; init; }
    public LocalizedString? Description { get; init; }
    public IReadOnlyList<string> Sites { get; init; }      // SiteDefinition.Name; [] = all
    public IReadOnlyList<string> Locales { get; init; }    // ISO codes; [] = all
    public IReadOnlyList<string> SearchedFields { get; init; }
    public string? SynonymSlot { get; init; }              // null = no synonym tuning
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
- `SynonymSlot` and `PinnedKey*` may be null; if both are null, the profile is
  diagnostic-only (no marketer tuning surface).
- `GraphQLDocument` path is resolved against the host's `ContentRootPath`. If
  the file is missing, log a warning; the profile still works for tuning, just
  the **Try it** tab is disabled.

### 2.2 Why a fluent builder, not attributes or config

- **Discoverability**: developers writing `Startup.cs` already think in terms of
  DI; attributes require scanning conventions we'd have to invent.
- **Type-safe references**: `Sites(...)` and `Locales(...)` accept lambdas or
  string lists, easy to refactor.
- **Composition**: a feature module in the host project can call
  `AddSearchProfile(...)` independently of where the addon is registered.

JSON config remains a second-class option for hosts that want to stand up a
profile without recompiling — bound from `CodeArt:GraphSearchtools:Profiles[]`
with the same shape, minus the `PinnedKeyForLocale` lambda (string template
only).

### 2.3 Profile registry

```csharp
public interface ISearchProfileRegistry
{
    IReadOnlyList<SearchProfile> All { get; }
    SearchProfile? Get(string key);
    IEnumerable<SearchProfile> ForSite(string siteName);
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
profile, so it can be read back and grouped per profile in the UI.

### 3.1 Naming convention

| Concept | Graph admin key | Example |
|---|---|---|
| Pinned collection | `{pinnedKey}` (developer-declared, possibly per-locale) | `site-en` |
| Synonym slot | `{synonymSlot}` per language | `site` (en/da/sv) |
| Saved query (legacy) | `gst:{name}` (unchanged) | `gst:my-test` |

The profile knows the keys; the marketer never types them. Multiple profiles
sharing a key (e.g. two front-end queries that both call `_pinnedResults: ["site-en"]`)
deliberately share the same data — that is the developer's intent.

### 3.2 Local storage

Graph stays the source of truth for pinned collections and synonym slots —
profile registration doesn't change that.

Two pieces of local state get a small `SearchProfileEdit` DDS table keyed by
`(profileKey, site, locale, timestamp)`:

- **Audit log** entries (one row per edit) feeding the per-profile Audit tab
  in §4.7. Originally deferred but pulled into v1 because the audit tab was
  promoted from "future enhancement" to a primary tab in the prototype.
- **Last-edited summary** (latest row per profile) feeding the index table's
  "Last edited" column and the stats row.

No other per-profile metadata is persisted in v1; profile registration itself
remains in-memory.

### 3.3 Site scoping

Graph itself does not partition pinned results or synonyms by CMS site —
they're tenant-global. Site scoping is therefore enforced **by convention in
the profile's keys**:

- A multi-site solution that wants per-site pins declares
  `UsesPinnedKey(locale => $"{siteName}-{locale}")` and produces unique keys.
- A single shared profile across sites uses one key and the marketer sees one
  configuration that applies everywhere.

The UI surfaces this honestly: the site picker is disabled when the profile
declares only one site, and labelled "Shared across sites" when the key
formula is site-independent.

### 3.4 Locale scoping

Synonyms in Graph already carry a language slot; the addon already maps slots
to languages (the recent locale-from-schema work). Pinned collections are
language-bound by convention via the key formula above. The profile's
`Locales` list drives the language picker — no free-form locale entry,
preventing typos that produce orphan data.

---

## 4. Tool integration

### 4.1 Pinned

**Today**: top-level menu entry; free-form list of pinned collections, marketer
types a phrase and collection name.

**With profiles**: Pinned **stops being a top-level menu entry**. Pinned results
are always keyed (Graph has no "global pinned" surface), so they always belong
to a profile — there is no useful tenant-level Pinned view. Marketers reach
the Pinned editor via **Profiles → {profile} → Pinned** tab.

The Pinned tab itself:

- **Site** picker (disabled if profile declares one site, or labelled
  "shared with {n} sites" if the key formula is site-independent).
- **Locale** picker constrained to the profile's locales.
- The resolved collection key is shown read-only as `Pinned key: site-en` with
  a deep-link hint to the GraphQL document line that wires it up.
- Phrase chips at the top, pinned content rows below, organic results shown
  inline below a divider for context (read-only).
- **Try-it side panel** (right rail of the Pinned tab) runs the registered
  GraphQL document and shows A/B columns: organic order vs. with-pins order.
  This is the live preview while editing — no need to leave the tab.

API change: `PinnedApiController` actions take a required `profileKey` query
param when at least one profile is registered. The controller resolves the
pinned key from the profile + locale combination instead of accepting an
arbitrary collection name. Generic-mode (no profiles) keeps the free-form
shape for backwards compatibility.

### 4.2 Synonyms

**Today**: top-level menu entry; per-language synonym blob editor, plus Global.

**With profiles**: Synonyms **keeps a top-level menu entry**, unlike Pinned.
Graph has a Global synonym slot that applies across every query regardless of
which slot the query passes — that genuinely needs a tenant-level surface, so
collapsing it under a single profile would hide it.

The top-level Synonyms tool gains a **scope switcher**:

- **Global** (the existing tenant-level editor, gated behind a warning that
  changes affect every query that consumes the global slot).
- **Per profile** — picks a profile and edits its `SynonymSlot` for a chosen
  locale. Equivalent to opening the Synonyms tab inside that profile's detail.

Each profile detail also has its own **Synonyms tab** for marketers who think
in terms of "I'm tuning the header search" rather than "I'm editing the site
synonym slot." Both surfaces edit the same data — the per-profile tab is just a
filtered view.

A profile without a `SynonymSlot` declared hides its Synonyms tab —
preventing marketers from editing data that won't be applied.

### 4.3 Saved Queries → Diagnostics

The user is right that Saved Queries no longer pulls its weight as a
top-level marketer feature. Reframe and demote:

- Rename the menu entry to **Diagnostics** (loc key
  `/graphsearchtools/menu/diagnostics`).
- Hide from the menu by default; opt-in via
  `Features.Diagnostics = true` (default `true` in dev/local, `false` in
  production via convention or explicit config).
- Each profile gets a **Try it** tab inside its detail page that opens the
  diagnostic runner pre-configured with the profile's GraphQL document and
  default variables.
- Free-form ad-hoc query runner remains available under Diagnostics for
  developers and support — but it is no longer the headline feature.

The existing `QueryRunnerService` keeps its current shape; what changes is who
calls it and from which UI. The `SavedQueriesOptions.DefaultQuery` field
becomes the **fallback for the Generic profile** when no profiles are
registered, preserving today's behavior.

### 4.4 Health

Unchanged. Health is tenant-level (gateway reachability, credentials, sync
freshness) and benefits no one by being scoped to a profile.

### 4.5 Autocomplete

Stays as a generic Graph-level tester. We could add a profile-bound mode
later, but autocomplete usually doesn't carry pinned/synonym semantics and the
generic tester is enough.

### 4.6 Overview

**Unchanged**. Overview keeps its existing role as the tools landing page (the
grid of `gst-tool-card`s linking to each tool). The profile dashboard lives
under its own top-nav entry — see §4.7. Keeping Overview separate avoids a
breaking change for hosts that link directly to it, and the profile dashboard
benefits from being its own URL anyway (saved in the marketer's tab).

### 4.7 Profiles — new top-level surface

Profiles becomes its own top-nav entry, sitting between Overview and Synonyms.
It has two pages:

**Profiles index** (the landing). A `gst-card` containing:

1. A **stats row** above the card with four `gst-stat`s — profiles registered,
   pinned phrases, synonym entries, % production traffic routed through a
   registered profile (the last metric requires telemetry from Phase 4 — show
   `—` until then).
2. A **filter toolbar** (search, site filter, locale filter).
3. A `gst-table` with one row per registered profile. Columns:
   - **Profile** — display name + sub-label, with the registration key and the
     GraphQL document path on a second line in `gst-mono`.
   - **Sites & locales** — two stacked rows of `gst-badge`s.
   - **Tuning** — three mini bars (pins, syns, semantic blend) with raw counts.
     Visual at-a-glance for "is this profile tuned at all?"
   - **Status** — `gst-badge` with semantic colour: success (tuned),
     warning (review pinned), danger (GraphQL document missing), default (cold
     / free-form).
   - **Last edited** — relative time + initials of last editor.
   - **→** — chevron to the profile detail.

A table beats cards once you have more than four profiles — denser, scannable,
sortable. We chose the table over the per-profile-card layout originally
proposed.

**No "+ Register profile" UI button.** Registration is code-first via DI
(see §2). The page header instead carries a **Documentation** button that
links to the registration handbook.

**Profile detail** — `gst-prof-layout` 2-column grid:

- **Left rail**: a sticky `gst-card` with the full profile metadata as a `<dl>`
  (sites, locales, searched fields, synonym slot, pinned-key formula, ranking
  + semantic blend, GraphQL document path). Below the card, a
  `gst-alert--warning` shows when the pinned-key formula is shared across
  sites / locales — flagging that downstream edits affect more than the picker
  suggests.
- **Right column**: a `gst-card` with `gst-tabs`:
  1. **Overview** — wire-up snapshot (which fields, which controls, last
     touched). Quiet by default; useful for support cases.
  2. **Pinned results** — the editor described in §4.1, with the in-tab Try-it
     side panel.
  3. **Synonyms** — the per-profile view from §4.2 (hidden when the profile
     has no `SynonymSlot`).
  4. **Try in production query** — full-runner mode of Diagnostics, scoped to
     this profile's GraphQL document. Same engine as the side panel in the
     Pinned tab, with more controls (variables, raw response, score / fulltext
     debug projections).
  5. **Audit log** — chronological list of edits to this profile's pinned
     collections and synonym slot. Backed by the `SearchProfileMetadata` DDS
     table mentioned in §3.2 (now in scope, since the audit tab needs it).

Tabs carry count badges where it adds signal (e.g. **Pinned results · 39**).

**Empty state** — when no profiles are registered, the Profiles index shows a
zero-state explaining what profiles are and pointing at the registration
handbook. Pinned and Synonyms still work in their top-level / generic-mode
forms (see §5).

---

## 5. Generic profile (always present)

The addon **always** synthesises a `Generic` profile and lists it as the last
row in the Profiles index. This isn't only an empty-state fallback — even with
five other profiles registered, Generic remains the catchment for pinned
collections and synonym groups that don't match any registered profile's key
formula.

```csharp
new SearchProfile {
    Key = "generic",
    DisplayName = "Generic",
    Sites = AllRegisteredSites,
    Locales = AllRegisteredLocales,
    SynonymSlot = null,           // expose Global + per-language editor
    PinnedKeyForLocale = null,    // free-form collection name editor
    GraphQLDocumentPath = null,   // SavedQueries' DefaultQuery applies (Try-it disabled)
    ...
}
```

When `ISearchProfileRegistry.All` contains only Generic, the experience
collapses to today's behaviour: marketers see one profile in the index, the
Pinned tab shows the free-form collection-name editor, the Synonyms top-level
tool's per-profile mode is empty, and Diagnostics works as it does today. No
regression for current installs.

When other profiles are registered, Generic shows a "Free-form" status and any
orphan pinned collections / synonym groups that the marketer or a previous
admin tool created outside the new naming convention.

---

## 6. Migration path

Existing installs that have already created pinned collections / synonym
groups via the current generic UI:

1. **Day 0**: developer adds `AddSearchProfile(...)` calls. Profiles appear in
   the menu; existing collections still visible under the Generic profile.
2. **Day 1**: developer's profile declares `UsesPinnedKey("site-en")`. If a
   pinned collection already exists with that name, it is automatically picked
   up — no data move required, the convention is the link.
3. **Day 2**: marketer migrates entries that lived under non-conforming names
   into profile-keyed collections via a one-time **Move to profile** action in
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
| **2.5** | **Search Profiles registration + tool rescoping** | **proposed** |
| 3 | Tuning power tools (Decay, Semantic, Schema, Webhooks, Logs) | pending |
| 4 | Analytics & audits | pending |
| 5 | Relevancy Lab | pending |

Phase 2.5 deliverables:

1. `SearchProfile`, `SearchProfileBuilder`, `ISearchProfileRegistry` types.
2. Fluent `AddSearchProfile(...)` extension on `IGraphSearchtoolsBuilder`
   (introduce builder pattern; today's `AddGraphSearchtools` returns `void`).
3. New **Profiles** top-level menu entry (between Overview and Synonyms),
   with the index page (§4.7) and the profile detail page (5 tabs: Overview,
   Pinned, Synonyms, Try in production query, Audit log).
4. **Pinned removed from the top-level menu**; the existing
   `Tools/Pinned/Index.cshtml` is repointed to live inside the profile detail's
   Pinned tab (or kept-but-redirected for hosts that link directly to it).
5. **Synonyms keeps its top-level entry**, with a new Global vs. per-profile
   scope switcher (§4.2).
6. Saved Queries → Diagnostics rename + menu demotion + per-profile **Try it**
   tab. The full-runner Diagnostics page is reused as the implementation of
   the per-profile Try-it tab and the side panel inside the Pinned tab.
7. New `SearchProfileEdit` DDS table for audit log + last-edited summary
   (§3.2).
8. CSS additions to `graphsearchtools.css` under `gst-prof-*` and `gst-pin-*`
   prefixes — see `docs/prototypes/search-profiles.html` for the reference
   styling that already mirrors the addon's design system.
9. Localization keys for the new strings (English first, other 10 with
   `TODO_TRANSLATE`).
10. Sample-site update: register two profiles in the Alloy demo so reviewers
    can see the multi-profile UX without writing code.
11. Tests:
    - Profile registration validation (key format, unknown sites).
    - Pinned API rejects writes without a `profileKey` when at least one
      profile is registered (prevents stray writes).
    - Synonyms API accepts both Global and per-profile scopes.
    - Generic-mode regression test: no profiles registered → Profiles index
      shows only the Generic row, the Generic profile's Pinned tab opens the
      free-form collection editor, the legacy `/Pinned` URL 301-redirects to
      the Generic profile detail.

Risks:

- **Existing customers' Phase 1/2 muscle memory** — Pinned and Synonyms
  re-organise. Mitigation: keep generic mode as a real fallback, document the
  upgrade in the README, and don't move data on disk.
- **Profile registration is opt-in** — many installs won't write profile code
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
2. Should profiles support **inheritance** ("kb-search inherits from
   site-search but overrides synonym slot")? Probably not in v1 — adds
   complexity, no concrete request yet.
3. Where does the **Relevancy Lab** (Phase 5) attach? Most likely to a
   profile (a profile's golden query set evaluated against itself), which
   strengthens the case for landing this phase before Phase 5.
