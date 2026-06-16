# ADR-42: DuetsPad `pad` Control Surface and Imperative Command Channel

## Status

Accepted

## Context

DuetsPad scripts run server-side on the Jint engine; the browser is a projection
(ADR-34, ADR-36). Scripts can already produce output — `dump` appends to the
Timeline, `canvas.*` mutates the Canvas, `ui.*` builds elements (ADR-35) — but
they cannot operate the pad itself: reset the current session, open a new tab
with a script as its initial content, or replace the editor contents. These are
browser actions,
and the script that wants them runs on the server.

The server-canonical output protocol (ADR-36) carries declarative state
projection: the server owns Canvas and Timeline state and the browser renders
whatever the server holds. Operating the pad is a different nature — the server
must tell the browser to *do* something (swap sessions, open a tab, set editor
text), not merely reflect state. DuetsPad has had no channel for server-to-browser
commands.

Reset has a specific wrinkle. An engine cannot dispose itself in the middle of the
very evaluation that requested the reset. A synchronous server-side engine swap
during that eval is fragile and still has to coordinate with the browser (panes,
stream, editor) regardless.

`dump` and the rest of core Duets are deliberately UI-agnostic (ADR-35): pad
operations are a DuetsPad concern, not a core scripting one.

## Decision Drivers

- Let scripts operate the pad (reset, open, set editor) through one coherent surface
- Keep core Duets UI-agnostic; pad operations belong to DuetsPad
- Respect that the server runs the script while pad actions execute in the browser
- Avoid an engine disposing itself mid-eval
- Reuse the existing one-way-SSE-out plus POST-in shape (ADR-36, ADR-41) rather
  than adding a transport
- State honest contracts: do not promise browser behavior the browser cannot
  guarantee (popup blocking; the absence of a live server-side editor buffer)

## Considered Alternatives

### Command transport — A: `control.*` events on the existing multiplexed stream (chosen)

- Pro: reuses ADR-36's per-session stream; no new transport or connection-state model
- Con: the stream was purely declarative state projection; control is its first
  imperative use, so the "browser is a projection" framing gains one exception

### Command transport — B: a separate command stream, WebSocket, or POST polling

- Pro: keeps the projection stream purely declarative
- Con: adds a second transport (and, for WebSocket, a connection-state model) for a
  need the existing per-session stream already serves one-way

### Reset execution — C: synchronous server-side engine swap

- Pro: reset would be immediate within the script
- Con: the eval calling reset still runs on the engine being swapped; a
  self-disposing engine mid-eval is fragile, and the swap must coordinate with the
  browser (panes, stream, editor) anyway

### Reset execution — D: browser-driven swap (chosen)

- Pro: the eval completes normally, then the browser tears down and recreates the
  session through the existing create/delete routes; no engine self-disposal
- Con: reset is eventually-consistent rather than synchronous in script

### Command flush timing — E: flush after the run, holding the eval gate (chosen)

- Pro: deferring commands until the run finishes keeps the run's own side effects
  (dump, canvas) coherent, and holding the eval gate during the flush prevents a
  reset from racing the next run
- Con: commands are not observable mid-run

### Command flush timing — F: emit each command immediately on the pad call

- Con: a reset could fire while its own run is still producing output, and commands
  could interleave with a following run

### `openText` contract — G: guarantee a new tab opens

- Con: `window.open` from a non-gesture SSE handler is normally popup-blocked;
  promising "a tab opens" is a contract the browser breaks by default

### `openText` contract — H: present an open action (chosen)

- Pro: best-effort `window.open`, with a non-blocking toast fallback whose user
  click (a real gesture) opens the handoff tab — an honest contract under popup
  blocking

### Editor text — I: provide a getter and setter

- Con: the server holds no live editor buffer (editor content is client-side), so a
  synchronous getter would be a false contract

### Editor text — J: set-only (chosen)

- Pro: `setEditorText` matches what the server can actually drive; no getter advertises
  a value the server does not have

## Decision

Introduce a `pad` script global — the DuetsPad host-command surface — and a
`control.*` imperative command channel on the multiplexed per-session stream
(ADR-36).

