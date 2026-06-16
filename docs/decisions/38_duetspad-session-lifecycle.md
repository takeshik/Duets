# ADR-38: DuetsPad Session Lifecycle — Explicit Disposal and Idle Reclamation

## Status

Accepted

## Context

ADR-34 makes `SessionRegistry` the owner of the DuetsPad session table and
`DuetsPadSession` the owner of one browser session's scripting and output state,
with `DuetsPadService` acting as the thin HTTP router that delegates to
`SessionRegistry`. This ADR resolves the explicit disposal, idle-timeout, and
multiple-browser attachment questions deferred by ADR-34. ADR-34 also established
that unknown session identifiers must not implicitly create the named session.

Without a disposal path, sessions accumulate for the lifetime of the service
process. A browser tab that is closed, reloaded into a new session, or
abandoned leaves its server-side `DuetsPadSession` — and the `DuetsSession` it
wraps — alive indefinitely. The service needs both an explicit way to discard a
session and an automatic way to reclaim sessions that are no longer in use.

At the same time, DuetsPad streams Canvas, Timeline, and type-declaration state
to the browser over long-lived SSE connections that periodically emit keepalive
comments. Any inactivity policy must not fight against those live connections,
and a transient network drop must not silently destroy server-side state.

## Decision Drivers

- Give the service an explicit, predictable way to discard a session
- Reclaim sessions that are genuinely unused, bounding resource growth
- Keep the unknown-session protocol uniform across all session-specific routes
- Avoid destroying server-side state on a mere browser disconnect
- Do not let a live, keepalive-maintained stream be mistaken for an idle session
- Defer richer multi-browser semantics rather than commit to them prematurely

## Considered Alternatives

This decision spans several lifecycle axes. Each subsection records the
alternatives for one axis and the selected policy for that axis.

### Reset semantics: reuse the same identifier vs. dispose and re-create

- Reusing the same identifier (clearing state in place) keeps the browser's
  stored identifier valid, but blurs the boundary between "this session" and "a
  fresh session" and invites stale-identifier ambiguity.
- Disposing the session and requiring a fresh identifier for any subsequent work
  keeps each identifier bound to exactly one session lifetime.

Chosen: **dispose**. `DELETE /sessions/{sessionId}` destroys the session; the
identifier is not reused.

### Unknown-session response for DELETE: distinct status vs. uniform shape

- A `404` for DELETE would diverge from the existing eval/Canvas/Timeline/
  type-declaration routes, which answer unknown identifiers with `200` and a
  `{ ok: false, error, sessionId }` body.
- Reusing that same shape keeps a single, predictable protocol.

Chosen: **uniform shape**. DELETE answers an unknown identifier the same way the
other session-specific routes do.

### Disconnect handling: dispose on disconnect vs. keep until idle/explicit

- Disposing when the SSE connection drops would make every transient network
  blip destructive and would couple state lifetime to connection stability.
- Keeping the session until either explicit disposal or idle reclamation
  decouples state lifetime from connection liveness.

Chosen: **keep**. Browser disconnect alone never disposes a session.

### Multi-browser attachment: first-class support vs. defer

- A lease/ownership mechanism would let multiple browsers attach to one session
  with well-defined semantics, but is a substantial design with no current
  driver.
- Deferring keeps the door open without committing to semantics now.

Chosen: **defer**. Multiple browser attachment to one session is not a
first-class supported scenario at this stage.

## Decision

DuetsPad gains an explicit disposal route and an idle-reclamation policy:

- **Explicit disposal.** `DELETE /sessions/{sessionId}` disposes the matching
  `DuetsPadSession` and removes it from the session table. On success it
  returns `{ ok: true, sessionId }`. An unknown or unparseable identifier
  returns the same unknown-session response used by the other session-specific
  routes (`{ ok: false, error: "Unknown session.", sessionId }`), keeping the
  protocol uniform. The DELETE route body in `DuetsPadService` delegates to
  `SessionRegistry.TryDeleteSession`, which owns the removal and disposal.

- **No identifier reuse.** Once a session has been disposed, its identifier is
  treated as unknown for all subsequent requests. A browser that needs to
  continue obtains a fresh session through `POST /sessions`; it does not revive
  the disposed identifier.

- **Idle reclamation.** `SessionRegistry` reclaims sessions that have had no
  activity within a configurable idle timeout, disposing them and removing them
  from the session table via `RemoveIdleSessions`. A session reclaimed this way
  is thereafter indistinguishable from an explicitly disposed one. When the idle
  timeout is not configured, reclamation does not run and sessions persist until
  explicit disposal or service shutdown.

- **Activity definition.** Session creation, evaluation, and SSE stream activity
  — including the periodic keepalive emitted on the Canvas, Timeline, and
  type-declaration streams — all count as session activity. A browser holding a
  live SSE stream therefore keeps its session alive; only genuine inactivity
  leads to reclamation.

- **Disconnect is not disposal.** A dropped SSE connection or closed browser tab
  does not, by itself, dispose the session. State outlives transient
  disconnects and is removed only by explicit disposal or idle reclamation.

- **Multiple browser attachment is not first-class.** Attaching more than one
  browser to a single session is not a supported scenario at this stage. The
  service does not break the existing ability to have multiple SSE subscribers,
  but it provides no ownership or lease semantics for shared sessions.

This ADR records the durable lifecycle decisions only. The mechanisms used to
make these behaviors testable (clock and timer abstractions, sweep scheduling)
are implementation details and are intentionally not fixed here.

## Rationale

Binding each identifier to a single session lifetime, and requiring a fresh
identifier after disposal, keeps the service's view of "which sessions exist"
unambiguous and consistent with ADR-34's rule against implicit creation from
unknown identifiers. Treating keepalive and stream activity as session activity
makes the idle policy cooperate with DuetsPad's server-canonical SSE model
instead of competing with it: an actively viewed pad stays alive without
special-casing, while an abandoned one is eventually reclaimed. Refusing to
dispose on disconnect keeps server-side Canvas and Timeline state — the
authoritative copy under ADR-34 — robust against ordinary network behavior.

## Consequences

- **Positive**: sessions can be discarded explicitly and are reclaimed when
  unused, bounding resource growth in a long-running service
- **Positive**: the unknown-session protocol stays uniform across every
  session-specific route, including DELETE
- **Positive**: live, actively viewed pads are never reclaimed out from under a
  connected browser, and transient disconnects are non-destructive
- **Negative / trade-offs**: a browser cannot revive a disposed identifier; it
  must create a new session and re-establish any state it needs
- **Negative / trade-offs**: multi-browser attachment remains unspecified and
  will need its own design if it becomes a requirement
