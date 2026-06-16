# ADR-43: DuetsPad Named Multi-Canvas

## Status

Accepted

## Context

ADR-32 gives DuetsPad a single Canvas surface for persistent structured display
state. ADR-35 stores Canvas content as reduced render nodes, ADR-34 makes the
server-side `DuetsPadSession` own that Canvas, and ADR-36 projects it to the
browser over the multiplexed per-session SSE stream with the `canvas.snapshot`
(initial) and `canvas.replace` (post-mutation) events. The Canvas script surface
is the `canvas` global with `add` / `set` / `clear`. ADR-41 attaches interaction
handlers to rendered Canvas output and releases them when that output is
replaced.

A single Canvas forces every persistent display into one tree. Distinct
concerns — say a live data table and a separate status panel — must share one
surface or fight over `canvas.set`. The goal is to let one session hold several
independent named canvases while keeping the existing single-canvas experience
unchanged for scripts and users that never ask for a second one.

Introducing named canvases settles a set of axes:

1. **Script API placement** — what global exposes the named set, and where it
   sits in the script global namespace.
2. **Default-canvas relationship** — how the existing `canvas` global relates to
   the named set.
3. **Access semantics** — how a script obtains a canvas by name and whether
   canvases have an explicit lifecycle.
4. **Server state and interaction scoping** — how `DuetsPadSession` keys canvas
   state, and how the per-session interaction store (ADR-41) scopes handlers.
5. **SSE addressing** — how the ADR-36 canvas events identify which canvas they
   carry once there is more than one per session.
6. **Browser presentation** — how the Canvas pane shows multiple canvases without
   degrading the single-canvas view.

## Decision Drivers

- Preserve the existing single-canvas script API and UX as the zero-cost default
- Keep the canvas-mutation surface (`add` / `set` / `clear`) identical per canvas
- Stay consistent with the existing flat script-global convention
  (`canvas`, `ui`, `pad`, `dump`) and avoid overloading the `pad` host-command
  surface (ADR-42) with display state
- Extend, not rewrite, the ADR-36 canvas event protocol — no new streams, no
  open-ended event-type strings
- Keep Canvas state server-canonical (ADR-36) and interaction handlers correctly
  scoped per canvas (ADR-41)
- Give multi-canvas sessions a coherent place in the workspace UI while leaving
  the single-default Canvas appearance untouched

## Considered Alternatives

The server-state-and-interaction-scoping axis is settled mechanically (state and
interactions are keyed by canvas name) and has no competing alternative, so it is
omitted below.

### Script API placement

#### A: `pad.canvases` nested under the host-command surface

- Pro: adds no new top-level global
- Con: `pad` is the host-command surface (ADR-42: `resetSession` / `openText` /
  `setEditorText`); a canvas collection is display state, not a host command, so
  this overloads `pad` with a second unrelated responsibility
- Con: the default `canvas` global lives at the top level while the named set
  lives under `pad`, splitting one concept across two locations

#### B: `canvases` top-level global

- Pro: matches the existing flat convention — `canvas` and `canvases` sit
  side by side at the same level, one concept in one location
- Con: one more symbol in the top-level script global namespace

### Default-canvas relationship

#### A: Retain `canvas` as an alias for `canvases.get("default")`

- Pro: scripts that only use `canvas` behave exactly as before
- Pro: `"default"` is a stable, predictable API identity
- Con: the default canvas's display name is then a UI concern that must be solved
  separately from its canonical name

#### B: Rename the default canvas to a display-friendly name

- Pro: the canonical name could double as the tab label with no extra mapping
- Con: changes the API identity of the default canvas and breaks the existing
  `canvas` ≡ default-canvas equivalence and `canvases.get("default")`

### Access semantics

#### A: Lazy `canvases.get(name)` with getOrAdd

- Pro: one method reaches a canvas whether or not it already exists, matching how
  scripts think ("give me the one called X")
- Pro: no separate create step for the common single-use case
- Con: a typo silently creates a new canvas rather than erroring

#### B: Explicit `canvases.add(name)` / `canvases.remove(name)` lifecycle

- Pro: creation and removal are explicit, and a removal verb frees resources
- Con: heavier than the use case warrants; forces a create call before use and a
  removal decision the simple cases do not need

### SSE addressing

#### A: A `name` field on the existing `canvas.snapshot` / `canvas.replace` events

- Pro: keeps the two fixed event types and the single multiplexed per-session
  stream from ADR-36
- Pro: the browser routes by `name` after the existing `canvas.` prefix check
- Con: the payload change is additive only for clients that use a single
  `"default"` canvas; a multi-canvas client must inspect and route by `name`, and
  a client that ignores `name` cannot distinguish canvases

#### B: A distinct event-type string per canvas (e.g. `canvas.replace.myName`)

- Pro: the canvas identity is encoded in the routing key
- Con: event types become open-ended, breaking ADR-36's fixed namespaced-verb
  scheme and its prefix-based browser demultiplexing

