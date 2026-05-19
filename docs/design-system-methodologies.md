# Design System Methodologies — Reference

This is a reference doc on the methodologies the industry uses to organise design
systems. It is **not** project-specific guidance — for GraphSearchtools' own
visual catalogue see [`design-system.md`](./design-system.md).

The goal here is to know the vocabulary, recognise the tradeoffs, and be able to
pick (or knowingly ignore) a methodology when adding shared UI patterns.

---

## 1. Why methodologies matter

A design system without a methodology is a pile of CSS classes and Figma
frames. Methodologies do four jobs:

1. **Vocabulary** — a shared word for "the thing in the corner" so designers,
   PMs, and devs stop arguing past each other.
2. **Composition rules** — how primitives combine into bigger things, and
   what is allowed to depend on what.
3. **Scaling guardrails** — what to do when you have 40 components and 11
   locales, not 4 components and English.
4. **Refactor leverage** — when the brand changes (or theming, or dark mode,
   or AI rewrites half the UI), the cost of the change is bounded.

None of this is free. Every methodology comes with overhead, and most teams
adopt at least two simultaneously (e.g. "Atomic Design for component layering
plus BEM for class names plus design tokens for primitives"). The art is in
knowing which problems each one actually solves.

---

## 2. Atomic Design (the main event)

### 2.1 Origin

Brad Frost first articulated **Atomic Design** in a 2013 blog post and later
expanded it into a book published in 2016 at
[atomicdesign.bradfrost.com](https://atomicdesign.bradfrost.com/). It is the
single most-cited mental model in the design-system world, and probably the
most-misapplied.

The motivation was specific to the moment: responsive web, growing component
libraries, and the realisation that "page-based" design (designers shipping
PSDs of full pages) didn't scale. Frost wanted a way to think about UI as a
**system of reusable parts** while still being able to talk about whole pages.

### 2.2 The chemistry analogy

Frost reached for high-school chemistry. Quoting [Chapter 2 of the
book](https://atomicdesign.bradfrost.com/chapter-2/):

> Atoms combine together to form molecules. These molecules can combine
> further to form relatively complex organisms.

The analogy is doing two things at once. First, it's a **composition story**:
small things combine into bigger things, and the bigger things inherit
behaviour from the smaller things. Second, it's a **vocabulary**: "atom" is
shorter and stickier than "primitive UI element," and the leap to "molecule"
and "organism" makes the hierarchy memorable.

### 2.3 The five levels

```
atoms  →  molecules  →  organisms  →  templates  →  pages
```

- **Atoms** — indivisible HTML elements. A label, an input, a button, a
  heading, a colour swatch. They can't be broken down further without losing
  function. They are usually useless on their own (a bare `<input>` floating
  in space) but they are the alphabet.

- **Molecules** — small functional groups of atoms. The canonical example is
  a search form: `label` + `input` + `button`. The molecule does one job and
  is the smallest unit that's actually useful in isolation.

- **Organisms** — relatively complex UI sections built from molecules and/or
  atoms. A site header (logo + nav + search) is the textbook example. A
  product card grid, a comment thread, an admin sidebar. Organisms are where
  "components" in the React/Vue sense usually live.

- **Templates** — page-level layouts that wire organisms together. Templates
  are content-agnostic skeletons: they describe "header here, sidebar here,
  three-column grid here, footer here" without committing to specific copy
  or images.

- **Pages** — templates with real content plugged in. The home page, the
  checkout page, the specific instance a user actually sees. Pages are where
  you discover whether your templates and organisms hold up under realistic
  data (very long names, missing images, RTL locales).

Frost is emphatic that this is **not a linear process**. You don't build all
the atoms, then all the molecules, then all the organisms. You move up and
down the hierarchy as you design — sometimes you discover an atom you need
because you're sketching a page; sometimes you build an organism and realise
its molecules want to be reused elsewhere.

### 2.4 Strengths

- **Mental model for composition.** It gives non-developers a way to think
  about reuse without learning React. "That's a molecule" lands faster than
  "that's a stateless functional component composing three primitives."
- **Naming discipline.** Forcing every component into a tier discourages the
  pile of one-off `MegaWidget` components that just grew over time.
- **Scales the conversation, not just the code.** Designers, PMs, and devs
  can co-own the hierarchy in a way they can't co-own a React tree.
- **Lines up nicely with Storybook.** Storybook's recommended folder
  structure (`atoms/`, `molecules/`, `organisms/`) is essentially Frost's
  hierarchy made literal. Whatever your views, Storybook normalised it.

### 2.5 Criticisms and pitfalls

The methodology has been around long enough that the critiques are now well
catalogued. The strongest:

**The chemistry metaphor breaks down quickly.** Atoms can't be broken down,
except that in 2025 Frost himself now says design tokens are
**subatomic particles** — see
[*Design Tokens + Atomic Design*](https://bradfrost.com/blog/post/design-tokens-atomic-design-%E2%9D%A4%EF%B8%8F/).
A real atom isn't divisible-but-actually-it-is. The metaphor is a mnemonic,
not a model, and treating it as a model leads to long Slack threads about
whether `IconButton` is an atom or a molecule.

**Fuzzy categorisation.** Is a card an organism or a molecule? Is a form
field with built-in validation an atom (because it's "one component") or a
molecule (because it composes label + input + error)? The answer is usually
"doesn't matter, pick one" but teams that take the taxonomy seriously waste
hours here. Sparkbox — who were early Atomic Design evangelists — now
[publicly say](https://sparkbox.com/foundry/iterating_on_atomic_design)
"no atomic design" when planning new systems, while still respecting the
underlying ideas.

**Premature abstraction.** The hierarchy invites you to invent an atom for
every primitive even when you have one consumer. Result: a `Spacer` atom, a
`Label` atom wrapping `<label>` with no extra behaviour, a `Heading` atom
with five variants nobody uses. The pattern works at Shopify-scale; at
ten-component-scale it's overhead.

**Doesn't model state or business logic.** Atomic Design is a *visual*
composition methodology. It says nothing about where you put data fetching,
where state lives, or how to wire up an organism that depends on five
different stores. Modern critiques (see [Design Systems Collective: *Is
Atomic Design Still Relevant in 2025?*](https://www.designsystemscollective.com/is-atomic-design-still-relevant-in-2025-d9c214788cfe))
argue this gap is why Feature-Sliced Design and domain-driven component
organisation are eating into Atomic Design's mindshare for stateful apps.

**Top-down generation conflicts with bottom-up assembly.** AI design tools
(Galileo, Vercel's v0, Figma Make) generate layouts top-down from a prompt.
Atomic Design assumes you assemble bottom-up. The two workflows are
reconcilable but require the team to be explicit about which direction
they're working in.

### 2.6 Frost's own retrospective (2023–2025)

Frost has not been quiet about the critique. His talk
[*Is Atomic Design Dead?* at SmashingConf NY
2024](https://www.youtube.com/watch?v=-3Pji_frbII) and the 2023 FRONT Zurich
version both push back, but with notable concessions:

- He admits the **biggest gap is design tokens**, which didn't exist as a
  named concept in 2013. He now frames tokens as
  *subatomic* — see the [2025 update post](https://bradfrost.com/blog/post/design-tokens-atomic-design-%E2%9D%A4%EF%B8%8F/)
  and the [Subatomic course](https://designtokenscourse.com/) he built with
  Ian Frost.
- He treats Atomic Design as a *thinking tool* rather than a folder
  structure. The taxonomy is for conversation; the file system can do
  whatever you want.
- He argues it matters *more* in the AI era because LLMs need a structured
  component vocabulary to generate against. Whether that argument holds up
  is still being litigated.

### 2.7 Real-world adoption

Atomic Design is most visible in three places:

1. **Storybook conventions.** The default folder structure in every
   Storybook tutorial since ~2017 has been atoms/molecules/organisms.
2. **Shopify Polaris.** Frost's vocabulary is name-checked in early Polaris
   write-ups (see [Shopify: *What Led Us to Consider Polaris*](https://www.shopify.com/partners/blog/design-system)),
   though the public site now organises components by **function** (Actions,
   Layout, Selection and input, Feedback) rather than by atomic tier.
3. **Component libraries at agency scale.** Sparkbox, Big Nerd Ranch, and
   most boutique design-systems consultancies use it as the default starting
   vocabulary even when they evolve away from it.

Notably, the **big-tech design systems** (Material, Carbon, Fluent, Polaris)
all **avoid** atomic terminology in their public docs. They use functional
categories ("Forms," "Navigation") at the component level and reserve their
hierarchy talk for the **token tier system** (see Section 5).

### 2.8 Verdict

Atomic Design earns its reputation as the lingua franca but is rarely the
*whole* answer. Use it for:

- Talking to designers about composition.
- Folder structure on greenfield projects (until you outgrow it).
- Naming the difference between "this is a primitive" and "this is a
  feature."

Don't use it as:

- An ontology to argue about in code review.
- A substitute for thinking about state, data, or business logic.
- A replacement for design tokens, which it doesn't model and which Frost
  himself now bolts on as a subatomic layer.

---

## 3. CSS naming and architecture methodologies

These three are about **CSS at scale**, not component composition. They
predate or overlap with Atomic Design and solve a different problem: how do
you stop your stylesheet turning into a specificity warzone.

### 3.1 BEM (Block, Element, Modifier)

Originated at Yandex; documented at [getbem.com](https://getbem.com/introduction/).
BEM is a **naming convention**, not a methodology in the architectural sense.

```css
.button                       /* block */
.button__icon                 /* element belonging to block */
.button--primary              /* modifier on block */
.button__icon--small          /* modifier on element */
```

The syntax (`__` for elements, `--` for modifiers) is deliberately ugly so
the structure is unambiguous at a glance.

**Core idea:** every class is flat (single selector, no nesting), and every
class encodes its own component identity. There is no `.button .icon` —
there is only `.button__icon`. This collapses CSS specificity to a single
class everywhere, which makes overrides predictable.

**Relation to Atomic Design:** orthogonal. BEM tells you how to name the
class on an atom or a molecule; it doesn't tell you how to compose them.
Many teams use both.

**Variation worth knowing:** Salesforce SLDS uses BEM-with-underscores
(`slds-text-heading_large`) instead of double dashes because double dashes
break in XML/HTML comments. Pragmatic but it diverges from the
[canonical spec](https://getbem.com/introduction/).

### 3.2 ITCSS (Inverted Triangle CSS)

Harry Roberts' methodology, sketched at
[csswizardry.com](https://csswizardry.com/2018/11/itcss-and-skillshare/).
ITCSS orders CSS files by **specificity and reach**, from low-specificity
generic rules at the top to high-specificity overrides at the bottom:

```
Settings   — variables, config, no CSS output
Tools      — mixins, functions, no CSS output
Generic    — resets, normalize, box-sizing
Elements   — bare HTML tags (h1, p, a)
Objects    — design patterns (grid, media object), no cosmetics
Components — the actual UI (buttons, cards, navs)
Utilities  — single-purpose overrides (.u-hidden, .mt-2)
```

The triangle is "inverted" because the top is the widest reach (everything
inherits from `:root`) and the bottom is the most specific (a utility class
overrides a component).

**Core idea:** if your source order follows specificity, you almost never
have to write `!important`. Cascade becomes an asset instead of a footgun.

**Relation to Atomic Design:** complementary. ITCSS doesn't care what a
"component" is internally; it cares where it sits in the cascade. You can
build Atomic-Design components and put their CSS in the ITCSS Components
layer.

### 3.3 SMACSS (Scalable and Modular Architecture for CSS)

Jonathan Snook's 2011 methodology, documented at
[smacss.com](https://smacss.com/book/categorizing/). Slightly older than
ITCSS and a bit looser. Five categories:

- **Base** — element defaults (`html`, `body`, `a`). No classes or IDs.
- **Layout** — major page structure. Prefixed `l-` (`l-header`, `l-grid`).
- **Module** — reusable components. The bulk of the work.
- **State** — temporary states (`is-active`, `is-hidden`). Often toggled by
  JS. Snook controversially endorses `!important` here.
- **Theme** — visual variants that ride on top of everything else.

**Core idea:** categorise styles by **what they're for**, prefix them so
you can tell at a glance, and stop styling things by element selector inside
modules.

**Relation to Atomic Design:** SMACSS "Modules" map roughly to Atomic
"molecules + organisms," and SMACSS "Base" maps to "atoms." But SMACSS is
file-organisation-first; Atomic Design is composition-first. They can
coexist (Atomic Design names the components, SMACSS organises the
stylesheet), though most teams adopting modern tooling now reach for design
tokens (Section 5) rather than SMACSS' Theme layer.

### 3.4 Cross-cutting note

BEM, ITCSS, and SMACSS all assume **you write CSS**. CSS-in-JS, Tailwind,
and CSS Modules each subvert their premises:

- Tailwind makes utilities the only layer, collapsing the triangle.
- CSS Modules give you BEM-like scoping for free, with hashed class names.
- CSS-in-JS makes the cascade per-component, so ITCSS' ordering doesn't
  apply.

These methodologies remain influential as **mental models** even where their
literal file conventions have been replaced by tooling.

---

## 4. Major industry design systems

Each of these is a *product*, not a methodology, but each embeds a
methodology worth knowing.

### 4.1 Material Design (Google)

Material 3 ([m3.material.io](https://m3.material.io/foundations/design-tokens))
is built on a **token-first** architecture, not Atomic Design. The hierarchy
runs:

```
Reference tokens   → raw palette values (md.ref.palette.primary40)
System tokens      → semantic roles (md.sys.color.primary)
Component tokens   → per-component (md.comp.filled-button.container.color)
Custom tokens      → brand overrides
```

The genius (and burden) of Material is that **components self-style from
tokens**, so swapping a theme — including Android's Material You dynamic
colour pulled from the user's wallpaper — propagates everywhere without
touching component code.

Material avoids the atom/molecule vocabulary entirely. Where Frost would
say "atoms," Material says "tokens"; where he'd say "molecules and
organisms," Material says "components."

### 4.2 Carbon (IBM)

[Carbon](https://carbondesignsystem.com/elements/color/tokens/) is the most
**token-saturated** of the major systems. Tokens are
`$interactive-01`, `$ui-01`, `$text-02` — every visible style is
parameterised. Themes (white, gray-10, gray-90, gray-100) are essentially
swaps of the ~52 universal colour tokens.

Carbon ships as a monorepo (`@carbon/themes`, `@carbon/type`,
`@carbon/grid`, etc.), which makes the architecture explicit: tokens are
their own package, components are their own package, and components
consume tokens. v11 introduced **layer tokens** and **inline theming**
(nested themes inside a page) as the system matured.

Carbon's methodology, distilled: IBM Design Language → tokens → themes →
components → patterns. No atoms, no molecules; just disciplined token
layering.

### 4.3 Polaris (Shopify)

[Polaris](https://polaris-react.shopify.com/) is the closest of the big-tech
systems to Atomic Design — Shopify's [original blog post on adopting
Polaris](https://www.shopify.com/partners/blog/design-system) explicitly
cites Frost — but the public docs organise components by **purpose**, not
tier:

```
Actions · Layout and structure · Selection and input · Images and icons · Feedback indicators
```

The "atomic" influence shows up in the token system rather than the
component taxonomy. Polaris design tokens are pitched as "atomic design
values" in CSS custom properties / JS constants. The 2025 unified Polaris
is shifting to Web Components for cross-surface use across Admin, Checkout,
and Customer Accounts.

### 4.4 Fluent 2 (Microsoft)

[Fluent 2](https://fluent2.microsoft.design/) uses a **two-tier** token
system on the web (global + alias) and a **three-tier** system on Apple
platforms (global + alias + control). On the web, the libraries are
`@fluentui/design-tokens` and `@fluentui/react-components` (v9), with the
Web Components implementation backed by FAST.

Fluent's notable architectural quirk: design tokens can be **scoped per DOM
subtree** via FASTElement, so a sidebar can have a different theme from the
main content without separate stylesheets. The in-development **Fluent
Semantic Tokens (FST)** layer adds optional, component-specific tokens with
a fallback to Fluent 2 tokens — a pragmatic answer to the "tokens are too
generic for some components" problem.

### 4.5 Lightning Design System (Salesforce)

[SLDS](https://developer.salesforce.com/docs/platform/lwc/guide/create-components-css-slds.html)
is the most BEM-forward of the major systems, but uses underscore-modifier
syntax (`slds-text-heading_large`) because double dashes break in XML
contexts. Architecturally it has four pillars: design tokens, utilities,
guidelines, component blueprints. SLDS 2 (Spring '25) introduced
**styling hooks** — CSS custom properties that separate structure from
visual design, letting consumers theme components without forking them.

### 4.6 Pattern across the big five

| System    | Token tiers              | Component vocabulary           | CSS convention   |
|-----------|--------------------------|--------------------------------|------------------|
| Material  | ref / sys / comp / custom| functional categories          | tokens-in-CSS    |
| Carbon    | global / component       | functional, layered            | Sass tokens (`$`) |
| Polaris   | atomic tokens            | functional categories          | tokens + utilities |
| Fluent 2  | global / alias (/ control)| component-centric             | CSS custom props |
| SLDS      | tokens + styling hooks   | blueprints                     | BEM (modified)   |

**None of them ship "molecules" or "organisms" as a public concept.** The
abstraction that has won at scale is **tokens**, not atomic tiers.

---

## 5. Design tokens as a cross-cutting concern

Tokens are the single most important methodology innovation since Atomic
Design. They are also the thing most of the methodologies above silently
converged on.

### 5.1 The standard

The [W3C Design Tokens Community Group](https://www.designtokens.org/)
reached its **first stable spec** in October 2025
([announcement](https://www.w3.org/community/design-tokens/2025/10/28/design-tokens-specification-reaches-first-stable-version/)).
Contributors include Adobe, Google, Microsoft, Meta, Figma, Salesforce, and
Shopify — essentially everyone with a stake.

The spec is a JSON-based, vendor-neutral format. Two things to know:

1. It's a **Community Group Report**, not a W3C Recommendation. It's a
   convention with momentum, not a formal standard.
2. It defines tokens, types, and an aliasing system — so one token can
   reference another. This is what enables tier layering.

### 5.2 The three-tier model

The widely-adopted token taxonomy (used by Material, Carbon, Polaris,
Fluent, and SLDS in various forms):

```
Tier 1: Primitive   → raw values        → color.blue.600 = #0052CC
Tier 2: Semantic    → role / intent     → color.interactive = {color.blue.600}
Tier 3: Component   → context-specific  → button.background = {color.interactive}
```

Each tier **aliases** the one below. Change the primitive, and everything
flows up. Change the semantic, and only components consuming that role
change. Themes are implemented by swapping the **semantic-to-primitive
mapping** while keeping component code identical.

This is the architecture Frost has now retrofitted onto Atomic Design,
calling tokens **subatomic** in his 2025 [*Design Tokens + Atomic
Design*](https://bradfrost.com/blog/post/design-tokens-atomic-design-%E2%9D%A4%EF%B8%8F/)
post and the accompanying
[Subatomic course](https://designtokenscourse.com/). The phrase "subatomic
particles" first appears in his 2019
[*Extending Atomic Design*](https://bradfrost.com/blog/post/extending-atomic-design/)
post, but 2025 is when he made it the centrepiece.

### 5.3 Why tokens matter more than the taxonomy

The honest read of the last decade: **tokens are doing most of the work
Atomic Design promised**.

- Renaming `.button` to `.atom-button` doesn't make theming easier; pulling
  `button.background` from a token does.
- Folder layout doesn't reduce duplication; a primitive palette referenced
  by every component does.
- Cross-platform parity (web + iOS + Android + native code) is achievable
  with tokens (via Style Dictionary or the W3C JSON format) in a way it
  never was with a CSS-based methodology.

Atomic Design gave the industry vocabulary. Tokens gave it the
implementation. The two are complementary, but the centre of gravity has
moved.

---

## 6. Synthesis: when each pays off

Most teams don't need to adopt one methodology and reject the others. Some
honest guidance on when each one earns its overhead:

**Atomic Design** pays off when:
- You have non-developers participating in component conversations and need
  shared vocabulary.
- You're starting a system and want a default folder structure to argue
  about later.
- You have enough components (≈30+) that "where does this go" is becoming
  a real question.

It's overkill when:
- You have fewer than ~15 reusable components.
- Your team is small enough that vocabulary isn't the bottleneck.
- You're shipping a single app with no aspirations to extract a library.

**BEM** pays off when:
- You're writing hand-written CSS that isn't scoped by tooling.
- You need predictable specificity without a build step.

It's overkill (or actively unhelpful) when:
- You're using CSS Modules, Tailwind, or CSS-in-JS — they solve the
  scoping problem differently.
- Your team finds the syntax noisy and won't use it consistently. (Half-BEM
  is worse than no BEM.)

**ITCSS / SMACSS** pay off when:
- You're maintaining a stylesheet with hundreds of rules and the cascade
  is biting you.
- You're inheriting CSS and need a refactor target.

They're overkill when:
- Your CSS lives next to components and never grows beyond a few hundred
  lines per component.

**Design tokens** pay off when:
- You have or want **theming** (dark mode, brand variants, multi-tenant).
- You have **more than one platform** (web + mobile, web + email, web +
  Figma).
- You expect colour, spacing, or type values to change and not want to
  grep for hex codes.

They're overkill when:
- You have one product, one theme, one platform, and one developer. Even
  then they're cheap enough that "overkill" is a stretch — but the
  three-tier ceremony might be.

**The big design systems (Material, Carbon, Fluent, Polaris, SLDS)** are
useful as references even if you don't adopt them. Read their token
documentation before designing your own. They've made every mistake worth
making.

---

## 7. Honest take

The story the last decade tells is roughly this:

1. 2013: Atomic Design names the problem and gives everyone the words.
2. 2015–2018: BEM / ITCSS / SMACSS solve CSS-at-scale, mostly displaced
   later by component scoping in modern frameworks.
3. 2016–2020: Big-tech design systems ship and quietly stop using atomic
   terminology in their public docs.
4. 2020–2025: Design tokens win. Every major system converges on a
   primitive → semantic → component tier model. The W3C spec stabilises
   in October 2025.
5. 2025+: Frost himself adds tokens as a subatomic layer; the methodology
   that named the field is now scaffolding under a token system, not the
   other way around.

If you're starting fresh in 2026, the order to invest in is roughly:
**design tokens first**, then a component taxonomy (Atomic or
functional — pick whichever your team will actually use), then a CSS
convention only if your tooling doesn't already enforce one. The
inversion — taxonomy first, tokens as an afterthought — is the most
common way teams end up with a system that looks tidy and refuses to
re-theme.
