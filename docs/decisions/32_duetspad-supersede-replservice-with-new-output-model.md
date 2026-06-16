# ADR-32: DuetsPad Supersedes ReplService as the Browser Debug Pad

## Status

Accepted

## Context

`ReplService` ([ADR-7](7_use-monaco-editor-as-the-browser-based-repl-ui.md))
provides a browser-based TypeScript REPL with a Monaco editor and an
append-oriented string console log. That model is useful for probing scripts,
but it cannot display structured text or UI elements in the output surface. It
is therefore too narrow for the next Duets web UI goal: a LINQPad-inspired
browser surface that can also act as an embedded debug dashboard for host
applications.

An append log is useful for history, but it is not a surface for representing
current debug-dashboard state.

LINQPad-style output is a primary reference point. For example, a LINQPad
control can be dumped into the output surface:

```csharp
new LINQPad.Controls.Button("Hello").Dump();
```

This ADR defines the name and top-level surfaces of the new browser UI.

## Decision Drivers

- Replace the append-only browser REPL with a debug-pad surface suited to structured output
- Preserve the existing Monaco, TypeScript, declaration, completion, and HTTP infrastructure where practical
- Separate history-oriented output from persistent display state
- Define the top-level DuetsPad vocabulary that Canvas, Timeline,
  rendering-model, session, and object-rendering decisions can use
- Avoid requiring embedders to maintain a separate SPA build pipeline

## Considered Alternatives

### A: Continue extending `ReplService`

- Pro: smallest naming and migration change
- Pro: preserves the current editor/log mental model
- Con: the append-only REPL shape does not naturally represent persistent dashboard state
- Con: follow-up decisions would inherit a name that no longer describes the UI
- Con: output history and display state remain conceptually conflated

### B: Keep Duets web UI as a REPL and document external dashboards

- Pro: keeps Duets core smaller
- Pro: lets each embedder choose its own frontend stack
- Con: reintroduces an external frontend build burden for the common debug-dashboard use case
- Con: prevents Duets from providing a shared LINQPad-like output model
- Con: duplicates dashboard plumbing across host applications

### C: Introduce DuetsPad as the successor to `ReplService`

- Pro: gives the new browser UI an explicit name and vocabulary
- Pro: separates history output from persistent display state
- Pro: keeps the debug-dashboard surface inside the Duets distribution model
- Con: introduces a new service surface and migration work
- Con: requires follow-up contracts for sessions, Canvas, Timeline, render nodes, and object rendering

## Decision

Choose **Alternative C**.

The new Duets browser debug UI is named **DuetsPad**.

DuetsPad supersedes `ReplService` as the Duets browser debug UI.

DuetsPad has four primary surfaces:

- **Editor**: the Monaco-based script authoring surface
- **Canvas**: the persistent structured display-state surface
- **Timeline**: the structured history surface for evaluation output,
  `dump`, console output, diagnostics, and output errors
- **Immediate**: the single-line evaluation input for lightweight probing

The `dump(value)` operation is history-oriented: it appends structured output to
the Timeline and returns the input value. Immediate evaluations are also
history-oriented: their evaluation results are shown in the Timeline rather than
owned by the Immediate input surface. Persistent display state is mutated
explicitly through Canvas APIs.

This ADR does not decide whether a successful Editor run that did not call
`dump` should automatically append its final evaluation result to the Timeline.
That behavior belongs to the detailed Timeline and evaluation contracts.

This ADR defines only DuetsPad and its top-level vocabulary. Detailed contracts
for Canvas, Timeline, the rendering model, session ownership, and interaction
lifecycle are outside the scope of this decision. In particular, first-class
interaction support such as buttons and form controls is compatible with
DuetsPad's long-term direction, but this ADR does not decide the interaction
mechanism or handler lifecycle.

## Rationale

The current `ReplService` name and two-surface model describe an editor plus
log. The new browser debug UI needs a broader vocabulary: scripts can still
probe values, but they can also build a persistent debug display. Deciding the
service name and top-level surfaces in this ADR prevents later ADRs from
introducing terms such as Canvas, Timeline, and Immediate without a parent
concept.

Keeping DuetsPad inside the Duets package preserves the current embeddability
story. Host applications can get a browser debug surface without adopting a
separate frontend build process.

First-class event handlers and interactive controls are separate from the name
and top-level surface definition. Keeping them out of this ADR separates the
browser debug pad concept from the design of session-live capabilities.

## Consequences

- **Positive**: Duets has a named successor to `ReplService` for browser debug dashboards
- **Positive**: follow-up ADRs can refer to Editor, Canvas, Timeline, and Immediate as defined terms
- **Positive**: history output and persistent display state are separated at the top level
- **Positive**: existing Monaco, TypeScript, declaration, completion, and HTTP infrastructure can be reused
- **Negative / trade-offs**: `ReplService` callers must migrate to the DuetsPad service surface
- **Negative / trade-offs**: several follow-up contracts are required before the implementation is complete
- **Negative / trade-offs**: first-class controls and interaction lifecycle must be decided separately
