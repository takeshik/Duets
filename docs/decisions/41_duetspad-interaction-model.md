# ADR-41: DuetsPad Interaction Model

## Status

Accepted

## Context

ADR-35 defines the DuetsPad rendering model: render nodes, object renderers,
`RenderContext`, `DisplayContent` as the render result, and the `dump` ownership
decision. ADR-35 deliberately defined a display-only render-node model and deferred
interactive handlers: "A later interaction decision may add new node types or
additional contracts." ADR-32 noted that buttons and form controls fit DuetsPad's
direction but left the mechanism and handler lifecycle undecided. This ADR decides
them.

ADR-35 establishes `DisplayContent` (a terminal body plus pending interactions) as
the result type of `IObjectRenderer.Render`. That contract — interactions carried
beside the display-only node tree, never inside it — is the foundation this ADR
builds on. ADR-34 establishes per-session ownership of Canvas and Timeline state;
this ADR extends that ownership to the per-session interaction store.

DuetsPad is server-canonical (ADR-36): Canvas and Timeline state lives on the
server and is projected to the browser over SSE; the browser is a view. An
interaction handler is server-side .NET code (an `Action`) closed over script and
session state. The browser cannot hold the handler — it can only ask the server to
run one. The model must therefore (a) attach a handler to a specific rendered node,
(b) give the browser a stable token to trigger it, and (c) tie the handler's
lifetime to the visibility of the output that owns it.

The rendering model gives `IObjectRenderer.Render` the signature
`Render(object value, RenderContext context)` returning `DisplayContent`, but a
node tree alone cannot express "clicking this element runs that delegate" without
either polluting the node model or smuggling state through it. This ADR specifies
how the pending interactions inside `DisplayContent` are committed, addressed, and
invoked.

## Decision Drivers

- Attach server-side handlers to rendered nodes without putting delegates into the
  display-only node tree
- Give the browser a stable, opaque token to trigger a handler — no client-side
  handler code
- Tie handler lifetime to the visibility of the owning output (Canvas replacement,
  Timeline trim, session dispose)
- Preserve ADR-35's display-only render-node contract and ADR-36's server-canonical
  projection
- Stay safe under the session's existing lock without introducing a second lock

## Considered Alternatives

### Handler transport — A: emit client-side JavaScript callbacks

- Pro: no server round-trip per click.
- Con: handlers are .NET delegates over session/script state; they cannot run in the
  browser. This would force a parallel client-side execution model.

### Handler transport — B: a bidirectional WebSocket channel

- Pro: symmetric messaging.
- Con: the rest of DuetsPad is one-way SSE out plus plain POST in (ADR-36); a socket
  adds a second transport and a connection-state model for a request/response need.

### Handler transport — C: address server-side handlers by id and POST to invoke (chosen)

- Pro: reuses the existing SSE-out / POST-in shape; the browser holds only an opaque
  id.
- Con: a network round-trip per interaction; the server must track id-to-handler
  lifetime.

### Render result — D: add an interaction slot to `IRenderNode`

- Pro: one type.
- Con: pollutes the display-only node model (ADR-35) with handler identity; every
  node kind carries an otherwise-unused slot, and reduction/serialization must ignore
  it.

### Render result — E: return body and interactions together as `DisplayContent` (chosen)

- Pro: keeps the terminal node tree display-only; interactions ride beside it; the
  node model stays reducible and serializable unchanged.
- Con: `IObjectRenderer.Render` returns `DisplayContent` rather than a bare node,
  as now specified in ADR-35.

## Decision

Choose **C** for handler transport and **E** for the render result.

**Render result.** Rendering produces `DisplayContent`: a terminal `Body`
(`ITerminalRenderNode`, public) plus an internal set of pending interactions.
`DisplayContent` is the result type of `IObjectRenderer.Render` and of the `ui.*`
and `dump` surfaces. The display-only render-node model of ADR-35 is unchanged —
interactions are carried beside the body, never inside the node tree — so this
preserves rather than breaks ADR-35's contract.

