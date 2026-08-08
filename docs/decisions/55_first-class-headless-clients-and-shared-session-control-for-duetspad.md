# ADR-55: First-Class Headless Clients and Shared Session Control for DuetsPad

## Status

Accepted — amends
[ADR-36](36_duetspad-server-canonical-output-protocol.md) (`timeline.update` field-projection
metadata) and partially supersedes
[ADR-42](42_duetspad-pad-control-surface-and-command-channel.md) (`pad.setEditorText` and
`control.setEditorText`), [ADR-47](47_duetspad-form-input-state-model.md) (concurrent field commit
projection and conflict resolution), and
[ADR-48](48_extract-duets-pad-into-its-own-package.md) (ownership of the protocol reference client)

## Context

DuetsPad is presented through a browser, but its server already has most of the machinery needed
by a non-browser client. `DuetsPadService` exposes HTTP operations below `/sessions`, and each
`DuetsPadSession` has a unified SSE stream for its projected output. Repository tooling has a
partial internal client, but `Duets.Pad` itself provides neither a supported reusable client nor
headless access to the browser-local Editor buffer. An AI agent, automated test, or other headless
system therefore cannot reliably intervene in a session that a human is using.

The required model is analogous to attaching another terminal to a tmux session. A human browser
and a machine client identify the same existing DuetsPad session, submit operations to that same
session, and observe its shared server output. The model is not one in which a machine client owns
the session, receives a private output stream, or needs every output event attributed to the
request that caused it.

This is a general `Duets.Pad` capability, not merely a convenience for repository-local AI tools.
A reusable .NET client lets adapters, integration tests, and .NET headless consumers share one
implementation without deciding which user-facing tool must expose the capability.

The Editor has a deliberately narrower sharing requirement than the existing projected surfaces.
Its last committed text must be readable and replaceable, but live collaborative editing and
change notification are not required.

## Decision Drivers

- Make headless intervention a supported `Duets.Pad` feature.
- Let a machine client target a human's existing session by an explicitly supplied session id.
- Cover the session operations needed for real intervention, not evaluation alone.
- Reuse the existing authenticated HTTP and unified SSE boundary.
- Put the reusable protocol implementation with the package that owns the protocol.
- Preserve the existing authentication, deletion, disconnect, and idle-reclamation rules.
- Preserve the shared-session model without introducing request attribution, client ownership, or
  leases.
- Avoid session enumeration and speculative discovery abstractions.
- Make Editor text retrievable and replaceable without revisions, live synchronization, or Editor
  events.

## Considered Alternatives

### A: Drive the existing browser UI as the machine interface

- Pro: Operates Monaco and the browser projection through the same interface as a human.
- Pro: Requires no supported headless protocol or public .NET client.
- Con: Makes ordinary session intervention depend on a browser process, selectors, focus, and
  presentation timing.
- Con: Is an unstable general control boundary for AI agents and headless systems.

### B: Document the raw HTTP/SSE protocol and let every consumer implement it

- Pro: Keeps the `Duets.Pad` public .NET surface smaller.
- Pro: The wire protocol remains usable from any language.
- Con: Adapters, integration tests, and .NET consumers would independently implement base-URI
  handling, Bearer authentication, bounded requests, SSE parsing and cancellation, attachment
  upload transactions, and lifecycle behavior.
- Con: The protocol's reference implementation would remain separated from the package that owns
  the server contract.

### C: Extend only the internal Sandbox client

- Pro: Provides a direct JSONL path for repository-local AI agents with little public API cost.
- Pro: Reuses the existing internal implementation.
- Con: ADR-16 defines Sandbox as internal developer and agent tooling, not an end-user deliverable.
- Con: Does not make headless intervention a general `Duets.Pad` capability.
- Con: Leaves tests and other .NET consumers dependent on a private interpretation of the
  `Duets.Pad` protocol.

### D: Add a reusable client to the existing `Duets.Pad` package

- Pro: Places the reference client with the protocol owner.
- Pro: Lets adapters, integration tests, and .NET headless systems share authentication, streaming,
  error, and lifecycle handling.
- Pro: Keeps the raw HTTP/SSE protocol language-neutral and requires neither a new package nor a
  new transport.
- Con: Adds a public API that must be maintained with the wire protocol.
- Con: Does not expose browser-local state that has not been committed to the session.

