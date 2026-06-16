# ADR-33: Adopt Tabler 1.x as the CSS Framework for DuetsPad

## Status

Accepted

## Context

DuetsPad ([ADR-32](32_duetspad-supersede-replservice-with-new-output-model.md))
needs an admin-application-style visual vocabulary for a browser debug pad:
cards, tables, form layouts, buttons, modals, toasts, breadcrumbs, dropdowns,
alerts, and icons. This ADR selects a CSS framework; it does not decide whether
each component in that vocabulary is exposed as a DuetsPad primitive. The
selected CSS framework should nevertheless be able to support that wider
direction without a redesign.

The framework must fit Duets' existing asset delivery model. Runtime browser
assets are delivered through
`IAssetSource` ([ADR-18](18_pluggable-asset-source-abstraction.md)), the same
pattern used for Monaco and the TypeScript compiler. The selected framework
therefore needs ordinary static CSS, JavaScript, and font assets that can be
retrieved from a public CDN by default and replaced by an embedder-supplied asset
source for offline operation.

First-class DuetsPad primitives should encapsulate framework class choices.
Lower-level escape hatches such as `ui.element` and `ui.rawHtml` may still allow
callers to provide classes or raw markup explicitly.

## Decision Drivers

- Pre-built admin-application components covering the wider DuetsPad visual vocabulary
- MIT or compatible license across the dependency chain
- Distributable as plain static files retrievable via `IAssetSource`
- Built-in dark mode
- Bootstrap-compatible class taxonomy, so most wrapping work is class application rather than custom CSS design
- Ability to use component primitives without adopting a framework-provided page layout

## Considered Alternatives

### A: Self-built minimal CSS

- Pro: smallest possible payload
- Pro: no external dependency
- Con: every component and accessibility behavior must be implemented from scratch
- Con: visual consistency across the catalog is on Duets to maintain

### B: Bootstrap 5.3 alone

- Pro: well-known
- Pro: MIT license
- Pro: dark mode via `data-bs-theme`
- Con: admin-application components such as polished data tables, empty states,
  and dashboard-oriented sections are absent
- Con: significant custom CSS is required to reach the target vocabulary

### C: CoreUI Free 5.x

- Pro: admin-focused
- Pro: Bootstrap-based
- Pro: MIT license
- Con: class-name and dependency history is less stable across major versions
- Con: documentation and community remain split across versions

### D: Shoelace

- Pro: Web Components and Shadow DOM isolation are attractive for mixed embedded content
- Pro: framework-agnostic
- Con: admin layout and dashboard vocabulary are weaker
- Con: per-component import patterns do not match Duets' current static asset delivery model as directly

### E: AdminLTE 4

- Pro: established admin template lineage
- Con: visual identity is rooted in earlier Bootstrap admin-template conventions
- Con: v4 migration maturity is weaker than choosing a current Bootstrap-based component set

### F: Bulma, Pico CSS, or UIkit

- Pro: each can be lighter than Bootstrap-derived admin frameworks
- Con: each lacks some combination of admin components, native dark mode,
  Bootstrap compatibility, or active maintenance suited to DuetsPad

### G: Tabler 1.x

- Pro: Bootstrap 5.3 base; existing Bootstrap class knowledge transfers
- Pro: admin-application component set out of the box
- Pro: MIT throughout
- Pro: distributed as ordinary static assets compatible with `IAssetSource`
- Pro: dark mode via `data-bs-theme`
- Con: total payload is larger than bare Bootstrap
- Con: ships page-layout templates that the four-surface DuetsPad shell does not use

## Decision

Choose **Alternative G**.

DuetsPad adopts Tabler 1.x as its CSS framework and icon vocabulary. Assets are
retrieved through `IAssetSource` by default from a public CDN or equivalent
source and may be replaced by embedders through the existing asset-source
mechanism.

DuetsPad serves these Tabler assets by default, at the pinned versions listed:

- `tabler.min.css` from `@tabler/core` **1.4.0**
- `tabler-icons.min.css` and `tabler-icons.woff2` from `@tabler/icons-webfont` **3.44.0**