**Addressing.** A handler is bound to a node position by a `DisplayPath` — a
sequence of non-negative child indices from the content root. As content is
composed, each nesting level prepends its index, so a handler authored at a child's
root resolves to the correct node in the final terminal tree. `ui.button(label,
handler)` yields a `DisplayContent` whose body is a `<button>` carrying one pending
`Click` interaction at the root; a disabled button carries none.

**Two-stage lifecycle.** A pending interaction (render-time: target path, event, and
the `Action` handler) becomes a committed interaction (target path, event, an
assigned handler id, and a `Live` / `Stale` state) only when the rendered content is
placed into Canvas or Timeline state. Handler ids are assigned at commit, not at
render, so re-rendering a value allocates no ids until it is actually displayed.

**Ownership.** Each `DuetsPadSession` owns one interaction store (extending the
ownership list of ADR-34). The store holds the id-to-handler registry and the
committed interactions keyed by surface: a single current set for the Canvas, and a
map keyed by Timeline entry id. The store has no internal lock; the session performs
every store mutation, and every lookup that must be atomic with state, under its
existing `_stateLock`, so interactions inherit the same ordering guarantees as
Canvas and Timeline state.

**Lifetime.** Handler lifetime follows the visibility of the owning output:

- Replacing the Canvas releases the previous Canvas handlers; clearing it releases
  all.
- Trimming the Timeline (ADR-39) releases the handlers of the dropped entries — the
  trim reports the removed entry ids and the store discards exactly those.
- Disposing the session releases all handlers.

**Invocation.** The browser triggers a handler with
`POST /sessions/{sessionId}/interactions/{handlerId}/invoke`. The response is
`{ ok, error, stale }`: `ok` on success; `stale: true` when the handler id is no
longer registered (its owning output was replaced, trimmed, or the session moved
on), so the browser can reconcile a click against output the server has already
retired. Invocation counts as session activity (ADR-38). Committed interactions are
projected with their `Live` / `Stale` state over the same SSE streams as their
surface (ADR-36), so the browser can present retired interactions distinctly.

## Rationale

Separating `DisplayContent` (body plus interactions) from the node tree is what lets
ADR-35's display-only contract stand: the terminal nodes that Canvas and Timeline
store stay pure, reducible, serializable display, while the handler binding lives in
a sidecar the browser never sees as markup. A handler slot on every node
(alternative D) would have spread mutable handler identity through a model whose
value is precisely that it is inert.

Addressing handlers by id over POST keeps the transport story singular — canonical
state out via SSE, commands in via plain POST — and keeps the browser ignorant of
handler code, which it must be because that code is .NET closures. The `stale`
answer exists because a server-canonical view is eventually consistent: a click can
race a trim or a re-render, and the honest reply is "that handler is gone," not a
generic error or a silent success.

Binding handler lifetime to output visibility, and routing every store mutation
through the session's one lock, means interactions cannot outlive what they belong
to and cannot observe Canvas or Timeline state torn mid-update. Reusing `_stateLock`
rather than adding a store-level lock keeps the single-writer ordering ADR-36 relies
on.

## Consequences

- **Positive**: ADR-35's render-node model stays display-only and serializable;
  interaction state does not leak into stored output.
- **Positive**: the browser holds only opaque handler ids and one POST verb — no
  client-side handler execution and no second transport.
- **Positive**: handlers are reclaimed deterministically with the output that owns
  them; there is no separate interaction garbage collection.
- **Positive**: `stale` makes click-versus-retire races explicit and recoverable.
- **Negative / trade-offs**: `IObjectRenderer.Render` returns `DisplayContent`
  (as specified by ADR-35); custom renderers build results as `DisplayContent`.
- **Negative / trade-offs**: each interaction is a server round-trip, so rapid
  interactions are bounded by request latency.
- **Negative / trade-offs**: correctness depends on the session holding `_stateLock`
  around store access — a discipline the store cannot enforce itself.