### E: Add a new WebSocket, JSON-RPC, or MCP execution boundary

- Pro: Could be tailored to a particular automation ecosystem.
- Con: Duplicates the existing HTTP/SSE transport, authentication gate, resource ceilings, and
  session lifecycle.
- Con: Adds a new security-sensitive server surface before the existing protocol has been made
  generally consumable.

## Decision

Choose alternative D. `Duets.Pad` supports headless intervention in an existing session through
its HTTP/SSE protocol and provides a reusable public .NET client in the existing `Duets.Pad`
package.

### Browser bootstrap and existing-session targeting

The headless workflow starts with a known, live session id. A typical handoff is:

1. A human evaluates `pad.sessionId` in the target DuetsPad session.
2. The human supplies the base URI, session id, and any required Bearer credential to the AI or
   other headless caller.
3. The public client constructs a local session-scoped handle from those values and uses the
   existing session routes for observation and control.

Constructing this handle does not register a participant or perform a server-side attach operation.
Each subsequent HTTP request or optional unified SSE subscription carries the supplied id in its
existing `/sessions/{sessionId}/...` route, and `SessionRegistry` resolves that id for the operation.
Opening SSE registers an ordinary subscriber and receives the existing initial state burst, but a
caller that only needs direct operations is not required to keep an SSE subscription open. No
dedicated attach or existence-check endpoint is added.

The public headless client does not create sessions, does not expose the browser's create-or-reuse
operation, and never calls `POST /sessions`. Addressing an unknown, expired, deleted, or malformed
session id receives the existing `Unknown session.` failure from the attempted session operation and
must never fall back to session creation.

The browser continues to bootstrap through `POST /sessions` as established by ADR-34. Supplying an
id that still names a live session resumes that session. Supplying no id, or an id that is absent or
stale, causes the registry to mint and return a fresh id; it does not create or revive the supplied
id. This browser bootstrap is not part of the supported public headless-client surface. An
authenticated caller using the raw HTTP protocol can still call `POST /sessions`, because the server
does not classify callers as browsers or machines. Excluding creation is therefore a public-client
capability boundary, not a new caller-type authorization rule.

No session-list, session-search, or discovery endpoint is added. No placeholder discovery
abstraction is added to the client. If discovery is required later, it needs a separate decision
covering information exposure, authorization, selection semantics, and lifecycle races.

### Authentication and lifecycle

Every headless request goes through the same path-based `/sessions` authentication gate defined by
ADR-49. When authentication is configured, the machine client sends the Bearer credential on each
HTTP request and when opening SSE. There is no internal bypass or machine-specific authentication
path. Possession of a session id does not bypass `Authenticate`.

ADR-38's existing lifecycle remains in force:

- disposing or disconnecting a machine client does not delete its target session;
- closing an SSE subscription does not delete the session;
- explicit `DELETE /sessions/{sessionId}` remains the deletion operation and is available because
  deletion is part of the agreed session API;
- `IdleTimeout` continues to reclaim abandoned sessions; and
- disposed or expired identifiers are never reused, and public headless operations against them
  fail as unknown-session operations.

No client ownership, borrowed/owned-session mode, participant registration, lease, or controlling
client role is introduced. Existing reset behavior remains governed by ADR-42. This decision does
not add a reset endpoint, a replacement-session event, or automatic replacement-session following.
This resolves ADR-38's deferred shared-session question only for the browser-and-headless model
defined here; it does not make multiple-browser presentation behavior a supported feature.

### Shared-session behavior

The browser and machine client are peers when operating the same session:

- evaluation and interaction invocation use the same existing per-session serialization path;
- server-canonical Canvas, Timeline, Modal, field, and attachment state remains owned by the one
  target `DuetsPadSession`;
- subscribers observe the same unified SSE output and its existing initial state burst; and
- accepted machine operations use the same state and projection rules as equivalent browser
  operations.

Server-canonical mutations use authoritative projection mechanisms so every connected peer
observes accepted state; they do not rely on the sending browser having already updated its own DOM.

Field writes, including direct browser or headless commits, the field snapshot carried by an
interaction invocation, and script-side assignment to a field handle's `value`, use server
processing order and last-write-wins. Every accepted write updates the canonical field store and is
projected to connected peers, even when the accepted value equals the current server-held value.
When a later accepted write reaches a browser, it overwrites and discards any focused or otherwise
uncommitted local value for that field. The discarded value must not be sent later by a delayed
focus-out commit or a subsequent interaction snapshot.