`pad` methods:

- `resetSession()`: reset the session (engine, Canvas, Timeline)
- `openText(text)`: open a new tab whose editor is handed `text` as its initial content
- `setEditorText(text)`: replace the editor contents
- reserved for the script-persistence work and not surfaced now: `load` / `save`
  and `openFile`
- no editor-text getter

Each `pad` call returns `void` and enqueues a control command on the session.
Commands are buffered during the run and flushed after `EvaluateAsync` and
`InvokeInteractionAsync` complete, while the eval gate is still held, so no command
interleaves with the next run. Commands are therefore eventually-consistent, not
synchronous within the script. Per-command collapse:

- `resetSession` and `setEditorText` are last-wins within one run (a pending command
  of the same op is replaced)
- `openText` appends one command per call, because opening N tabs is N distinct side
  effects

A control event carries an op and a payload and is serialized on the same stream as
the surfaces (`control.<op>`), demultiplexed by the browser by event-type prefix.

**Reset is browser-driven.** On `control.reset` the browser closes the current
stream, deletes the old session, creates a new one — taking the new session id from
the create response, never from the about-to-close stream — repoints session
storage, clears the Canvas and Timeline panes, and re-subscribes, leaving the editor
pane untouched (editor content is client-side and not session state). There is no
page reload, and the menu reset and the scripted reset share one swap path. Because
session identity coincides with stream identity (ADR-36), an event buffered for the
old session cannot reach the new one, so no generation id is required, provided the
browser closes the old stream during the swap (including on a stream error observed
mid-swap).

**`openText` presents an open action.** The text is handed to the new tab
through a one-shot client-side key; the new-tab URL carries only an opaque handoff
id, never the text body. The browser attempts `window.open` best-effort and, when it
is blocked, shows a non-blocking toast whose user click — a real gesture — opens the
handoff tab. The new tab consumes the handoff once on load and then falls back to its
normal editor-restore behavior.

`dump` stays UI-agnostic in core; `pad` is DuetsPad-only, alongside `canvas` and
`ui`.

## Rationale

Reusing the per-session stream for `control.*` keeps the transport story singular —
canonical state and now commands flow out over one SSE stream, commands and
interactions come back over plain POST (ADR-41). The cost is conceptual: the stream
is no longer purely a projection. That is acceptable because the command channel is
small, explicitly namespaced, and still strictly server-to-browser; it does not turn
the browser into an authority over any state.

Browser-driven reset sidesteps the self-disposal problem directly: the engine never
has to tear itself down mid-eval, because the teardown is the browser recreating the
session afterward through routes that already exist. The eventually-consistent
semantics are honest — the script cannot, and should not pretend to, observe its own
session being replaced.

Flushing after the run under the eval gate is what makes "reset then more script
output" well defined rather than racy: output produced during the run reaches the
(old) Timeline, and only then does the post-run reset swap the session. Holding the
gate across the flush keeps the next run from observing a half-applied command set.

`openText` and `setEditorText` are shaped by what the browser and server can
actually honor. Promising a tab will open, or that the editor's live text can be
read back synchronously, would be contracts the system cannot keep; the chosen shapes
(present-an-open-action, set-only editor text) state only what is true.

## Consequences

- **Positive**: scripts can operate the pad (reset, open, set editor) through one
  surface, without a separate API or transport
- **Positive**: reset never disposes an engine mid-eval and needs no session
  generation id, because session identity already coincides with stream identity
- **Positive**: command ordering against runs is well defined (buffer, then flush
  under the eval gate)
- **Positive**: core Duets stays UI-agnostic; pad operations live in DuetsPad
- **Negative / trade-offs**: the projection stream gains an imperative `control.*`
  use, the one exception to "the browser is a projection"
- **Negative / trade-offs**: pad operations are eventually-consistent; a script
  cannot observe their effect within the same run
- **Negative / trade-offs**: `openText` cannot guarantee a tab opens under popup
  blocking and depends on a toast fallback
- **Negative / trade-offs**: there is no editor-text getter, so reading current
  editor content is not available to scripts