#### C: A separate SSE stream per canvas

- Pro: each canvas's subscribe/replay hook is independent
- Con: reintroduces the per-connection cost ADR-36 avoided by multiplexing one
  stream per session, against the HTTP/1.1 per-origin connection cap

### Browser presentation

#### A: Tabbed Canvas pane (sub-tabs in split view, promoted to top-level tabs in tabbed view)

- Pro: one canvas is visible at a time, matching the existing single-pane layout;
  the tab strip disappears when only `"default"` exists
- Pro: reuses the workspace's existing tab vocabulary in tabbed view
- Con: adds tab UI and per-canvas panel tracking the Canvas pane did not have

#### B: All canvases stacked in one scrolling pane

- Pro: no tab UI; every canvas is visible at once
- Con: many or large canvases crowd the pane and lose the "one focused surface"
  feel; diverges from the single-pane model

## Decision

DuetsPad sessions own multiple named canvases.

**Script surface.** A new top-level `canvases` global exposes `get(name)` with
getOrAdd semantics: the first call for a name creates the canvas, later calls
return the same instance. The returned object has the same `add` / `set` /
`clear` surface as the existing `canvas` global (its declared TypeScript type is
`DuetsPadCanvas`). The `canvas` global is retained as a top-level alias for
`canvases.get("default")`. The `"default"` canvas always exists. Canvas names are
non-empty strings; there is no removal API.

**Server state and interaction scoping.** `DuetsPadSession` keys Canvas state by
name (the `"default"` canvas is always present), and the per-session interaction
store (ADR-41) keys canvas interactions by canvas name, so handlers are scoped to
and released with their own canvas. Canvas state remains server-canonical
(ADR-36).

**Protocol.** The `canvas.snapshot` and `canvas.replace` events (ADR-36) gain a
`name` field identifying the canvas. The two event-type strings and the single
multiplexed per-session stream are unchanged. On subscribe, the initial burst
emits one `canvas.snapshot` per existing canvas; the relative ordering across
event kinds is unchanged (all `canvas.snapshot` events still precede
`timeline.reset` and the `type.declaration` events), but the canvas block now
contains one event per canvas rather than exactly one. This amends the
canvas-event payload of ADR-36.

**Browser presentation.** The Canvas pane is tabbed. In a split view the named
canvases appear as sub-tabs inside the Canvas pane, hidden when only `"default"`
exists. In tabbed view mode the canvases are promoted to flat top-level tabs
alongside Editor and Timeline. The top-level canvas tab label reads `Canvas` when
only the default exists and `Canvas(name)` once there are several; the canonical
canvas name is unchanged by this labeling.

## Rationale

`canvases` as a top-level global keeps one concept in one location next to its
`canvas` default, consistent with the flat script-global convention; nesting it
under `pad` would misplace display state inside the host-command surface
(ADR-42). Retaining `canvas` as the `"default"` alias makes the feature
additive — existing scripts and the existing UX are untouched until a script asks
for a second canvas — which is why renaming the default canvas was rejected.
Lazy `get(name)` beats an explicit add/remove lifecycle because the dominant case
is a script that wants one canvas with the least ceremony; a removal verb can be
added later if churn ever justifies it.

Adding a `name` field rather than per-canvas event types or per-canvas streams is
the minimal extension of ADR-36: it preserves the fixed namespaced-verb scheme
and the single multiplexed stream, and the browser gains only a `name`-based
routing step after the existing prefix check. Keying interactions by canvas name
keeps ADR-41's lifecycle guarantees correct per canvas instead of letting
handlers leak across canvases.

The tabbed Canvas pane preserves the single-focused-surface model: one canvas is
shown at a time, and the tab strip is hidden entirely when only `"default"`
exists, so the single-canvas appearance is unchanged. Promoting canvases to
top-level tabs in tabbed view reuses the workspace's existing tab vocabulary so
each canvas is reachable at the same level as Editor and Timeline, rather than
hiding a second navigation layer inside a maximized pane.

## Consequences

- **Positive**: scripts can separate independent display state into named
  canvases without losing the simple single-canvas default
- **Positive**: the script API and UX are backward-compatible; nothing changes
  until a second canvas is requested
- **Positive**: interaction handlers are scoped to and released with their own
  canvas
- **Negative / trade-offs**: one more top-level script global (`canvases`)
- **Negative / trade-offs**: ADR-36 is amended — every protocol consumer, test,
  and the browser canvas router must read and preserve `name` on every
  `canvas.snapshot` / `canvas.replace` event, and initial-burst handlers must
  iterate over one snapshot per canvas instead of handling exactly one
- **Negative / trade-offs**: the browser tracks per-canvas panels and the Canvas
  pane carries tab UI it did not before
- **Negative / trade-offs**: canvases have no removal API and persist for the
  session lifetime; a session that creates many short-lived named canvases
  retains them until reset. Revisitable by a later ADR if a removal verb is
  needed.