Canvas and Modal projection reuse their existing patch operations and mark accepted field-value
operations as authoritative. Because a Timeline update replaces an entire entry while unrelated
pending fields should survive that replacement, `timeline.update` gains an optional
`authoritativeFieldId` string. When present, the browser excludes that field from its pending-value
restoration while retaining pending edits to other fields. Full recovery snapshots received after a
stream gap are authoritative and must not restore local values over a newer snapshot revision,
because the gap may contain an accepted write the browser did not observe.

This rule does not require a field revision, client identity, lease, merge, conflict response, or a
new named event; it extends the existing projection events. This supersedes ADR-47's preservation of
focused or uncommitted field input and its decision not to broadcast browser-originated commits,
and amends ADR-36's `timeline.update` event shape. ADR-47's remaining field-state contracts and
ADR-50's attachment-state contracts remain controlling.

Events are not attributed to the client or HTTP request that caused them. No client id, request id,
operation id, ownership marker, or causal filtering is added. This follows the tmux model: inputs
share one execution context and observers share its output. If request attribution or auditing is
needed later, it is a separate protocol decision.

Editor text is the explicit exception to continuous projection. Its state and access rules are
defined below; the shared-session model must not be used to infer Editor change notification.

### Supported machine operations

The public client covers the existing-session capabilities used by the browser, including:

- evaluation;
- tagged-template completion;
- Canvas snapshot retrieval;
- unified SSE observation;
- interaction invocation, including the field and attachment state required by that protocol;
- field commits;
- attachment selection begin, streaming upload, commit, and cancellation;
- Editor text retrieval and whole-document replacement; and
- explicit session deletion.

Modal actions and dismissals continue to use their existing opaque interaction handlers; this ADR
does not add a separate Modal command API. The client must preserve the protocol's resource limits
and streaming attachment behavior.

### Script-visible session identity

The `pad` global exposes the current session id as a read-only string:

```typescript
declare const pad: {
  readonly sessionId: string;
};
```

This is the explicit human-to-machine handoff mechanism and also permits a script to identify the
session in which it is executing. No additional browser copy UI is required by this decision.

### Editor state

Editor text becomes last-committed, server-held state belonging to `DuetsPadSession`. It is exposed
to scripts as one read/write property:

```typescript
declare const pad: {
  editorText: string;
};
```

The existing `pad.setEditorText(text)` API is removed rather than retained as a second way to write
the same state. Its `control.setEditorText` presentation command is removed with it. This partially
supersedes ADR-42; `pad.resetSession`, `pad.openText`, and the rest of ADR-42's command-channel
decision remain unchanged.

The HTTP protocol provides operations to read and replace the same state:

- `GET /sessions/{sessionId}/editor` returns the last committed text.
- `PUT /sessions/{sessionId}/editor` replaces the whole text and is bounded by
  `MaxRequestBodyBytes`.

Editor changes use server processing order and last-write-wins. There is no Editor revision,
conditional request, merge, lock, or conflict response. A later accepted write may overwrite an
earlier human or machine write.

The browser's `POST /sessions` bootstrap request may include its local Editor text as an initial
value candidate. The server applies that candidate atomically only when the request creates a fresh
session. If the supplied session id resolves to an existing live session, the candidate is ignored
and the session's server-held Editor text remains authoritative. After bootstrap, the browser reads
the Editor state through the session-scoped GET operation and initializes Monaco from that value.
This avoids a post-creation PUT race and prevents a reconnecting browser's local storage from
overwriting an already-live session. The candidate is subject to `MaxRequestBodyBytes` with the rest
of the bootstrap request.

The browser does not send every Monaco edit. It commits Editor text before running it. On Editor
focus loss, page hiding, or departure, it sends only when the local text differs from the last text
that browser read from or successfully wrote to the server; departure remains best-effort. This
prevents an untouched stale browser buffer from overwriting another client's later write merely
because focus changed, without adding polling or change notification. A read may therefore lag
behind characters still being typed in a focused Editor; it returns the last committed value.
Existing local-storage and handoff behavior remains the source of the initial-value candidate for a
newly created browser session, but it is not authoritative for an already-live session.

