# ADR-44: Tagged-Template Completion RPC Boundary

## Status

Accepted

## Context

DuetsPad needs host-provided completions inside tagged-template bodies:

```ts
path`/foo/ba`
```

The candidate data belongs to the host process, so TypeScript's native
completion cannot produce it: a template body is just an opaque string to the
language service.

An earlier design placed a server-side JavaScript lexical detector in front of
the completion callback. That made the server decide whether a caret position was
really inside a tagged-template expression before dispatching to the callback.
Reviewing that premise showed that it moved parser-grade work to the wrong side
of the protocol. The detector had to reason about strings, comments, regular
expressions, division, template interpolation, member access, and line breaks,
while the server gained no meaningful security boundary from doing so.

## Decision Drivers

- Avoid hand-written JavaScript lexer/parser logic in Duets.
- Keep the server-side completion path small, bounded, and auditable.
- Preserve a clean separation between editor firing conditions and host callback
  dispatch.
- Keep editor-context detection in the Monaco client, using Monaco tokenization
  plus narrow client-side helper logic.
- Keep runtime tagged-template evaluation independent from edit-time completion.
- Bound host callback execution with size, rate, concurrency, timeout,
  cancellation, result-count, and replacement-span checks.

## Considered Alternatives

### A: Server-side JavaScript context detector

- Pro: The server can reject completion requests it believes are outside a
  tagged-template context.
- Pro: A non-Monaco caller can send raw source and position only.
- Con: Correctness approaches JavaScript parser territory, especially around
  regular expressions, template literals, interpolation, member tags, and
  automatic semicolon insertion.
- Con: The detector becomes a maintenance focus unrelated to the host completion
  feature itself.
- Con: The security benefit is negligible because the session already authorizes
  eval, and completion callbacks are read-only bounded RPC handlers.

### B: Server as a thin completion RPC boundary

- Pro: The server does not parse JavaScript source. It validates only the tag,
  request size, field bounds, callback limits, result count, and replacement
  spans.
- Pro: The Monaco client owns the editor firing condition and maps
  segment-relative spans back to model ranges, which is where editor positions
  naturally belong.
- Pro: There is only one context detector, on the client side, with no
  server/client drift.
- Con: Non-Monaco callers must provide explicit completion context rather than
  raw source plus position.
- Con: If the client misfires, the server may run a bounded read-only callback
  unnecessarily.

### C: Server-side parser-backed syntax authority

- Pro: The server can make parser-grade syntax decisions without hand-written
  lexical approximation.
- Pro: It could support non-Monaco callers with raw source and position.
- Con: It couples the runtime server path to a JavaScript/TypeScript parser for a
  UI firing condition.
- Con: It duplicates the Monaco editor's existing knowledge and adds cost to the
  common keystroke-triggered path.

## Decision

Use alternative B. The `/sessions/{id}/complete` endpoint is a thin, safe RPC
boundary to registered tagged-template completion providers. It never scans
JavaScript source text and never decides whether a caret is syntactically inside
a tagged template.

The DuetsPad Monaco client detects whether completion should fire for a
registered simple identifier tag by combining Monaco tokenization with a narrow
client-side helper. It builds the explicit context
`{ tag, textBeforeCaret, textAfterCaret, currentSegmentRaw, segmentIndex,
caretOffsetInSegment, rawSegments }` and sends it to `/complete`. The current
protocol accepts only the active single template segment (`segmentIndex: 0`);
the server normalizes `rawSegments` to `[currentSegmentRaw]` instead of trusting
the client-supplied list. The server checks that the tag is registered, enforces
operational limits, builds `TemplateCompletionContext` from the normalized
request, dispatches the callback without holding the eval/session lock,
validates returned items, caps the result, and returns Monaco-neutral candidate
data.

Runtime evaluation is a separate path. `DuetsSession.RegisterTaggedTemplate`
records the completion callback in a core registry and, only when an evaluator is
provided, asks the backend to install a callable script global and registers a
callable `.d.ts` declaration. A completion-only tag does not create a runtime
global or callable declaration.

## Rationale

The server-side detector attempted to defend against the wrong threat. Calling a
completion callback outside the intended editor context is not a privilege
boundary in DuetsPad: the same session id already allows eval, and the completion
callback contract is read-only and bounded. The real defenses are the callback
contract and the RPC limits.

Putting syntax authority on the server also fights the architecture. The Monaco
client owns the model, caret position, tokenization access, and completion UI. It
can decide whether to offer completions and can map segment-relative replacement
spans back to model ranges without inventing absolute offsets in the server
protocol. The small client helper is a UI firing heuristic, not a security
boundary. The server is better kept as the host callback boundary.

Constraining registered tags to simple ASCII identifiers keeps the public API and
client matching rule narrow. Member, computed, and call-expression tags are not
registered completion tags in this design.

## Consequences

- **Positive**: No server-side JavaScript lexer/parser exists for this feature.
- **Positive**: `/complete` has a small security and resource-control surface:
  registered-tag check, size bounds, rate limit, single in-flight callback,
  timeout/cancellation, result cap, and replacement-span validation.
- **Positive**: Completion-only registration cannot trick the editor into
  offering a callable runtime API; callable declarations are emitted only when a
  runtime evaluator is installed.
- **Positive**: Client and server responsibilities are explicit: Monaco decides
  when to ask, the server decides whether and how to run a registered provider.
- **Positive**: Client-provided `rawSegments` is not an authority; the server
  derives the callback's raw-segment list from the accepted current segment.
- **Negative / trade-offs**: A non-Monaco caller must provide explicit template
  context instead of raw source and caret position.
- **Negative / trade-offs**: Client context detection is still a UI concern and
  may be conservative. A misfire can run a bounded read-only callback, but it
  cannot cross a privilege boundary.
