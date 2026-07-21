# ADR-52: DuetsPad Modal Surface

## Status

Accepted

## Context

DuetsPad can project persistent Canvas and Timeline content, attach server-side
handlers to rendered nodes (ADR-41), and accept server-canonical form values and
file attachments from the browser (ADR-47, ADR-50). It does not have a modal
surface that can combine those capabilities and report a user's choice to a
later server-side turn.

A browser modal cannot synchronously return a future user choice to the script
that opens it. Evaluation and interaction handlers run to completion while the
session evaluation gate is held, whereas a user's response arrives in a later
HTTP request that needs the same gate. Waiting for that request in the opening
turn would deadlock. The imperative `control.*` channel from ADR-42 also cannot
represent the modal itself: control events are transient commands, while an
open modal must remain authoritative across SSE reconnects and must own the
lifetime of its fields, attachments, and interaction handlers.

The required primitive must accept arbitrary `ui.*` content, including mutable
slots and file pickers, support multiple named footer actions, report the chosen
action, close programmatically, and preserve modal focus behavior.

## Decision Drivers

- Fit the synchronous, turn-based evaluation and interaction model without
  blocking the evaluation gate.
- Reuse `DisplayContent`, interaction invocation, field snapshots, attachment
  validation, and incremental projection rather than creating parallel UI
  mechanisms.
- Make open-modal state reconnectable and give every retained handler, field,
  slot, and attachment an explicit lifetime.
- Execute a modal result callback at most once under duplicate clicks and
  concurrent browser attachments.
- Preserve focused edits during mutable-content updates and provide accessible
  modal focus and dismissal behavior.
- Bound long-lived modal state independently of authentication.

## Considered Alternatives

### A: Return or await the user's choice in the opening turn

- Pro: resembles native `prompt` and Promise-based browser modal APIs.
- Con: the response is a later interaction request that needs the evaluation
  gate held by the waiting turn, so the current execution model cannot honor the
  contract without a suspendable cross-turn script runtime.

### B: Send `control.modal.open` and `control.modal.close` commands

- Pro: extends the existing imperative command serializer with two operations.
- Con: an open modal is durable server-owned state that must be replayed and
  participate in reachability; representing it as an ephemeral command conflicts
  with ADR-36 and ADR-42 and makes reconnect convergence implicit.

### C: Add a server-canonical, callback-driven Modal surface (chosen)

- Pro: fits the existing turn model, reuses rendering and interaction transport,
  makes reconnect behavior explicit, and gives modal-owned resources a precise
  lifetime.
- Con: every Canvas/Timeline-specific placement and reachability path must learn
  about a third projected surface.

### D: Limit the first version to immutable body content and form fields

- Pro: avoids a modal patch protocol and some placement work.
- Con: describing that subset as arbitrary `ui.*` content would be false; slots,
  nested interactive content, and file pickers would be accepted syntactically
  but would not retain their established behavior.

## Decision

Choose **C**, including mutable and attachment-backed content in the initial
contract rather than adopting D.

### Script API and result model

`ui.modal` follows the callback-first shape of `ui.button`:

```ts
const name = ui.textBox({ placeholder: "Your name" });

const modal = ui.modal(
  ui.stack([ui.label("Name"), name]),
  result => {
    if (result.reason === "action" && result.actionId === "save") {
      dump(name.value);
    }
  },
  {
    title: "Enter name",
    buttons: [
      { id: "cancel", label: "Cancel" },
      { id: "save", label: "Save", variant: "primary" },
    ],
    defaultButtonId: "save",
    dismissButtonId: "cancel",
    size: "md",
  },
);
```

The body accepts the same values as Canvas and Timeline rendering. Button ids
are non-empty and unique. A string button is shorthand whose id and label are
that string. `defaultButtonId` selects the action invoked by Enter. An omitted
`dismissButtonId` produces `{ reason: "dismiss", actionId: null }` for Escape,
backdrop, or header-close dismissal; a string maps dismissal to that action id;
explicit `null` disables those dismissal mechanisms. Referenced default and
dismiss ids must exist.

The callback receives either `{ reason: "action", actionId: string }` or
`{ reason: "dismiss", actionId: string | null }`. Before it runs, the existing
invoke field snapshot and attachment precondition are applied, so captured input
and file-picker handles expose the latest committed values. Footer actions close
automatically. Ordinary interactions inside the body do not close the modal and
may update slots or other surfaces.

The returned session-bound handle exposes `isOpen` and idempotent `close()`.
Programmatic close does not call the result callback: the calling script turn is
already the explicit continuation. A stale handle remains safe after user close,
session reset, or disposal. Body render failures follow the existing non-throwing
surface convention: a `render-error` Timeline entry is appended and the returned
handle is already closed.