The Monaco loader (`monaco-editor` **0.55.1**, `min/vs/loader.js`) is served
alongside these as part of the DuetsPad asset set, through the same
`IAssetSource` delivery path.

`tabler.min.js` may be served when DuetsPad uses Tabler JavaScript components,
but the DuetsPad shell should not depend on Tabler JavaScript unless a component
requires it.

Asset acquisition, caching, and serving — including the default Unpkg CDN
sources for the assets above — are realized by **`AssetProvider`**. This
component owns the `Lazy`-wrapped asset caches, constructs the `Unpkg` +
`WithDiskCache` source chains, and handles the asset HTTP routes. `DuetsPadService`
maps routes to `AssetProvider`; the provider has no shared mutable state with
the service.

`AssetProvider` applies a **Tabler Icons CSS rewrite** before the icons CSS
is served: `RewriteTablerIconsCss` replaces every `@font-face` `src:` declaration
in the upstream `tabler-icons.min.css` with a single entry pointing at the
locally served `tabler-icons.woff2`, dropping the upstream woff/ttf fallback
`src` entries that have no route in DuetsPad. This rewrite is why
`tabler-icons.woff2` is the only icon font asset served: the fallback entries
in the upstream stylesheet are stripped so the browser never requests them.

Tabler's page-layout primitives such as page wrappers, sidebars, and navbars are
not adopted as DuetsPad's layout model. The DuetsPad shell is built directly
around its Editor, Canvas, Timeline, and Immediate surfaces. DuetsPad consumes
Tabler's component classes and visual vocabulary rather than its dashboard
template structure.

Dark mode follows Bootstrap 5.3's `data-bs-theme` mechanism. First-class
DuetsPad primitives should wrap Tabler classes internally. Escape hatches such
as `ui.element` and `ui.rawHtml` remain explicit ways to leave that wrapper
layer.

## Rationale

Tabler is the strongest fit among the options that satisfy the static-file,
licensing, admin-component, and dark-mode constraints. It keeps the Bootstrap
class taxonomy while providing a more complete admin-application vocabulary than
Bootstrap alone.

CoreUI has similar goals but a less attractive versioning and documentation
story. Shoelace's Web Component model is structurally appealing, especially for
isolation, but its component coverage and asset-loading style are a weaker match
for this project. Self-built CSS would minimize payload but would make Duets
responsible for component design and accessibility behavior that a mature
framework already provides.

Using `IAssetSource` keeps Tabler consistent with the existing Duets browser
asset model. The framework decision does not introduce a new package manager or
frontend build requirement for host applications.

Tabler Icons are consumed through the webfont package because that matches
Tabler's class-based component styling. This accepts a larger first-load asset
than a smaller curated icon subset, but keeps icon use simple and cacheable.

## Consequences

- **Positive**: DuetsPad gets a rich admin-application visual vocabulary without a custom design system
- **Positive**: dark mode can use the Bootstrap 5.3 `data-bs-theme` mechanism
- **Positive**: first-class primitives can mostly be class wrappers instead of custom CSS components
- **Positive**: distribution remains consistent with the existing `IAssetSource` pattern
- **Positive**: `AssetProvider`'s CSS rewrite eliminates dead font-format routes by stripping
  woff/ttf fallback `src` entries from the upstream icons CSS; the browser requests only `woff2`
- **Negative / trade-offs**: Tabler adds more payload than bare Bootstrap or self-built CSS
- **Negative / trade-offs**: Tabler CSS is global, which complicates any later Shadow DOM isolation strategy
- **Negative / trade-offs**: raw HTML inserted by callers may inherit Tabler styling
- **Negative / trade-offs**: replacing Tabler with a non-Bootstrap framework would require revisiting the wrapping layer
- **Negative / trade-offs**: the CSS rewrite is a preprocessing step applied at serve time; it must be
  updated if the upstream `@font-face` block structure changes in a future Tabler Icons release
