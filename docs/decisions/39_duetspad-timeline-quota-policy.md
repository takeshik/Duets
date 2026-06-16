# ADR-39: DuetsPad Timeline Quota Policy

## Status

Accepted

## Context

ADR-36 makes DuetsPad Timeline state server-canonical and defines `timeline.trim`
as a protocol event ("removal of entries before a boundary, optionally with a
retained marker entry"). It deliberately did **not** define a quota policy:
*when* the server trims, *what* the boundary is, or *whether* a marker is
retained. That decision was left to "the Timeline contract built on top of this
protocol".

The gap now has a concrete cost. The Timeline is ordered structured history and
grows on every `dump`, evaluation result, console line, and render-error marker.
Nothing bounds it. A long-running host process driving DuetsPad accumulates
Timeline entries indefinitely, growing the server-canonical state (and every
reconnecting subscriber's `timeline.reset` snapshot) without limit. ADR-36
anticipated exactly this ("Timeline quota trimming … have stable verbs") but the
mechanism was never built.

This ADR defines the quota policy that drives `timeline.trim`.

## Decision Drivers

- Bound server-canonical Timeline memory for long-lived sessions
- Preserve the most recent, most relevant history when trimming
- Keep the default behavior unchanged for existing embedders (no surprise data loss)
- Keep `timeline.append` cheap; trimming must not force full snapshots
- Keep entry identity stable so the browser projection (ADR-36) converges
  correctly across append and trim
- Avoid scope creep into marker rendering, which has no UI consumer yet

## Considered Alternatives

### A: No quota (status quo)

- Pro: simplest; no new option or trim logic
- Con: Timeline grows unbounded; long-running sessions leak memory
- Con: leaves the `timeline.trim` protocol verb permanently unused

### B: Byte-size quota

- Pro: bounds actual memory more directly than a count
- Con: render-node trees have no cheap, stable size measure; cost estimation is
  fuzzy and backend-dependent
- Con: the trim boundary is `removeBeforeId` (an entry id), so a size budget
  still has to be translated into a count of entries to drop — extra machinery
  for a fuzzier guarantee

### C: Time-based eviction (drop entries older than a duration)

- Pro: matches a "recent history" mental model
- Con: a quiet session keeps a fixed wall-clock window that may still be
  unbounded in entry count during a burst; a busy session may drop entries the
  user is actively reading
- Con: needs a clock and a background sweep on the Timeline, duplicating the
  idle-sweep machinery from ADR-38 for a weaker memory guarantee

### D: Entry-count quota, drop-oldest, server-applied, default unlimited

- Pro: a hard, predictable bound on entry count
- Pro: the boundary maps directly onto `removeBeforeId` — the id of the first
  retained entry
- Pro: trimming the oldest entries preserves the most recent history, which is
  what a debug surface is read for
- Pro: `null` default leaves every existing embedder unchanged
- Con: count does not bound per-entry size, so a few very large entries can
  still be heavy (acceptable; size quota is alternative B's harder problem)

## Decision

Choose **Alternative D**.

DuetsPad applies a Timeline quota with the following policy:

- **Unit**: maximum number of retained entries, configured by
  `DuetsPadServiceOptions.TimelineEntryLimit` (`int?`).
- **Default**: `null` means unlimited; existing behavior is unchanged. A
  non-`null` value must be positive.
- **Eviction**: drop-oldest. After an append that pushes the entry count above
  the limit, the server retains the most recent `limit` entries and removes the
  rest.
- **Server-applied, append-then-trim**: the server applies the quota as part of
  the same ordered state transition as the append. Live subscribers observe
  `timeline.append` for the new entry before the corresponding `timeline.trim`.
  The browser is never responsible for deciding what to trim.
- **Boundary**: `timeline.trim.removeBeforeId` is the id of the first
  **retained** entry. Browser projections remove entries with
  `id < removeBeforeId`.
- **No marker**: `timeline.trim.marker` is always `null` in this implementation.
  The protocol slot defined by ADR-36 remains, but no retained-marker entry is
  produced.
- **Id stability**: trimming never reallocates or reuses entry ids. `TimelineState`
  preserves its `NextId` across a trim, so a subsequent append always issues a
  strictly greater id than any previously issued, even for ids that were trimmed
  away.
- **Reconnect**: no special handling. A new or reconnecting subscriber receives
  `timeline.reset` carrying the current (already-trimmed) Timeline, so trimmed
  entries never reappear.

This policy realizes the `timeline.trim` verb ADR-36 reserved; it does not change
the protocol or its event names.

## Rationale

Entry-count drop-oldest (D) is chosen because it gives a hard, predictable bound
while preserving exactly the history a debug surface is read for — the most
recent output. The boundary the protocol already speaks in is an entry id
(`removeBeforeId`), so a count maps onto it directly, whereas a byte budget (B)
or a time window (C) must still be converted into "how many entries to drop"
while offering only a fuzzier or weaker guarantee. B's size measurement over
backend-dependent render-node trees is not cheap or stable, and C reintroduces a
clock and a background sweep (already paid for idle reclamation in ADR-38) for a
bound that is not actually on memory.

`null`-as-unlimited keeps the option opt-in: no existing embedder silently starts
losing Timeline history. Treating non-positive configured limits as invalid
turns a meaningless configuration into an immediate configuration error rather
than a confusing runtime behavior where every append would trim to nothing.

Server-application with append-then-trim ordering follows directly from
ADR-36's server-canonical model: the server owns the decision and the browser is
a projection. Sending `append` before `trim` preserves a simple ordered
observation for live subscribers, and a late subscriber observes neither out of
order because it instead gets a `timeline.reset` of the post-trim state.
Preserving `NextId` across trims keeps entry identity monotonic, which is what
lets the browser projection converge without ambiguity — a reused id would make
`timeline.update` and `timeline.trim` boundaries ambiguous.

Keeping `marker` always `null` is a deliberate scope boundary. The protocol slot
exists (ADR-36) and can be filled later when a UI consumer for a "history
trimmed here" affordance exists; producing marker entries now would add server
state and rendering concerns with nothing to display them.

## Consequences

- **Positive**: server-canonical Timeline memory is bounded for long-running
  sessions when a limit is configured
- **Positive**: the `timeline.trim` protocol verb reserved by ADR-36 is now
  actually exercised by a real flow
- **Positive**: default behavior is unchanged; the quota is strictly opt-in
- **Positive**: monotonic, never-reused entry ids keep the browser projection
  unambiguous across append, update, and trim
- **Positive**: non-positive configured limits are rejected as configuration
  errors instead of producing surprising trim-all behavior
- **Negative / trade-offs**: an entry-count limit does not bound per-entry size;
  a few very large entries can still be heavy. A byte-size quota (alternative B)
  can be layered on later if needed
- **Negative / trade-offs**: trimmed history is gone from server state and
  cannot be recovered by reconnecting; there is no retained marker to indicate a
  trim occurred until the `marker` slot is implemented
