# ADR-36: DuetsPad Server-Canonical Output Protocol

## Status

Accepted

## Context

ADR-32 defines DuetsPad as the successor to `ReplService` and separates two
output surfaces:

- Canvas, for persistent structured display state
- Timeline, for structured history output

ADR-34 gives each DuetsPad browser session a server-side owner. ADR-35 defines
render nodes as the structured output representation stored by Canvas and
Timeline. Those surfaces now need a synchronization model between the
server-side DuetsPad session and the browser. The current browser REPL model
cannot be reused directly: it is an append-oriented string log, while DuetsPad
needs reloadable structured output and a persistent Canvas.

A session also pushes type-declaration updates to the browser so Monaco can
offer completions — a stream that predates DuetsPad. So a session has several
kinds of structured server-to-browser events: Canvas state, Timeline history,
and type declarations.

Two questions follow. First, how is server state synchronized to the browser.
Second, how many SSE connections carry it: one stream per surface, or a single
per-session stream multiplexing every event kind.

The protocol also needs stable event names. Bare verbs such as `snapshot`,
`append`, or `replace` are ambiguous once more than one event kind exists. The
same verb can mean different things for Canvas and Timeline state, and future
event kinds may add more operations.

## Decision Drivers

- Keep Canvas and Timeline state inspectable on the server
- Make browser reload and SSE reconnect converge on server state
- Avoid client-local state becoming authoritative
- Keep Canvas and Timeline semantics distinct
- Use event names that remain clear as the protocol grows
- Preserve room for Timeline entry update and quota trim without changing the
  event naming scheme later
- Avoid consuming several persistent connections per session: HttpListener is
  HTTP/1.1 (ADR-3), so the browser's per-origin connection cap (~6) is real, and
  one EventSource per surface multiplied by open tabs can starve other requests

## Considered Alternatives

The decision has two axes: the synchronization model and the number of streams.

### Synchronization model — A: Browser-local Canvas and Timeline state

- Pro: server implementation is smaller
- Pro: client-side rendering can mutate DOM directly
- Con: reload loses display state unless a separate persistence mechanism is added
- Con: server-side inspection and testing of structured output becomes weak
- Con: multiple browser connections have no authoritative state to converge on

### Synchronization model — B: Server snapshots only

- Pro: simplest event shape
- Pro: reconnect is straightforward because every event is a full state
- Con: append-oriented Timeline updates become unnecessarily expensive
- Con: entry-level replacement and quota trimming cannot be represented directly
- Con: Canvas and Timeline operations are still semantically different but hidden
  behind one broad event kind

### Synchronization model — C: Unnamespaced event verbs

- Pro: short event type strings
- Pro: mirrors common CRUD words
- Con: `snapshot`, `append`, `replace`, and similar verbs are ambiguous across
  event kinds
- Con: future event kinds would either reuse unclear verbs or introduce a second
  naming style
- Con: tests and browser code can accidentally handle the wrong event kind

### Synchronization model — D: Server-canonical state with namespaced operation events (chosen)

- Pro: the server remains the source of truth for Canvas and Timeline state
- Pro: initial subscription and reconnect can send a current server-state event
- Pro: event names describe both the surface and the operation
- Pro: Timeline can support append, entry update, and trim without changing the
  naming convention
- Con: the server must own session state and subscriber fan-out
- Con: the protocol has more event types than a single snapshot-only stream

### Stream count — one SSE stream per surface

- Pro: each surface's subscribe/replay hook is independent
- Con: several persistent EventSource connections per tab; under the HTTP/1.1
  per-origin connection cap, multiple tabs plus ordinary requests can starve
- Con: the relative ordering of each surface's initial state is undefined because
  the connections are independent

### Stream count — single per-session multiplexed stream (chosen)

- Pro: one connection per tab, so connection-count pressure disappears
- Pro: one ordered channel lets the initial state burst be delivered in a
  deterministic order
- Pro: "one session, one stream" makes reset reduce to "new session, new stream",
  so events buffered for an abandoned session cannot reach the new one
- Con: a burst on one surface can head-of-line-block another; acceptable at the
  single-developer scale DuetsPad targets, and the escalation path is HTTP/2 or
  WebSocket, not re-splitting the stream

## Decision

Choose **Alternative D** for the synchronization model and a **single per-session
multiplexed stream** for transport.

DuetsPad Canvas and Timeline state are server-canonical. The browser is a
projection of server state and must not be treated as the authoritative owner of
Canvas or Timeline contents.

