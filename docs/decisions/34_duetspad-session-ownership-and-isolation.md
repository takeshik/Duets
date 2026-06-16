# ADR-34: DuetsPad Session Ownership and Isolation

## Status

Accepted

## Context

ADR-25 makes `DuetsSession` the canonical scripting session. ADR-32 introduces
DuetsPad as a browser debug pad with Editor, Canvas, Timeline, and Immediate
surfaces.

Those decisions create a new ownership question. A DuetsPad browser session
needs a script engine, type declarations, `dump` binding, `ui` and `canvas`
globals, Canvas state, Timeline state, object renderer state, and SSE
subscriber ownership. These responsibilities are related to a scripting session
but are not the same thing as `DuetsSession` itself.

Extending `DuetsSession` directly would make DuetsPad concerns part of the core
scripting abstraction. Sharing one scripting session across multiple browser
sessions would also make output state and script globals interfere with each
other.

## Decision Drivers

- Preserve `DuetsSession` as the core scripting abstraction
- Keep DuetsPad state isolated per browser session
- Allow more than one DuetsPad browser session per service process
- Make Canvas and Timeline ownership explicit
- Keep SSE subscriber management close to the state being streamed
- Avoid implicit session creation through unknown session identifiers
- Preserve a path for explicit reset and idle-timeout cleanup

## Considered Alternatives

### A: Add DuetsPad state directly to `DuetsSession`

- Pro: fewer wrapper types
- Pro: scripting and output state are always reachable from one object
- Con: core scripting sessions gain browser UI responsibilities
- Con: non-DuetsPad uses pay for DuetsPad concepts
- Con: future DuetsPad lifecycle rules would pressure the core session API

### B: Use one shared `DuetsSession` for all DuetsPad browsers

- Pro: all browsers see the same script state
- Pro: initial service implementation is smaller
- Con: users can interfere with each other's script globals and output
- Con: session-specific renderer registration cannot be isolated
- Con: Canvas and Timeline ownership becomes ambiguous

### C: Let the browser own session identity and output state

- Pro: server lifecycle is simpler
- Pro: browser reload behavior can be implemented client-side
- Con: server-side Canvas and Timeline ownership is lost
- Con: stale or forged identifiers become hard to reason about
- Con: host-side inspection and cleanup are weaker

### D: Introduce `DuetsPadSession` wrapping one `DuetsSession`

- Pro: `DuetsSession` remains focused on scripting
- Pro: each DuetsPad browser session has isolated script and output state
- Pro: Canvas, Timeline, renderer registry, and SSE subscribers have one owner
- Pro: service-level session lookup can reject unknown identifiers
- Con: adds a DuetsPad-specific session type and lifecycle
- Con: callers must distinguish core scripting sessions from DuetsPad sessions

## Decision

Choose **Alternative D**.

`DuetsPadSession` is the server-side owner for one DuetsPad browser session. It
wraps exactly one `DuetsSession` and owns the DuetsPad-specific state attached
to that scripting session:

- Canvas state
- Timeline state
- object renderer registration
- DuetsPad script globals such as `canvas`, `ui`, and the DuetsPad `dump` sink
- Canvas and Timeline SSE subscribers
- the per-session interaction store

`SessionRegistry` owns the session table, creates server-issued opaque
session identifiers, routes HTTP and SSE lookups to the matching
`DuetsPadSession`, and performs disposal and idle reclamation.
`DuetsPadService` is the thin HTTP router that delegates session-table and
lifecycle operations to `SessionRegistry`, and delegates static-asset
handling to `AssetProvider`. A request for an unknown session identifier
must not implicitly create that specific session; it should either fail for
session-specific routes or create a fresh session through the explicit session
creation route.

Construction-time wiring of the `canvas`, `ui`, and `dump` globals, and the
registration of per-session `.d.ts` declarations into the underlying
`DuetsSession`, is performed by `SessionBootstrap`. The session still owns
the resulting runtime state; `SessionBootstrap` is a pure construction-time
helper that does not retain state after wiring completes.

The SSE streaming mechanism — response headers, channel creation, keepalive
timer, and the read-until-disconnect loop — is the shared `SseTransport`
primitive. The session owns the subscriber registry (Canvas, Timeline, and
type-declaration subscriber lists), but does not itself run the stream loop.

A browser stores its current DuetsPad session identifier in browser
`sessionStorage`. Reusing an existing live identifier is allowed. If the stored
identifier no longer maps to a live server session, the service creates a fresh
session through the normal session creation route and returns the new
identifier.

DuetsPad evaluation is serialized per `DuetsPadSession`. Eval-driven side
effects such as `dump`, `console.log`, `canvas.add`, `canvas.set`, and
`canvas.clear` belong to the same session state. SSE subscribers observe events
from only their matching session.

This ADR does not decide the final idle-timeout duration, explicit reset API,
or multiple-browser attachment policy beyond the isolation rule above. Those
lifecycle questions are deferred and decided separately.

## Rationale

`DuetsSession` is already the canonical scripting boundary. DuetsPad needs that
boundary but also needs browser-output state and streaming responsibilities.
Wrapping `DuetsSession` keeps the dependency direction clear: DuetsPad depends
on core scripting, while core scripting does not depend on DuetsPad.

Per-session isolation avoids surprising cross-browser interference. It also
gives object renderers, Canvas, Timeline, and SSE subscribers one owner, which
is required for server-canonical output protocols built on top of DuetsPad.

Server-issued session identifiers give the service control over lifecycle and
cleanup. Browser `sessionStorage` is enough to make reloads converge on the same
live session without making the browser authoritative.

## Consequences

- **Positive**: DuetsPad can run multiple isolated browser sessions in one
  service process
- **Positive**: `DuetsSession` remains usable without DuetsPad dependencies
- **Positive**: Canvas, Timeline, renderer registration, and SSE subscribers
  have one server-side owner
- **Positive**: stale or unknown session identifiers do not create ambiguous
  server state
- **Negative / trade-offs**: DuetsPad has its own lifecycle object to manage
- **Negative / trade-offs**: service implementations must route every request
  through session lookup
- **Negative / trade-offs**: explicit reset, idle timeout, and richer
  multi-browser attachment policies still need follow-up design