Editor access is pull-based. This decision adds no `editor.*` SSE events, no Editor state to the
unified SSE initial burst, and no polling or other requirement to notify an already connected
browser or machine client when another client replaces the text. A client that needs the value
reads it explicitly. The absence of Editor notification applies only to Editor text and does not
remove the existing SSE projection requirements for Canvas, Timeline, Modal, fields, or
attachments.

### Adapters

An adapter that uses the .NET client may expose the subset appropriate to that tool. This decision
does not designate Sandbox or any other adapter as the required AI-facing interface, and an
adapter's operations do not define the public client's capabilities.

### Verification boundary

Raw protocol tests verify authentication, wire shapes, limits, and failures independently of the
reference client so the server and client cannot conceal a shared mistake. Integration tests use the
public client against a real DuetsPad server and cover strict existing-session targeting, supported
operations, lifecycle, and the shared field and Editor rules defined above.

## Rationale

The existing HTTP/SSE protocol already contains the important server boundaries: authenticated
commands, a server-issued session identity, a serialized execution path, authoritative projected
state, a broadcast stream, and resource ceilings. Replacing it would duplicate the most
security-sensitive parts of DuetsPad without improving the requested shared-session model.

The public client lets adapters, protocol integration tests, and .NET headless systems share URI
handling, Bearer authentication, response handling, SSE parsing, cancellation, attachment
streaming, and non-destructive disposal. Keeping that implementation in an internal executable
would leave a general `Duets.Pad` feature owned outside the package that defines the protocol.

The public client implements session operations, not a second client-side Canvas/Timeline/Modal
state machine, discovery mechanism, orchestration layer, or transport.

The Editor and field rules are part of this decision because they close concrete gaps in operating
one existing session from browser and machine peers. They are not independent collaboration
features: Editor access supplies the otherwise missing inspect-and-replace operation, while field
projection prevents accepted browser and machine operations from leaving peers with contradictory
server-canonical state.

The tmux analogy makes shared execution and broadcast output more important than attribution or
ownership. It also does not imply live synchronization of every browser-local editing gesture.
Server-held, explicitly committed, revision-free Editor text is sufficient for inspection and
replacement while keeping the agreed non-realtime behavior.

Separating browser bootstrap from existing-session targeting prevents the public client from
accidentally allocating a replacement session while preserving the lifecycle and resume behavior
already established for the browser. This is an API-capability distinction: the authenticated raw
HTTP creation route remains available, while the supported headless client deliberately omits it.

## Consequences

- **Positive**: AI agents, automated tests, and other headless systems can operate an existing
  authenticated DuetsPad session without driving the browser DOM.
- **Positive**: Browser and machine operations share the existing session state, serialization,
  authentication, resource ceilings, and lifecycle.
- **Positive**: The protocol reference implementation belongs to `Duets.Pad`, and adapters can
  reuse it without defining the package's capability boundary.
- **Positive**: `pad.sessionId` provides explicit session handoff without session enumeration.
- **Positive**: Unknown-session operations fail instead of silently creating a replacement session
  through the public client.
- **Positive**: Editor text is inspectable and replaceable without revisions, live collaboration,
  or new Editor events.
- **Negative / trade-off**: Any successfully authenticated caller that knows a session id can use
  all agreed session operations, including destructive deletion; there is no per-client
  authorization or ownership.
- **Negative / trade-off**: The reusable .NET API becomes a compatibility obligation alongside the
  wire protocol.
- **Negative / trade-off**: Editor reads can lag behind uncommitted focused edits, and
  last-write-wins may discard an earlier concurrent change.
- **Negative / trade-off**: A later accepted field commit discards a browser's uncommitted value for
  that field so all peers converge on the server-canonical value.
- **Negative / trade-off**: A newer full recovery snapshot replaces pending local field values on
  that recovered projection even when its revision may have advanced for another mutation;
  selective preservation across an unknown event gap would require additional history or
  client-revision tracking.
- **Negative / trade-off**: Session creation remains possible to an authenticated raw HTTP caller;
  omitting it from the public client prevents accidental use but is not an authorization boundary.
- **Excluded or deferred**: Session discovery, request or event attribution, client ownership and
  leases, Editor notification and revisions, richer field conflict handling, reset replacement
  semantics, MCP adapters, and a separate client package require independent decisions if later
  requirements justify them.
