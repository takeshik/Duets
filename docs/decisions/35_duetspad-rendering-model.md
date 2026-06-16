# ADR-35: DuetsPad Rendering Model — Render Nodes, Object Renderers, Render Context, and Dump Ownership

## Status

Accepted

## Context

ADR-32 defines DuetsPad as a browser debug pad that can display structured
output. ADR-34 gives each DuetsPad browser session a server-side owner for
Canvas and Timeline state. Those decisions require a server-side representation
for output before it is serialized and projected into the browser.

The old `ReplService` output model was string-oriented. That is insufficient
for LINQPad-like output because values may need to render as structured text,
tables, labels, stacks, or other display-only UI fragments. At the same time,
DuetsPad must avoid making raw HTML the normal representation: raw HTML is an
escape hatch, not the structured contract.

Duets also needs a way to render existing CLR objects without forcing every
object to implement a DuetsPad interface. This is the same kind of extension
problem as `IComparable` / `IComparer`: some values can carry their own
render-node representation, while other values need an external renderer.

Once a renderer recurses into members and collection items, two further concerns
surface:

- Rendering must be bounded — in nesting depth and in the number of collection
  items materialized — and those bounds should be configurable by the caller of
  `dump`, not hardcoded. (The default renderer had grown a hardcoded depth cap.)
- The per-recursion state that enforces these bounds (current depth and the
  cycle-detection set) must be threaded through rendering. If a renderer delegates
  a nested value back to the pipeline, depth and cycle detection would otherwise
  reset at the boundary, so a depth limit could never bound a tree that crosses
  renderer boundaries, and cycle detection would fragment.

ADR-20 introduced `dump` as a core global function. Its entire framing is "the
REPL needs to output an intermediate value to the output pane" — a concept of the
pre-DuetsPad web UI. ADR-32 later superseded that web UI / `ReplService` with
DuetsPad. In core Duets, `dump` is `__consoleImpl__('log', inspect(value, opts))`
plus a pass-through return, with options `{ depth, compact }` driving a string
inspection; DuetsPad overrides `dump` at runtime to render structured output to
its Timeline, with the different options `{ maxDepth, maxItems }`. Two divergent
`dump` semantics thus shared one name, and their option shapes collided in the
shared TypeScript declaration. Core Duets is a UI-agnostic embeddable library
(ADR-3, ADR-22); its actual value surface is the evaluation result plus
`console` / `util.inspect`. A `dump` that "outputs to a pane" presumes a UI core
does not own.

## Decision Drivers

- Represent structured output before browser projection
- Keep raw HTML available but explicit
- Allow values to be rendered without modifying their CLR types
- Keep Canvas and Timeline state reduced to stable terminal nodes
- Preserve object identity and script usability for `dump(value)`
- Leave interactive handlers outside the display-only render-node contract
- Make render failures explicit instead of silently returning null
- Bound recursion cost (nesting depth and item count) with caller-configurable limits
- Keep depth limiting and cycle detection consistent across renderer boundaries
- Give the dump-options surface and the `dump` verb an unambiguous home
- Keep core Duets UI-agnostic (value surface = evaluation result + `console` / `util.inspect`)
- Do not fix presentation in an ADR — concrete presentation is deferred to implementation and tests

## Considered Alternatives

### A: Serialize arbitrary CLR objects directly

- Pro: fewer DuetsPad-specific types
- Pro: object dumps can start from reflection alone
- Con: browser projection becomes coupled to CLR implementation details
- Con: custom UI primitives and raw HTML escape hatches have no common shape
- Con: display semantics are hard to test without running the browser

### B: Use raw HTML as the common representation

- Pro: browser projection is simple
- Pro: callers can express anything HTML can express
- Con: structured inspection is lost
- Con: escaping and security boundaries become harder to reason about
- Con: DuetsPad primitives collapse into string generation

### C: Require all renderable values to implement one interface

- Pro: simple dispatch rule
- Pro: values can describe their own display shape
- Con: existing CLR types cannot participate without wrappers
- Con: host applications would need to modify domain types for debug output
- Con: default rendering and per-session customization remain underspecified

### D: Render nodes plus object renderers (chosen)

- Pro: DuetsPad has a stable structured output model
- Pro: existing CLR values can be rendered by external renderers
- Pro: values that already are render nodes can pass through directly
- Pro: the server can reduce output before storing it in Canvas or Timeline
- Con: introduces DuetsPad-specific model types
- Con: renderers need ordering and failure rules

### Threading — A: pass static options only, `Render(value, options)`