### Server-canonical state and ownership

Each session owns an insertion-ordered active-modal collection under
`_stateLock`. A modal projection contains its id, revision, reduced body tree,
committed body and footer interactions, and presentation options. Its committed
footer and dismiss handlers retain the result callback while the modal is
active. The session limit `MaxActiveModals` defaults to 8; `null` means
unlimited, and opening beyond the limit fails before committing resources.

`InteractionStore` owns committed interactions by modal id in addition to its
Canvas and Timeline collections. Closing a modal unregisters every body,
footer, and dismiss handler immediately. Field, slot, and attachment placement
searches include every active modal tree. Script-originated field writes,
browser commits, slot updates, attachment projection, pruning, and invoke
validation all use those modal roots.

A footer or dismiss handler applies the invoke snapshot, then atomically claims
the still-open modal before calling user code. The first claim wins; subsequent
requests receive the existing stale-interaction outcome. The callback runs while
the modal's fields and attachments remain reachable. A `finally` boundary then
closes the modal, releases its interactions, prunes field-backed state, and
broadcasts the close even when user code throws. Session reset and disposal
discard modals without invoking callbacks.

### Projection protocol and reconnect

Modals are a state namespace on the existing multiplexed SSE stream, not a
`control.*` command:

- `modal.snapshot`: the ordered full set of active modal projections for a
  newly attached subscriber;
- `modal.open`: one newly committed projection;
- `modal.patch`: a contiguous revision update with positional operations and
  the full post-update interaction set;
- `modal.replace`: one full projection when replacement is no larger than a
  patch;
- `modal.close`: removal of one modal id.

The modal revision and patch rules reuse ADR-45's full-baseline, contiguous
revision, preflight-before-mutate, and full-interaction-set invariants. A full
replacement remains the fallback when a patch is not smaller. Modal state is
committed before its event is broadcast under the same lock discipline as the
other surfaces. The initial SSE burst includes `modal.snapshot` after the
Canvas and Timeline baselines, allowing reload and reconnect to converge even if
the opening event was missed.

### Browser lifecycle

The browser retains one root per projected modal so field and attachment
snapshots cover visible and queued modals. Modals form a FIFO queue and only
the head is visible; nested stacked modals and competing focus traps are not
used. While the queue is non-empty, all non-modal application content,
including the toast container, is inert. The visible modal traps focus, Escape
and backdrop dismissal follow `dismissButtonId`, and final close restores the
previous focus when it is still valid.

An action click disables the relevant controls while the invoke is pending but
does not optimistically remove the modal. Only an accepted `modal.close` event
removes it, avoiding client/server divergence after a failed request. Session
swap tears down all modal roots, bindings, pending focus state, and inertness.

## Rationale

Callback continuation is the existing interaction model expressed honestly: the
opening evaluation ends, the browser later invokes an opaque server handler, and
the callback executes in that later turn. A Promise facade would not make the
opening turn suspendable and would add continuation behavior without solving the
gate boundary.

Calling Modal a surface follows from its lifecycle, not its appearance. It is
canonical state with reconnect replay and owns resources while visible or
queued, whereas `control.*` exists specifically for non-stateful browser
commands. Revisioned projection is required by the arbitrary-content contract:
full replacement during a slot or field update could destroy focus and an
uncommitted edit.

FIFO presentation gives simultaneous opens deterministic behavior without
nested modal accessibility hazards. Atomic claim plus immediate interaction
retirement gives a single result even when two browser attachments respond at
nearly the same time. Waiting for the authoritative close event keeps the client
honest when invocation fails.

## Consequences

- **Positive**: one primitive supports text input, arbitrary structured content,
  mutable slots, file pickers, multiple actions, and explicit result identity.
- **Positive**: active modals and their server-committed values survive SSE reconnect and browser
  reload.
- **Positive**: Modal reuses the established rendering, invoke, field,
  attachment, and patch contracts rather than adding a second form system.
- **Positive**: action callbacks are at-most-once and cleanup is deterministic on
  success, exception, reset, and disposal.
- **Negative / trade-offs**: Canvas/Timeline-specific reachability and projection
  code must be generalized or extended to Modal, increasing the initial change.
- **Negative / trade-offs**: `ui.modal` is callback-based; a true cross-turn
  `await` requires a separate suspendable-runtime decision.
- **Negative / trade-offs**: only the queue head is presented, so later modals
  wait even though their server projections and resources are already retained.
- **Negative / trade-offs**: programmatic close deliberately does not invoke the
  result callback, giving it different continuation semantics from user close.
