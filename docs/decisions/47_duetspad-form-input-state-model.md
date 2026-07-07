# ADR-47: DuetsPad Form Input State Model

## Status

Accepted

## Context

DuetsPad's interaction model (ADR-41) lets a script attach a server-side click handler to a button
or link; the browser triggers it with a body-less `POST /sessions/{id}/interactions/{handlerId}/invoke`.
That covers "run some server code when the user clicks", but not "let the user enter data that the
click handler can read". The concrete need is a set of input controls (text entry, checkbox,
dropdown, slider, and the like) whose current contents a button handler can read — without
reproducing an HTML `<form>` submit, and without the handler being coupled to any particular control.

The unresolved question is not the control catalog — adding controls to the `ui.*` surface follows
the existing render-node (ADR-35) and interaction (ADR-41) patterns and is not an architectural
decision. The question is **where an input's value lives, how it travels between browser and server,
and how long it survives.**

Two facts shape the answer:

- The value is edited by a human typing into the browser, so the browser is where fresh input first
  appears. Under ADR-36 every browser surface is a pure *projection* of server-canonical state; an
  input value is the first state class where the browser also *produces* values.
- ADR-45 projects value changes to the browser through `canvas.patch` / `CanvasDiffer`, and ADR-46
  locates already-projected content for in-place update by marker search. These already exist and
  should carry input-value projection rather than a new channel.

## Decision Drivers

- Satisfy the requirement: a click handler reads every input's current value; no form submit; the
  handler stays decoupled from the controls.
- Treat input contents as durable session data, not ephemeral browser view-state: a value must
  survive projection churn (incremental patches, SSE reconnect, option-list changes) while the
  control that holds it is on screen.
- Reuse ADR-45 projection and ADR-46 placement discovery; do not invent a parallel value channel.
- Avoid per-keystroke browser→server traffic.
- Make no promise that a user's value is *valid* for its control.

## Decision

### Input values are server-canonical session state

Each session owns a **field store** keyed by a stable field identity. The value of every input
control lives there and is the single source of truth, exactly as Canvas and Timeline are
server-canonical (ADR-36). Identity lives on the input *handle*, not on the render nodes, and is
rendered as a marker so placements can be found for update — the same identity-on-the-handle,
immutable-nodes model ADR-46 established for `ui.slot`.

### The browser is a second writer that commits

This is the one place where ADR-36's "browser is a projection" is amended: for input values the
browser also **commits** changes to the server, which remains the canonical holder. The browser does
not own the value; it proposes edits and the server-held value is authoritative. A script-side write
(`handle.value = x`) is another writer to the same store.

Commit triggers:

- **Focus-out (blur)** is the normal commit boundary. Per-keystroke (`change`/`input`) commit is
  rejected as excessive.
- Because a user may type into a control and click a button *without* blurring first, and a separate
  blur-commit POST has no ordering guarantee against the invoke, the **invoke request carries a
  snapshot of the relevant field values in its body**. This extends ADR-41's body-less invoke POST.
  The snapshot guarantees the handler observes the latest edit regardless of blur timing.

### Projection and write-back reuse the existing patch path

A script-side write to `handle.value` mutates the store and is projected to the browser through the
ADR-45 `canvas.patch` path via `CanvasDiffer` (falling back to `canvas.replace` on a large diff),
locating placements by ADR-46 marker search — the same path `ui.slot` uses. There is no separate
"targeted patch" that bypasses the differ; a single-field write simply produces a small diff.

Because `value` / `checked` / `selectedIndex` are **live DOM properties** rather than attributes, the
browser's attribute-based projection is extended to set these properties when projecting an input
node. A browser-originated commit in the ordinary single-placement case is not echoed back to the
committing browser.

### Browser commits are scoped to single attachment

A browser-originated commit updates the authoritative Canvas/Timeline state silently — without
broadcasting a patch or advancing a revision — so the committing browser sees no echo and a *later*
(re)connecting browser gets the committed value through its snapshot. It is deliberately **not**
pushed to a different browser that is *already* attached to the same session: doing so would require
adding client identity to the commit and SSE protocols purely to keep multiple concurrent browsers in
sync, which is disproportionate given ADR-38 declares multiple attachment "not first-class." The
accepted consequence: while two browsers are attached at once, one's field edits are not reflected in
the other until it reconnects. If genuine multi-tab co-editing is ever needed, this is revisited with
a client identity that lets commits broadcast to non-origin subscribers while suppressing the origin
echo.

### Lifetime is tied to the rendered content

A field value is retained for exactly as long as the control's rendered content exists: it survives
incremental patches, SSE reconnect, and option-list changes. A **full canvas rebuild** — a script
re-run that clears and reconstructs the canvas — destroys the control and resets its value. This
mirrors ADR-41, where a handler is released when its output is replaced: an input is part of that
output and shares its lifetime. Consequently the session data for inputs is intentionally *not*
carried across a re-run, and no identity-overlay reconciliation across re-runs is performed.

### Values are strings; validity is never guaranteed

Every field value is a string, stored and returned verbatim — no coercion and no validation. A
script that wants a number parses the string itself. A checkbox is represented as the string
`"True"` / `"False"` (`bool.ToString()`), because the browser exposes its state as a boolean and no
`on`/`off` string is imposed.

A stored value is **not guaranteed to be valid for its control**. A dropdown or radio value absent
from the current option set is still retained. The consequence, accepted deliberately: a `<select>`
cannot display an out-of-range value, so its rendered selection may diverge from the stored value
until the control is next operated, at which point the browser commits a valid option over the
retained one. Text-like controls have no such divergence.

### Conflict resolution

A server-side write or projection that lands on a field the user is currently editing (focused or
with an uncommitted edit) must not clobber the in-progress edit: the browser guards the apply step by
preserving the focused/uncommitted value. This is the ordinary controlled-input concern, not a
change in who owns the value.

`handle.value` is therefore **read-write**: reads are session-scoped (readable from any eval while
the field lives), and writes mutate the store and project.

## Consequences

- Input values gain the durability of session state without a new transport: they persist across the
  projection churn ADR-45/ADR-46/ADR-36 already handle, and reset only on a deliberate canvas
  rebuild.
- ADR-41's invoke POST is amended to carry a field-value snapshot; ADR-36's browser-is-a-projection
  invariant is amended for this one state class (the browser is a second writer for input values).
- The browser projection layer must set live DOM properties (`value` / `checked` / `selectedIndex`),
  not only attributes, when projecting input nodes.
- A maintainer must not "fix" out-of-range dropdown/radio values by discarding them: retention of
  invalid values is the decided behavior, and the `<select>` display divergence is its accepted cost.
- Carrying typed values across a script re-run (identity-overlay reconciliation) is explicitly out of
  scope; adding it later must re-derive re-run identity and reset/retain semantics.
- The control catalog and control signatures are implementation over this model, governed by ADR-35 /
  ADR-41, and are not fixed here.