- Pro: smallest signature change; carries the limits.
- Con: options carry the limit, not the position. A renderer cannot know its
  current depth, and a nested render through the pipeline restarts at depth 0, so
  `MaxDepth` never bounds a tree that crosses a renderer boundary and the
  cycle-detection set fragments.

### Threading — B: thread a `RenderContext` (options + depth + recursion entry point) (chosen)

- Pro: depth and cycle detection stay consistent across renderer boundaries;
  renderers recurse through a single shared entry point.
- Pro: separates static configuration (limits) from dynamic recursion state
  (position), mirroring a graph walker's depth-limit vs current-depth distinction.
- Con: `IObjectRenderer.Render` changes signature — a breaking change to a public
  extension point.

### `dump` ownership — C: keep `dump` in core (status quo)

- Pro: no breaking change.
- Con: two divergent `dump` semantics share a name; the `{ depth, compact }` vs
  `{ maxDepth, maxItems }` option shapes collide in one declaration; core carries
  an output-pane concept it does not own.

### `dump` ownership — D: make `dump` DuetsPad-only (chosen)

- Pro: dissolves the name/option conflict (only DuetsPad declares `dump`); gives
  `DumpOptions` a clear home in `Duets.Pad`; keeps core UI-agnostic.
- Con: breaking change to core; `samples/inspect-and-dump.cs` and the core `dump`
  declaration must change; revises ADR-20.

## Decision

Choose **Alternative D** for the render-node model, **B** for context threading,
and **D** for `dump` ownership.

### Render-node model

DuetsPad output is represented by render nodes. A render node may be reducible
or terminal:

- `IRenderNode`: a node in the DuetsPad rendering model
- `ITerminalRenderNode`: a render node at the reduction boundary
- `Text`: terminal text content
- `Element`: terminal structured HTML-like element
- `RawHtml`: terminal raw-HTML escape hatch

Canvas and Timeline store terminal render nodes. Non-terminal nodes are reduced
before they enter Canvas or Timeline state. If a node cannot reduce further,
`Reduce()` returns the node itself.

`Element` is HTML-oriented. DuetsPad may restrict unsafe element names, but it
must not require all UI expression to go through narrowly predefined component
types. `RawHtml` remains available for content that cannot reasonably be
represented as structured nodes, but it is not the primary model.

### Render result

Rendering produces `DisplayContent`: a terminal `Body` (`ITerminalRenderNode`,
public) plus an internal set of pending interactions. `DisplayContent` is the
result type of `IObjectRenderer.Render` and of the `ui.*` and `dump` surfaces.
The display-only render-node model is preserved — interactions are carried beside
the body, never inside the node tree. The interaction lifecycle (committed
interactions, handler ids, invocation, and lifetime rules) is specified by the
interaction model ADR.

### Object renderers

Object rendering is handled by `IObjectRenderer`:

- `CanRender(value)` is an applicability check and may inspect object state
- `Render(object value, RenderContext context)` returns `DisplayContent`
- `Render` may throw after `CanRender` returned true
- renderers signal failure with exceptions, not null
- renderer registration is session-scoped
- if multiple registered renderers can render a value, the last registered
  renderer wins
- a default renderer exists so ordinary values can always produce output

### Render context

`RenderContext` carries the per-recursion state:

- `Options`: a `DumpOptions` value
- `Depth`: current nesting depth
- `RenderChild(value)`: renders a nested value at `Depth + 1`, reusing the same
  cycle-detection set and options

Depth limiting and cycle detection are centralized in the dispatch step — applied
at the root and inside `RenderChild` — not inside individual renderers. Every
renderer, including session-registered ones, inherits the bounds and cycle safety;
renderers recurse only through `RenderChild`.

### Render dispatch order

The pipeline entry is `ObjectRenderingPipeline.Render(object? value, DumpOptions? options = null)`,
returning `DisplayContent`. `DisplayRenderer` performs dispatch. The session
funnels all rendering through a single `TryRenderContent` entry point. The
dispatch order is:

1. null / `DBNull`
2. depth limit
3. values already being `DisplayContent` (passthrough)
4. values already implementing `IRenderNode`
5. cycle detection (reference types on the active recursion path)
6. session-registered `IObjectRenderer` instances, in last-wins order
7. the default object renderer

The per-call options merge is done by `DumpOptionsResolver.Merge`.

### Dump options

`DumpOptions` is a public type in `Duets.Pad.Rendering` with:

- `MaxDepth` (default **5**)
- `MaxItems` (default **1000**)