All server-to-browser events for a session travel on one SSE stream,
`GET /sessions/{sessionId}/events`, backed by the shared **`SseTransport`**
primitive (`SseTransport.RunAsync`), which owns the mechanical parts of an SSE
response: setting response headers, managing an unbounded channel, running a
keepalive timer, and tearing down on client disconnect. The session supplies one
subscribe/replay hook and one unsubscribe teardown. Event kinds are distinguished
by namespaced event types, not by separate connections.

The event type strings are namespaced by surface and operation:

- `canvas.snapshot`: current Canvas state sent when a subscriber attaches
- `canvas.replace`: current Canvas state after a Canvas mutation
- `timeline.reset`: current Timeline state sent when a subscriber attaches or
  when the Timeline is reset as a whole
- `timeline.append`: one new Timeline entry
- `timeline.update`: replacement for one existing Timeline entry
- `timeline.trim`: removal of entries before a boundary, optionally with a
  retained marker entry
- `type.declaration`: one type-declaration update for Monaco completions
- `control.*`: imperative commands from the server to the browser, defined by the
  interaction-with-the-pad decision (ADR-42); these are commands, not state
  projection, and are the one exception to the "browser is a projection" framing

When a subscriber attaches, the server delivers each surface's current state as an
initial burst, ordered under the session state lock: `canvas.snapshot`, then
`timeline.reset`, then the `type.declaration` events. A single multiplexed channel
makes this order deterministic, which independent per-surface connections could
not guarantee.

Canvas uses full-state replacement for both initial delivery and mutation
delivery. Timeline distinguishes full reset, append, entry update, and trim
because Timeline is ordered history and may grow over time.

The event type strings are part of the DuetsPad protocol. Server code, browser
code, and tests must use the namespaced strings above rather than bare verbs.

This ADR does not define the full JSON schema for render nodes or Timeline
entries. Those schemas are part of the rendering and Timeline contracts built on
top of this protocol decision.

## Rationale

Server-canonical state matches DuetsPad's role as a debug dashboard surface. A
host process should be able to inspect Canvas and Timeline state without asking
the browser, and a browser reload should converge on the current session state.

Canvas and Timeline have different lifecycles. Canvas represents current
display state, so full replacement is acceptable as a protocol operation.
Timeline represents structured history, so append and entry-level operations
are meaningful protocol concepts. Treating both surfaces as one generic
`replace` or `snapshot` stream would hide that difference.

Namespaced event types make the protocol readable and reduce accidental
cross-kind coupling. `timeline.reset` and `canvas.snapshot` are deliberately
different even though both can carry current state: Timeline reset is a history
operation, while Canvas snapshot is an initial projection of display state.

One stream per session, rather than one per surface, is what makes the namespace
carry its full weight: because logical separation already lives in the event type,
splitting it across connections adds cost (a persistent connection per surface,
against the HTTP/1.1 per-origin cap) without adding clarity. Multiplexing also
makes the initial burst orderable and makes session identity and stream identity
coincide, so a reset that issues a new session id is automatically a new stream
that stale events cannot cross into.

Keeping `timeline.update` and `timeline.trim` in the protocol is intentional
even if the initial UI mostly emits `timeline.append`. They express stable
Timeline operations that are expected once output entries can be revised or
quota trimming is enabled. A quota policy (decided separately) realizes
`timeline.trim`; `timeline.update` is defined in the protocol but is not
currently emitted by the server.

## Consequences

- **Positive**: browser reload and SSE reconnect can restore Canvas and Timeline
  from server state
- **Positive**: tests can assert protocol events without depending on DOM state
- **Positive**: Canvas and Timeline semantics remain distinct in event names
- **Positive**: Timeline quota trimming and entry replacement have stable verbs
- **Positive**: one connection per session avoids the HTTP/1.1 connection-count
  pressure of several persistent EventSource connections per tab
- **Positive**: the initial state burst has a deterministic order, and session
  identity coincides with stream identity
- **Negative / trade-offs**: DuetsPad sessions must retain server-side output
  state
- **Negative / trade-offs**: browser code must demultiplex several event types on
  one stream
- **Negative / trade-offs**: a large burst on one surface can head-of-line-block
  another on the shared stream; revisiting this means moving to HTTP/2 or
  WebSocket rather than re-splitting the stream
- **Negative / trade-offs**: full Canvas replacement may become inefficient for
  large Canvas trees and can be revisited by a later protocol ADR