The session/service holds the default. `dump(value, options?)` accepts a per-call
override merged over that default. Other render entry points (Canvas, `ui.*`) use
the session default and do not take an options argument.

In TypeScript: `opts?: { maxDepth?: number; maxItems?: number }`.

The concrete presentation of bounded output (type-name header, item count,
truncation indicator, `ToString()` summary row, and table/grid/list shape) is
left to implementation and tests, not fixed here.

### `dump` ownership

`dump` is DuetsPad-only. It is removed from core (`ScriptEngineInit.js` /
`.d.ts`); core retains `console` and `util.inspect`, and its value surface is the
evaluation result. DuetsPad defines `dump` and registers its declaration with
`opts?: { maxDepth?: number; maxItems?: number }`. The sample
`samples/inspect-and-dump.cs` is updated accordingly.

This partially supersedes ADR-20: the decision to expose `dump` as a generic
global function `dump<T>(value): T` (rather than an `Object.prototype`
extension), and its completion/return-type rationale, remain in force. The
ownership and options are superseded: `dump` moves from core to DuetsPad-only,
and its options change from the console-inspect `{ depth, compact }` to the
render `{ maxDepth, maxItems }`.

Interactive event handlers are outside the render-node model. This ADR defines a
display-only render-node model. The interaction model ADR specifies how handlers
attach to rendered output.

## Rationale

Render nodes give DuetsPad a model that can be stored, compared, serialized,
tested, and projected without asking the browser to interpret arbitrary CLR
objects. This matches the server-owned session boundary in ADR-34.

Separating `IRenderNode` from `IObjectRenderer` keeps the model open. Values
that naturally are DuetsPad output can implement the node contract directly,
while ordinary CLR values can be handled by external renderers. Session-scoped
renderer registration lets a host application customize debug output without
changing global Duets behavior.

Using terminal nodes as the Canvas and Timeline boundary keeps stored output
stable. Reducible nodes are useful for higher-level primitives such as stacks
or tables, but the authoritative state should contain the reduced display
shape.

Raw HTML remains necessary as an escape hatch, but treating it as one terminal
node among structured nodes keeps it visible and reviewable. Most DuetsPad
output should be expressed through `Element` and other structured nodes.

Carrying limits in `DumpOptions` while tracking position in `RenderContext`
separates static configuration from dynamic recursion state. Centralizing depth
limiting and cycle detection in the dispatch step — rather than in each renderer —
is what keeps the bounds holding across renderer boundaries: a renderer that
delegates a child through `RenderChild` cannot escape the depth budget or the
cycle set. The signature change is the cost of making the context available to
every renderer uniformly; a static-options parameter (alternative Threading-A)
cannot convey position and so cannot bound cross-renderer trees.

`dump` is a UI-output verb. With DuetsPad established as the branded debug pad
(ADR-32), `dump` belongs to DuetsPad; its presence in core was an artifact of the
era when Duets-the-library and the web UI were not separated. Removing it leaves
core with a coherent, UI-agnostic value surface (evaluation result + `console`),
and lets the single remaining `dump` declaration advertise the options that
actually apply.

## Consequences

- **Positive**: Canvas and Timeline can store structured output rather than
  strings or arbitrary CLR objects
- **Positive**: host applications can customize rendering without modifying
  domain types
- **Positive**: `dump(value)` can return the original script value while C# gets
  a rendered representation
- **Positive**: browser projection can handle a small stable set of terminal
  node kinds
- **Positive**: rendering recursion is bounded, and the bounds are caller-configurable per `dump` call without changing renderer code
- **Positive**: depth limiting and cycle detection are uniform across all renderers because they live in the dispatch step, not in each renderer
- **Positive**: a single owner for `dump` and its options; the TypeScript declaration conflict is dissolved
- **Positive**: core Duets stays UI-agnostic
- **Negative / trade-offs**: object rendering introduces a dispatch pipeline
  that must be tested independently
- **Negative / trade-offs**: default object rendering can become complex and
  must not be treated as complete LINQPad parity
- **Negative / trade-offs**: future interaction support may require extending
  the node model
- **Negative / trade-offs**: `IObjectRenderer.Render` takes a `RenderContext`, so the contract is not a bare `value -> node` function, and `RenderContext` must be public; existing custom renderers must update their signature and recurse via `RenderChild`
- **Negative / trade-offs**: `dump` is removed from core — a breaking change for embedders that relied on the core global; `samples/inspect-and-dump.cs` is updated and ADR-20 is revised
