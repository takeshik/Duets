# ADR-45: DuetsPad Canvas Incremental Patch Protocol

## Status

Accepted

## Context

ADR-36 makes Canvas server-canonical: the server holds the canonical Canvas tree
and projects it to the browser over the single multiplexed per-session SSE stream.
Every Canvas mutation broadcasts a `canvas.replace` event whose `state` field
carries the **entire** canvas tree (ADR-43 keyed the event by canvas `name`). The
browser applies `canvas.snapshot`/`canvas.replace` by tearing the panel down
completely — `panel.textContent = ""` — and rebuilding the whole subtree from the
projected nodes, then re-running interaction wiring (ADR-41).

This whole-tree-every-time model has two costs that block value-changing display
components (such as a progress indicator whose value updates over time):

1. **Wire traffic.** Each mutation re-serializes and re-sends the full canvas tree,
   including every node unrelated to the change. For an accumulating canvas — the
   common `canvas.add` loop — the tree grows unbounded and each append re-sends all
   prior content, so a session of *n* appends transfers O(*n*²) bytes. A
   value-driven component updated at any frequency multiplies the full canvas size
   by the number of updates. The waste is in the *unchanged* part of the tree, not
   in any per-message overhead.

2. **DOM churn.** Full teardown discards DOM identity: focus, scroll position, and
   other transient view state are lost, interaction handlers are re-bound on every
   update, and — critically for value-changing components — CSS transitions and
   animations restart from scratch every frame, so a Tabler progress bar cannot
   animate smoothly toward a new value; it jumps.

The current model is acceptable for a small, low-frequency debug canvas, which is
what DuetsPad has been so far. It stops being acceptable the moment a component's
*value* is meant to change over the life of a render. That is exactly what value-
changing components and the broader "live update" direction require, so the
projection mechanism — not the component catalog — is the thing to decide first.

Two properties of the existing design are relevant to the diff strategy:

- **The render tree is structurally equatable.** The current implementation uses
  sealed records (`Element`, `Text`, `RawHtml`) and sealed classes with
  `IEquatable<T>` (`ElementAttributes`, `ElementChildren`), providing value-based
  structural comparison. A differ can compare any two trees by value using this
  infrastructure. Structural sharing (same-instance subtrees from `canvas.add`)
  provides a **fast-path shortcut** — `ReferenceEquals` prunes whole subtrees in
  O(1) — but is not the correctness foundation; value equality is. If the
  implementation of these types changes, the differ's structural comparison
  contract must be preserved.
- **Nodes are already addressable by path** (ADR-41): the interaction model
  addresses handlers by `DisplayPath` (root plus child indices). The same path
  scheme can target patch operations, so this ADR introduces no second addressing
  model.

## Decision Drivers

- For recognized incremental cases (append, value change), avoid sending unchanged
  node-tree payload on the wire. Full-tree fallback remains available for cases the
  diff cannot express efficiently.
- Preserve DOM identity across positional-identity-preserving updates (append,
  value change) so CSS transitions animate and focus/scroll survive.
- Keep ADR-35's immutable, display-only node tree and ADR-36's server-canonical
  projection, single multiplexed stream, and deterministic initial-burst order.
- Reuse ADR-41's `DisplayPath` addressing rather than inventing a parallel one.
- Keep reconnect and initial-burst correct: a fresh subscriber still needs a full
  baseline, and snapshot-then-patches must converge to the same DOM as a single
  full replace.
- Run within the existing `_stateLock` (ADR-34); introduce no second lock.
- Preserve ADR-35's security invariant: only `rawHtml` nodes may use `innerHTML`;
  every other DOM mutation goes through typed DOM APIs.
- Bounded complexity — degrade to the existing full-replace path rather than grow a
  fragile diff for rare structural edits.

## Considered Alternatives

### A: Status quo — full snapshot + client full-replace

- Pro: simplest; already implemented and correct.
- Con: re-sends the whole tree on every mutation (O(*n*²) over an accumulating
  canvas); tears down DOM identity, so CSS transitions restart and focus/scroll are
  lost. Value-changing components cannot be done well. This is the model being
  superseded for incremental updates.

### B: Client-side diff of full snapshots (morphdom-style)

The server keeps sending the full tree; the browser diffs the new tree against the
live DOM and morphs in place instead of tearing down.

- Pro: fixes DOM churn — CSS transitions animate, focus/scroll/interactions survive;
  purely a client change, no wire-schema change.
- Con: **does not reduce wire traffic at all** — the full tree, including every
  unchanged node, is still serialized and sent on every mutation, and the O(*n*²)
  accumulation cost remains. It addresses only half the problem, and not the half
  the traffic concern is about.

### C: Server-side diff — conservative positional patch protocol

The server keeps the last-projected canvas state per name, diffs the new state
against it, and broadcasts a new `canvas.patch` event: an ordered list of
operations addressed by `DisplayPath` (set/remove attribute, replace text, replace
node, insert/remove child). The browser applies the operations to the existing DOM
in place. `canvas.snapshot` and `canvas.replace` (full tree) are retained for the
initial burst, reconnect, and as a fallback.

The diff uses **positional structural reconciliation**: same-type nodes at the same
position are compared structurally to find attribute and text differences;
different-type nodes trigger a `replace-node` operation. Reference equality
(`ReferenceEquals`) serves as a **fast-path shortcut** — when the old and new
subtrees are the same instance (as happens after `canvas.add`, which reuses
existing child instances), the differ prunes the subtree in O(1) without value
comparison.

The worst-case diff cost is O(tree size). The wire payload and DOM mutations are
proportional to what the positional diff identifies as changed. For the target use
cases — tail append and single-element value change — the diff produces minimal
output (a single insert-child or a few attribute ops). For mid-list insert/remove
or reordering, the positional diff may identify the entire suffix as changed, which
is not semantically minimal but is correct; the fallback threshold catches
excessively large diffs.

- Pro: for append and value-change cases, only the changed nodes are on the wire;
  DOM identity is preserved so CSS transitions animate; reuses `DisplayPath`; runs
  under the existing lock.
- Con: adds a `canvas.patch` event type and schema (a second amendment to ADR-36's
  canvas event family after ADR-43), a server-side differ, a per-name last-
  projected state, and a client patch applier. Positional child reconciliation
  produces suboptimal (suffix-wide) diffs for mid-list structural edits.

### D: Append-only delta for `canvas.add`

Add a `canvas.append` event carrying only the newly appended node; leave everything
else as full `canvas.replace`.

- Pro: trivially removes the O(*n*²) cost of the common append loop with almost no
  machinery.
- Con: covers only append. It does nothing for changing an *existing* node's value —
  the live-update case that motivates this ADR. It is a strict subset of what C
  produces (C emits an insert-child op for the same case).

### E: Hybrid — server patch intent + client DOM reconciliation library

The server computes and sends patch operations (as in C), but the client applies
them by feeding the projected post-patch subtree to a DOM reconciliation library
(e.g. morphdom) instead of a custom patch applier with explicit operation ordering.

- Pro: reduces client implementation surface — no custom preflight validation, no
  manual index arithmetic, no canonical ordering rules. The library handles node-
  type changes, attribute diffing, and child reordering automatically.
- Con: the library's reconciliation behavior becomes an implicit protocol
  dependency — its diffing heuristics, node-identity rules, and mutation order are
  not specified by this protocol and may change across versions. Fine-grained
  atomicity guarantees (preflight-then-mutate) are lost; the library may leave the
  DOM partially updated on failure. The security invariant (no `innerHTML` except
  for `rawHtml` nodes) is harder to audit through a third-party abstraction. For
  DuetsPad's small trees, the custom applier is straightforward enough that the
  library dependency adds coupling without commensurate benefit.

### F: Mutation-boundary semantic events (record intent at call site)

Instead of diffing the old and new trees after the fact, record the mutation intent
at each `canvas.add` / `canvas.set` / value-update call site: `canvas.add` emits
an "append" event, `canvas.set` emits a "replace" event, a value change emits
"set-attr" / "replace-text" directly. No tree diff is needed.

- Pro: no after-the-fact diff cost. No reference-sharing or structural-equality
  dependency. Each mutation produces a single, unambiguous intent record.
- Con: the current API surface (`CanvasState.Append`, `CanvasState.Set`) operates
  at the tree level, not the attribute/text level. Value-changing components would
  require new fine-grained mutation APIs (e.g. `canvas.setAttr(path, name, value)`)
  that leak DOM structure into the scripting API, violating the separation between
  authoring-level components and projection-layer DOM details. `canvas.set`
  (replace all children) cannot be decomposed into fine-grained events without a
  diff anyway — it would fall back to "replace all" for any non-trivial structural
  change. The approach also couples the event schema to the mutation API shape;
  adding new mutation methods would require new event types. C's after-the-fact diff
  is decoupled from the authoring API and handles all mutation patterns uniformly.

## Decision

Adopt **C: a server-side positional structural diff and a `canvas.patch` event**,
keeping full snapshots as the baseline and fallback. The append case (D) is not a
separate mechanism; it is the single insert-child operation the differ naturally
produces.

Concretely:

- **Event schema, revision model, and server projection boundary.** The existing
  event types (`canvas.snapshot`, `canvas.replace`) retain their semantics; their
  payloads gain a `revision` field (a monotonically increasing integer scoped per
  canvas name). A new `canvas.patch` event type is added (a second amendment to
  ADR-36's canvas event family after ADR-43). All three event types carry a canvas
  `name` and `revision`; `canvas.patch` additionally carries `baseRevision` and an
  ordered operation list; all three carry the full post-event interaction set for
  that canvas.

  The server maintains a per-canvas **last-projected state**: the canonical record
  of the node tree, committed interaction set (including delegate references and
  handler IDs), and revision for that canvas. Each canvas is initialized with
  `CanvasState.Empty`, an empty committed interaction set, and revision 0. This
  state is shared across all subscribers and is what the server delivers to new
  subscribers via `canvas.snapshot` and to resyncing clients via the resync
  response. Revisions are **contiguous**: each non-no-op broadcast advances the
  revision by exactly one (`revision == baseRevision + 1` for every `canvas.patch`
  event).

  **Interaction set invariant.** The post-event interaction candidate for a
  canvas is keyed by `(DisplayPath, event)` — at most one entry per key. The
  server validates this invariant **before no-op comparison and before handler
  commitment**. The current
  `DisplayContent` composition model (each child's interactions are prepended
  with the child's index via `PrependPath`) ensures path disjointness across
  sibling subtrees by construction, so duplicates do not arise through normal
  API usage. If a future API change or custom construction produces duplicate
  keys, the server **rejects the rendered content before committing handlers or
  mutating canvas projection state**. Duplicate interactions are treated as a
  projection validation failure for that canvas mutation: no handler IDs are
  allocated, no existing handlers are released, the node tree and last-projected
  state remain unchanged, the revision is not advanced, and no canvas event is
  emitted. The implementation may report the failure through the existing
  render-error/logging path, but it must not publish a partial canvas update.
  The no-op comparison, wire serialization, and client binding (which expects
  one handler per `(DisplayPath, event)` pair) all rely on this uniqueness.

  For Canvas events governed by this ADR, the post-event interaction set is the
  **current visible Canvas binding set**: every entry is `Live`. Retired Canvas
  handlers are absent from the set rather than projected as `Stale`; replacing
  or clearing a Canvas releases those handlers server-side. A future protocol
  that projects Canvas `Stale` entries would need to include interaction state in
  the no-op comparison and in the client reconciliation key/value contract.

  **Interaction projection construction.** Each canvas mutation type constructs
  the **post-event interaction candidate** differently, reflecting the distinct
  interaction lifecycles of the existing `InteractionStore` API:
  - **Append** (`canvas.add`): The candidate is the last-committed interaction
    set concatenated with the newly pending interactions from the appended
    content (each target prepended with the appended child's index, matching
    `AppendCanvasInteractions`). Existing committed handlers are preserved —
    they are not released or re-registered; their handler IDs and delegate
    references remain in the committed set. Only the new entries are committed
    (handler IDs allocated). Because append always changes the tree (a child
    is added, increasing child count), the no-op check on the tree dimension
    always fails, and append always proceeds to commitment and event emission
    without reaching the interaction comparison.
  - **Replace** (`canvas.set`): The candidate is entirely from the new
    content's pending interactions (each target prepended with childIndex 0,
    matching `SetCanvasInteractions`). On commit, all previously committed
    handlers for that canvas are released and new ones are registered. The
    no-op check compares this candidate against the last-committed set.
  - **Clear** (`canvas.clear`): The candidate is empty. On commit, all
    previously committed handlers for that canvas are released (matching
    `ClearCanvasInteractions`). The no-op check compares against the last-
    committed set (if both tree and interactions are already empty, it is a
    no-op).

  **No-op check.** On each canvas mutation, the server performs a no-op check
  under `_stateLock` **before committing interactions** (before handler ID
  allocation). Before comparing, it constructs the post-event interaction
  candidate for the mutation type and validates the candidate invariants:
  `(DisplayPath, event)` uniqueness and Canvas interaction state. Existing
  committed entries retained in an append candidate must be `Live`; pending
  entries have no handler ID or state yet, but if committed they will become
  `Live` entries. A candidate that fails validation is rejected as described
  above and does not proceed to comparison. Once the candidate is valid, the no-op
  check compares two dimensions against the last-projected state:
  1. **Node tree**: value equality via the existing structural comparison
     (`CanvasState.Equals`).
  2. **Interactions**: the validated post-event interaction candidate compared
     against the last-committed interaction set by `(DisplayPath, event)` key.
     For the **append** mutation type this comparison is never reached (the tree
     dimension always fails). For **replace** and **clear**, the key sets must
     match exactly, and for each matching key the `Action` delegate in the
     candidate must be reference-equal (`ReferenceEquals`) to the delegate stored
     in the last-committed entry. For replace, the candidate contains only the new
     content's pending delegates (no handler IDs yet); the comparison is against
     the last-committed entries' stored delegate references. For clear, the
     candidate is empty; if the last-committed set is also empty, the dimension is
     unchanged. This is reference identity — not behavioral equivalence — because
     the server cannot determine whether two distinct delegate instances produce
     the same effect.

  If **both** dimensions are unchanged, the mutation is a **no-op**: the
  existing committed handlers are preserved (no release, no re-commit, no
  handler ID allocation), no event is emitted, the revision is not advanced,
  and the last-projected state is unchanged. This avoids phantom revision
  bumps from mutations such as a `canvas.set` that passes the same tree
  structure and the same delegate references.

  The delegate-reference comparison is **conservative**: new delegate instances
  (e.g. fresh lambda captures on each render, or Jint interop wrappers that do
  not cache `Action` objects per JavaScript function reference) compare as
  different even when behaviorally identical, producing a revision bump. This
  is safe — it never suppresses a genuine change — but means that typical
  usage patterns (inline arrow functions, re-captured closures) will rarely
  achieve a no-op for interactive content. For non-interactive content (no
  pending interactions), only the tree dimension matters, and the no-op fires
  whenever the tree is structurally unchanged.

  If the tree is unchanged but the interaction candidate differs (different
  delegate references or different `(DisplayPath, event)` key sets), the
  server commits the new interactions per the mutation type's rules (see
  above) and runs the normal patch-vs-replace selection: the diff yields an empty
  operation list, so the empty-operation `canvas.patch` is normally chosen as the
  smaller encoding (replace is emitted only when it is not larger), carrying the
  updated interaction set, and advances the revision by one. This ensures the client
  always receives new handler IDs when handlers change and reconciles its
  bindings. This path is reachable only for **replace** and **clear** (append
  always changes the tree).

  If the mutation is not a no-op, the server — atomically under `_stateLock` —
  derives the event payload from the prepared new tree and interaction candidate,
  then commits the change:
  1. Computes the event from the prepared new state: the `canvas.patch` operation
     list (via the differ) and the `canvas.replace` full tree, selecting patch
     only when its serialized form is smaller. The payload carries the full
     post-event interaction candidate (with allocated handler IDs) — for
     **append**, the existing committed set plus the new entries; for
     **replace**, the new entries; for **clear**, empty.
  2. Commits interactions according to the mutation type: **append** preserves
     existing committed handlers and commits only the new entries (registering
     the handler IDs via `InteractionRegistry`); **replace** releases all old
     handler registrations and commits all new entries; **clear** releases all
     old handler registrations (committed set becomes empty). In all cases,
     committed entries store delegate references for future no-op comparisons.
     Interaction candidate validation already succeeded before the no-op check,
     so commitment cannot partially register handlers and then discover a
     duplicate key.
  3. Updates the last-projected projection — node tree and revision (incremented
     by one) together — and records the full post-event committed interaction
     set.
  4. Broadcasts the event (`canvas.patch` or `canvas.replace`) with the new
     revision, the operation list (for patch) or full tree (for replace), and
     the full interaction set (serialized from the post-event committed set
     including handler IDs).

  This boundary applies uniformly: every event and every resync
  response carries a consistent triple of (revision, state, interactions). The
  wire-format serialization of the interaction set must be **deterministic**
  (canonical field order, stable entry ordering) so that identical committed
  sets always produce identical wire output. Initial `canvas.snapshot` events
  send the current last-projected state as of lock acquisition.

  A subscriber always starts from a full tree (`canvas.snapshot`), then receives
  ordered incremental events on the same multiplexed SSE stream.

- **Client revision gate.** The client tracks its current revision per canvas.
  The initial state for each canvas is **unset** (no revision yet). For all
  revision-gate comparisons (`revision > current`, `baseRevision == current`,
  etc.), unset is treated as **less than any revision value**; any full-state
  event or resync response satisfies `revision > unset`. While unset:
  - Any `canvas.snapshot` or `canvas.replace` passes the revision gate as the
    baseline candidate; it is applied only after full-state validation succeeds,
    setting the client's revision to the event's `revision`.
  - Any `canvas.patch` is treated as a gap (the client has no base state to patch
    against) and triggers canvas-scoped resync.

  Once a revision is established, the client enforces a **monotonic revision
  gate**. For `canvas.patch`, the client first validates the **contiguous-revision
  invariant**: `revision` must equal `baseRevision + 1`. If this check fails —
  regardless of the relationship between `baseRevision` and `current` — the patch
  is **malformed** (server bug or version mismatch); the client triggers a canvas-
  scoped resync and logs the violation for telemetry. If contiguity holds:
  - **Match** (`baseRevision == current`): apply the patch and advance the
    client's revision to `current + 1`.
  - **Stale** (`baseRevision < current`): silently ignore. Because revisions are
    contiguous, `revision ≤ current`; the update has already been incorporated.
  - **Gap** (`baseRevision > current`): the client missed one or more events.
    Trigger canvas-scoped resync.

  For `canvas.snapshot` / `canvas.replace`: accept only when `revision` is strictly
  greater than the client's current revision. Same or older revisions are silently
  ignored. Accepted full-state events are still applied only after full-state
  validation succeeds.

  Before applying any accepted full-state event (`canvas.snapshot`,
  `canvas.replace`, or a resync response), the client validates the full projected
  tree and the full interaction set. The tree validation uses the same
  well-formedness, DOM API operand, and render-node security-policy rules as patch
  preflight applies to `replace-node` / `insert-child` subtrees, but to the full
  incoming tree. Interaction validation uses the same interaction-set rules as
  patch preflight, with targets resolved against the incoming full tree. If
  validation fails, the client skips the full DOM replace, leaves the existing DOM
  and bindings unchanged, triggers or remains in canvas-scoped resync, and logs
  the malformed full-state payload.

  `canvas.patch` events are not idempotent (insert/remove operations cannot be
  safely re-applied), which is why same-revision patches are never re-applied.
  Malformed patches trigger resync (not just logging) because the client cannot
  trust the revision sequence going forward — silent ignore would leave the client
  permanently stale for that canvas.

- **Canvas-scoped resync.** When the revision gate identifies a **gap** or a
  **malformed violation**, the client requests a **canvas-scoped resync** for that
  canvas. The concrete mechanism
  (subscriber identity, authentication boundary, endpoint shape, retry policy,
  backoff, error surfacing) is **deferred to implementation** or a follow-up ADR
  because it introduces a subscriber-scoped control plane that warrants its own
  focused design.

  **Response shape.** The resync response carries the same payload shape as
  `canvas.snapshot`: canvas `name`, full node tree, full interaction set, and
  `revision`. The `name` field allows the client to match the response to the
  correct canvas when multiple resyncs may be in flight concurrently. The client
  applies the response as a full DOM replace followed by interaction binding
  reconciliation, exactly as it would a `canvas.snapshot`.

  **Ordering invariant.** The resync response is out-of-band (not delivered on the
  SSE stream), so patches may arrive on the stream both before and after the
  response — including patches computed against revisions after the resync
  snapshot. The following state machine handles this:
  1. Once a gap or malformed violation is detected, the client enters a
     **resync-pending** state for that canvas. While resync-pending, incoming
     `canvas.patch` events for that canvas are **buffered** (not discarded),
     because patches computed against post-snapshot revisions will be needed
     after the resync response arrives.
     `canvas.snapshot` and `canvas.replace` events are still subject to the normal
     revision gate and may resolve the gap; if one does (advancing the client past
     the gap), the client exits resync-pending, drains the buffer through the
     normal revision gate, and **logically invalidates** the pending resync
     request — no HTTP cancellation is required; if the response arrives later,
     step 4's revision gate will discard it.
  2. The server takes the resync snapshot under `_stateLock` (the same lock that
     guards the last-projected state), so the snapshot's revision *R* is a valid
     point in the contiguous revision sequence.
  3. The resync response delivers the snapshot at revision *R* to the requesting
     client only (not broadcast) and does not reconnect the multiplexed SSE stream
     (to avoid losing non-replayable `control.*` commands per ADR-42).
  4. The resync response is subject to the **same revision gate** as any full-state
     event: the client applies it only when *R* > current. If *R* ≤ current
     (because a `canvas.snapshot`, `canvas.replace`, or earlier resync already
     advanced the client past *R*), the response is silently discarded. In either
     case, the buffer is then drained through the normal revision gate. If the
     response passes the revision gate but fails full-state validation, the client
     rejects the response without replacing DOM or bindings, keeps the current
     buffer intact, issues a fresh resync request, and remains resync-pending.
  5. On successful application, the client replaces its DOM with the full state,
     reconciles interaction bindings from the response's interaction set, sets
     current = *R*, and drains the buffer. The full-state validation described in
     the revision gate happens before this replacement; a malformed resync
     response is rejected without clearing the current DOM. Buffered patches are
     replayed in arrival order through the normal revision gate (match → apply,
     stale → discard, malformed or gap → step 7).
  6. After the buffer is fully drained without a new gap or malformed violation,
     the client exits resync-pending and resumes normal SSE consumption.
  7. If a **gap or malformed violation** is encountered during buffer drain: for a
     gap, the offending patch and all remaining un-drained patches stay in the
     buffer; for a malformed violation, the offending patch is discarded (its
     broken contiguity will never resolve) and the remaining un-drained patches
     stay in the buffer. In both cases, a new resync request is issued (the
     previous request is already consumed or will be discarded by the revision
     gate) and the client remains resync-pending.

  A malformed `canvas.snapshot` or `canvas.replace` received while resync-pending
  is handled like a malformed resync response: it is rejected without replacing
  DOM or bindings, the buffer is retained, a fresh resync is issued, and the
  client remains resync-pending. Outside resync-pending, a malformed full-state
  event triggers canvas-scoped resync and leaves the existing DOM and bindings
  unchanged. If malformed full-state responses repeat past an implementation-
  defined threshold, the client surfaces an error to the user rather than
  resyncing indefinitely.

  **Buffer bounds.** The resync-pending buffer must be bounded by both **entry
  count** and **total serialized byte size** (whichever limit is reached first),
  because a small number of patches may carry large replacement subtrees or
  interaction sets. When either cap is exceeded, the client discards the entire
  buffer and issues a fresh resync request (remaining resync-pending). The fresh
  snapshot will be at a more recent revision, reducing post-snapshot patch volume.
  If the buffer overflows repeatedly (implementation-defined threshold), the client
  surfaces an error to the user rather than looping indefinitely.

  This state machine guarantees convergence: the resync snapshot is taken at a
  definite point in the contiguous revision sequence, post-snapshot patches are
  preserved in the buffer, and the revision gate on both the resync response and
  each buffered patch prevents backward rollback.

- **Diff strategy: positional structural reconciliation with reference fast path.**
  The server diffs the new `CanvasState` against the per-canvas last-projected
  state. The differ walks the old and new trees recursively:
  - `ReferenceEquals` → skip (fast path; O(1) pruning of shared subtrees).
  - Same node type → compare structurally. For `Element`: compare `Tag` (must
    match, otherwise replace), then diff `Attributes` key-by-key (`set-attr` /
    `remove-attr` ops), then recurse into `Children` positionally. For `Text`:
    compare `Value` (`replace-text` op). For `RawHtml`: compare `Content`
    (`replace-node` op — `RawHtml` is opaque).
  - Different node type → `replace-node`.
  - Children count differs → `insert-child` / `remove-child` at tail.

  The diff cost is O(tree size) worst case (fully rebuilt tree from `canvas.set`).
  For append (`canvas.add`), each existing child subtree is pruned in O(1) via
  `ReferenceEquals`, but the differ must still scan the prefix — so a single append
  costs O(existing top-level children) in server CPU, and *n* sequential appends
  cost cumulative O(*n*²) in diff time. The **wire payload** is O(appended) per
  append (only the new node is sent), which is the primary improvement. An
  implementation may optimize by tracking mutation metadata (e.g. recording that
  `CanvasState.Append` was the last operation) to detect the append case without
  scanning, but this is implementation-level, not a protocol concern. For single-
  element value change, the diff walks the (small) tree and produces a few attribute
  ops. For mid-list insert or reordering, the positional diff identifies the entire
  suffix from the first mismatch as changed — not semantically minimal, but correct;
  the fallback threshold (below) catches cases where the patch would be larger than
  a full replace.

- **Operation set and value types.** Each operation is one of the following:
  - `set-attr { path, name, value }` — `path` is a `DisplayPath` identifying an
    existing `Element` node. `name` is the attribute name (string). `value` is
    `string | null`: a string sets a key-value attribute; `null` sets a boolean
    (valueless) attribute (e.g. `disabled`). This mirrors the existing
    `ElementAttributes` model where values are `string?`.
  - `remove-attr { path, name }` — removes the named attribute entirely. `path`
    identifies an existing `Element` node.
  - `replace-text { path, value }` — `path` identifies an existing `Text` node;
    `value` is the new text content (string).
  - `replace-node { path, node }` — `path` identifies an existing node (any type);
    `node` is the full projected replacement subtree. **Suppresses all other
    operations on the same path or any descendant**: the server omits them, and
    the client preflight rejects any operation whose path equals or is a
    descendant of a replaced path.
  - `remove-child { parentPath, index }` — `parentPath` identifies an existing
    `Element`; `index` is a valid child index in the **pre-remove-phase child
    list** (the parent's children as they were before any `remove-child` ops in
    this patch were applied to this parent).
  - `insert-child { parentPath, index, node }` — `parentPath` identifies an
    existing `Element`; `index` is the insertion position in the **post-remove
    child list** (after all `remove-child` ops for this parent, before any
    `insert-child` ops). Constraint: `0 ≤ index ≤ postRemoveChildCount +
    priorInsertsInSameParent`. `node` is the full projected subtree.

  Note that `remove-child` and `insert-child` operate in **different index
  spaces** (pre-remove vs. post-remove). A `remove-child` index and an
  `insert-child` index with the same numeric value do not necessarily refer to the
  same position; they are not comparable across phases.

- **Canonical ordering, server contracts, and client preflight.** The server emits
  operations in a **canonical order** that the client applies sequentially:
  1. `replace-node`, deepest paths first.
  2. `set-attr`, `remove-attr`, `replace-text` on surviving nodes, in path order.
  3. `remove-child` in **descending index order** within each parent (so each
     removal does not shift subsequent removal indices within the pre-remove-phase
     index space).
  4. `insert-child` in **ascending target-index order** within each parent (so
     each insertion shifts subsequent target indices by +1 within the post-remove
     index space).

  **Server uniqueness contract.** The server must not emit duplicate or conflicting
  operations within a single patch. Specifically:
  - At most one `set-attr` per `(path, name)`.
  - At most one `remove-attr` per `(path, name)`.
  - No `set-attr` and `remove-attr` for the same `(path, name)` (setting and
    removing the same attribute in one patch is contradictory).
  - At most one `replace-text` per path.
  - At most one `replace-node` per path. No two `replace-node` operations where
    one path is an ancestor of the other (the ancestor replacement subsumes the
    descendant; the deeper operation is redundant).
  - No other operation (of any type) targeting the **same path as** or any
    **descendant of** a `replace-node` path. The replacement subtree is the
    complete new state for that position; any additional operation on the same
    node or its children is either redundant or contradictory.
  - At most one `remove-child` per `(parentPath, pre-remove-phase index)`.
  - At most one `insert-child` per `(parentPath, effective insertion position)`,
    where the effective position is the target index as it stands after accounting
    for all prior ascending inserts in the same parent.
  - No `insert-child` whose `parentPath` is the same as or a descendant of a
    `replace-node` path.
  - No `remove-child` whose `parentPath` is the same as or a descendant of a
    `replace-node` path. Additionally, the effective target path of a
    `remove-child` — `parentPath` extended by `index` — must not equal or be an
    ancestor of any `replace-node` path (replacing a node or its descendant and
    then removing the ancestor is contradictory).

  These constraints are structural properties of the diff algorithm (the differ
  walks the tree once, producing at most one operation per target by construction).

  **Client preflight validation.** The client applies a patch **atomically** via a
  two-phase approach. The **preflight pass** walks the operations in canonical
  order as a dry run. It maintains the following tracking state:
  - A **replaced-path set**: paths targeted by `replace-node`. Any operation whose
    path equals or is a descendant of a replaced path is rejected.
  - An **attr-op seen set** of `(path, name)` pairs, covering both `set-attr` and
    `remove-attr`: a duplicate within or across the two operation types is rejected.
  - A **replace-text seen set** of paths: a duplicate is rejected.
  - A **per-parent virtual child count**, decremented by each `remove-child` and
    incremented by each `insert-child`.
  - A **remove-child seen set** of `(parentPath, pre-remove-phase index)` pairs:
    a duplicate is rejected.
  - An **insert-child seen set** of `(parentPath, effective insertion position)`
    pairs: a duplicate is rejected.

  Per-operation checks (each check that passes updates the corresponding tracking
  state; a failure at any point aborts the entire preflight):
  - `set-attr`: node at `path` exists, is an `Element`, path not in or under
    replaced-path set, `(path, name)` not already in attr-op seen set. Adds
    `(path, name)` to attr-op seen set.
  - `remove-attr`: node at `path` exists, is an `Element`, path not in or under
    replaced-path set, `(path, name)` not already in attr-op seen set. Adds
    `(path, name)` to attr-op seen set.
  - `replace-text`: node at `path` exists, is a `Text` node, path not in or under
    replaced-path set, path not already seen for replace-text. Adds path to
    replace-text seen set.
  - `replace-node`: node at `path` exists, path not in or under replaced-path set,
    **and** no existing entry in replaced-path set is a descendant of path (an
    ancestor replacement subsumes the descendant). Adds path to replaced-path set.
  - `remove-child`: parent exists, is an `Element`, parent path not in or under
    replaced-path set, effective target path (`parentPath` extended by `index`)
    not equal to and not an ancestor of any entry in replaced-path set, `index`
    valid against virtual child count, `(parentPath, index)` not already seen.
    Adds `(parentPath, index)` to remove-child seen set. Decrements virtual child
    count.
  - `insert-child`: parent exists, is an `Element`, parent path not in or under
    replaced-path set, `0 ≤ index ≤ virtualChildCount`, subtree well-formed,
    `(parentPath, effective position)` not already seen. Adds `(parentPath,
    effective position)` to insert-child seen set. Increments virtual child count.

  In addition to the structural and uniqueness checks above, the preflight must
  **validate all operand values that will be passed to DOM APIs** during the
  mutation phase, so that no DOM API call can throw during mutation:
  - `set-attr`: `name` is a valid attribute name (non-empty string, no characters
    that would cause `setAttribute` to throw).
  - `remove-attr`: `name` is a valid attribute name (non-empty string, no
    characters that would cause `removeAttribute` to throw).
  - `replace-text`: `value` is a string.
  - `replace-node` / `insert-child`: the projected subtree is recursively well-
    formed — every element has a valid tag name, every attribute has a valid name,
    attribute values are `string | null`, text content is a string, and `rawHtml`
    node content is a string. `rawHtml` nodes within the subtree are projected
    through the existing audited `rawHtml` path (the sole `innerHTML` site).
  - `path` and `parentPath` fields are arrays of non-negative integers.
  - `index` fields are non-negative integers.

  The same preflight pass also validates the **post-patch interaction set**
  carried by the event before any DOM mutation is performed. This validation uses
  the dry-run post-patch tree/DOM shape implied by the operations:
  - The interaction set is an array and contains at most one entry per
    `(DisplayPath target, event name)` pair.
  - Each `target` is a `DisplayPath` array of non-negative integers and resolves
    to an `HTMLElement` in the post-patch tree. A target may point into a subtree
    inserted or replaced by the same patch, but it must be resolvable in the
    dry-run result.
  - Each `event` is one of the interaction events supported by the client
    protocol (currently `click`).
  - Each `handlerId` is a non-empty GUID-format string suitable for the interaction
    invoke endpoint.
  - Each Canvas interaction `state` is `live`. Canvas events represent the
    current visible binding set; retired Canvas handlers are omitted, not
    projected as stale entries.

  The preflight must also enforce the **render-node security policy** as a
  client-side defense-in-depth layer mirroring the server-side validation in
  `Element.cs` and `ElementAttributes.cs`:
  - **Forbidden tags**: `script`, `iframe`, `object`, `embed`, `template`. Any
    element with one of these tags in a `replace-node` or `insert-child` subtree
    is rejected.
  - **Forbidden attributes**: attribute names matching the `on*` event-handler
    pattern (e.g. `onclick`, `onload`). Checked in `replace-node` / `insert-child`
    subtrees and in `set-attr` operands.
  - **Forbidden URL values**: attribute values beginning with `javascript:` (case-
    insensitive, after trimming leading whitespace) on URL-bearing attributes.
    The URL-bearing attribute set is a fixed, closed list shared between server
    and client: `href`, `src`, `action`, `formaction`, `poster`, `srcset`.
    Checked in `replace-node` / `insert-child` subtrees and in `set-attr`
    operands.
  - **Forbidden `srcdoc` attribute**: the `srcdoc` attribute (used on `iframe`, but
    forbidden regardless of tag because `iframe` itself is forbidden). Checked in
    `replace-node` / `insert-child` subtrees and in `set-attr` operands.

  A security-policy violation causes the same response as any other preflight
  failure: the entire patch is skipped, a canvas-scoped resync is triggered, and
  the violation is logged for telemetry.

  If any operation, operand, render-node security, or interaction-set check
  fails, the client **skips the entire patch** (no DOM mutations, no interaction
  rebinding), triggers a canvas-scoped resync, and logs the failure for telemetry.
  Only when all checks pass does the client proceed to the **mutation phase**,
  applying operations in canonical order. This ensures a malformed or stale patch
  never leaves the DOM in a partially updated state.

  Implementers must validate the canonical ordering with **protocol-level test
  examples** covering multi-insert, multi-remove, and mixed insert/remove scenarios
  to ensure server and client agree on index semantics.

- **Interactions re-sent whole; client binding is reconciled.** Each `canvas.patch`
  carries the full post-patch interaction set for that canvas, bound to the same
  `revision` as the node patch. The client applies the interaction set **only after
  successful patch application**; on patch failure (preflight rejection), existing
  bindings are preserved and the failed patch's interaction set is discarded.

  The client **reconciles** handlers against the patched DOM: for each
  (target, event) pair present in the new set, the client replaces any previously
  bound handler; for any pair that was bound previously but is **absent** from the
  new set, the client removes the listener entirely. Binding identity is keyed by
  `(DisplayPath target, event name)` — one handler per pair. The server guarantees
  that the interaction set is unique by this key (see Interaction set invariant
  above), and the client has already validated that uniqueness during preflight.
  This prevents both listener accumulation on surviving DOM nodes and stale
  listeners on nodes whose interactions have been removed.

  The interaction set is small per entry (a handler id, a `DisplayPath` target, an
  event name, and a state flag, which is always `live` for Canvas events under
  this ADR). Full-set re-send means an append loop of interactive nodes produces
  O(*n*²) total interaction records (the *k*-th patch carries *k* records). This
  residual quadratic cost is bounded by the per-record size and is acceptable for
  expected DuetsPad workloads. **Incremental interaction add/remove semantics are
  deferred** — see Consequences.

- **Positional DOM identity, not logical item identity.** The reconciler compares
  children by position. Tail append/remove is handled naturally. Mid-list insert,
  deletion, and reordering cause DOM identity to associate with a different logical
  item at each shifted position — the positional diff treats the entire suffix from
  the first mismatch as changed. The visual result is always correct (all
  attributes and text are patched to their new values), but CSS transitions may
  animate between logically unrelated values, and focus/scroll may remain on the
  wrong logical item. This is acceptable for DuetsPad's current use cases (single-
  element value updates, append-only output). **Stable keys for logical item
  identity are deferred** — see Consequences.

- **Graceful fallback.** When the complete serialized `canvas.patch` event would be
  larger than or equal to the complete serialized `canvas.replace` event for the same
  transition, the server emits `canvas.replace` instead (patch is chosen only when it
  is strictly smaller). The comparison metric is
  the **complete serialized event byte size** — all fields including `name`,
  `revision`, `baseRevision`, operations, and interaction set for `canvas.patch`;
  `name`, `revision`, state, and interaction set for `canvas.replace`. The fallback
  decision is deterministic on the server; the client need not be aware of the
  threshold. The client converges to the identical DOM whether it received a patch
  or a full replace; that parity is a required, tested invariant.

- **Security invariant preserved.** The client patch applier mutates the DOM only
  through typed APIs (`setAttribute`, `textContent`, `createElement`,
  `replaceChild`, ...). It never uses `innerHTML`; the sole `innerHTML` site
  remains `rawHtml` node projection (ADR-35). A `replace-node` op containing a
  `rawHtml` node routes through the same single, audited `rawHtml` projection path.
  The client preflight enforces the **render-node security policy** (forbidden
  tags, `on*` attributes, `javascript:` URLs, `srcdoc`) as a defense-in-depth
  layer mirroring the server-side validation. This ensures that even a malformed
  or compromised patch cannot inject active content into the DOM.

## Rationale

B was rejected because it leaves the traffic problem untouched — the unchanged
tree is still on the wire on every update, which is precisely the waste the
decision must remove. A was rejected as the model being superseded. D is correct
but partial: it cannot express an in-place value change, and C already subsumes
it. E (hybrid with a client-side DOM reconciliation library) was rejected because
the library's reconciliation behavior becomes an implicit protocol dependency,
fine-grained atomicity guarantees are lost, and the security invariant (no
`innerHTML` except for `rawHtml`) is harder to audit through a third-party
abstraction; for DuetsPad's small trees, the custom applier is straightforward. F
(mutation-boundary semantic events) was rejected because the current API operates
at the tree level, not the attribute/text level; value-changing components would
require fine-grained mutation APIs that leak DOM structure into the scripting API,
and `canvas.set` cannot be decomposed without a diff anyway.

C satisfies the primary driver (for append and value-change cases, unchanged node-
tree payload stays off the wire) and the DOM-identity driver (CSS transitions,
focus, and interactions survive on positional-identity-preserving updates). The
differ is a **positional structural reconciliation**: it walks the old and new
trees comparing same-position, same-type nodes by value equality, producing patch
operations for differences. This is not the same algorithm as React's reconciler
(which uses type/key/position heuristics and props-level diffing); it is a simpler
positional diff suited to the small, key-less trees DuetsPad produces.

The worst-case diff is O(tree size), not O(changed), because `canvas.set` with a
fresh tree shares no references with the previous tree. Even the append case costs
O(existing children) per append for prefix scanning (cumulative O(*n*²) in server
CPU). This is acceptable: the diff runs in-memory on the server (no I/O), and the
wire payload — the primary concern — is proportional to what the diff identifies
as changed. For mid-list structural edits, the diff can include the entire suffix,
in which case the fallback threshold prevents sending a patch larger than a full
replace.

Interactions are re-sent whole to avoid the complexity of incremental interaction
bookkeeping under `DisplayPath` index shifts from insert/remove operations. The
client reconciles binding by replacing present handlers and removing absent ones;
surviving DOM nodes never accumulate duplicate or stale listeners. Full re-send
means an append loop of interactive nodes still produces O(*n*²) total interaction
records, but the per-record payload is smaller than node-tree entries, so the
residual quadratic cost is bounded by a smaller constant factor.

The reconciler provides **positional** DOM identity, not logical item identity.
Stable keys were rejected for this ADR: DuetsPad users write quick debug scripts
and cannot reasonably be expected to assign keys. Positional identity is sufficient
for the motivating use cases (value updates, append loops) because positional and
logical identity coincide. The limitation — mid-list insert/remove causes identity
shift — produces correct visual output but incorrect transitions and focus, an
acceptable trade-off for a debug pad.

The contiguous revision model (`revision == baseRevision + 1`) structures the
client gate as a contiguity check followed by a three-case disposition (match /
stale / gap). Contiguity is validated first so that a broken patch — regardless of
how its `baseRevision` compares to `current` — is caught as malformed and triggers
resync rather than being silently classified as stale. When contiguity holds,
`baseRevision < current` necessarily implies `revision <= current`, making stale
detection sound. The initial unset-revision state ensures that a new subscriber
accepts its first valid full-state event as the baseline, and that any
`canvas.patch` arriving before a validated baseline triggers resync rather than
silent discard.

The resync ordering invariant — snapshot under `_stateLock`, client buffers
patches while resync-pending, resync response subject to revision gate, buffer
replayed after application — guarantees convergence without reconnecting the SSE
stream. The bounded buffer (entry count and byte size) prevents unbounded memory
growth during slow resyncs; overflow triggers a fresh resync rather than
degradation. The concrete mechanism is deferred because it introduces subscriber-
identity and control-plane concerns that warrant focused design.

Client-side patch application is atomic via a preflight validation pass: all
operations are dry-run validated — including node-type checks, replace-node
descendant suppression (bidirectional: both ancestor-under-descendant and
descendant-under-ancestor), server-uniqueness-contract violations, DOM API operand
validity, render-node security policy enforcement, and the full post-patch
interaction set — before any DOM mutation. A malformed or stale patch never
produces a partially updated DOM or a DOM/interactions split-brain state.

The no-op check (both node tree value equality AND post-event interaction
candidate delegate reference equality) prevents two failure modes:
(1) suppressing interaction-only changes (e.g. a button re-rendered with a
different callback), which would leave the client with stale handler IDs, and
(2) emitting phantom events for structurally identical mutations with the same
delegate references. The interaction comparison uses delegate reference identity
— not handler IDs or serialized wire format — because handler IDs do not exist
at comparison time (they are allocated during commitment, which happens only
after the no-op check determines the mutation is not a no-op). The comparison is
conservative: distinct delegate instances that happen to be behaviorally
equivalent produce a revision bump, which is safe but not optimal. This means
that interactive content re-rendered with fresh lambda captures will typically
produce a revision bump even when nothing logically changed — an acceptable
overhead for a debug pad. For append, the no-op check is structurally
unreachable (the tree dimension always fails), so interaction commitment always
proceeds in append mode (existing handlers preserved, new entries added).

The interaction projection construction rules (append preserves existing
handlers and adds new entries; replace releases all and re-commits; clear
releases all) reflect the existing `InteractionStore` API's mutation-type-
specific lifecycle. The `(DisplayPath, event)` uniqueness invariant ensures that
the full post-event interaction set — whether built by concatenation (append) or
wholesale replacement (set/clear) — is a valid map from binding key to handler.
This invariant is structurally guaranteed by the `DisplayContent` composition
model (`PrependPath` ensures path disjointness across sibling subtrees) but is
stated explicitly as a server-side contract for defense-in-depth. Duplicate
interaction keys are rejected rather than collapsed with last-wins semantics,
because silently dropping a handler would make no-op comparison, handler
registration, and user-visible interaction behavior depend on construction order.

The server's atomic update (compute the event payload from the prepared state and
interaction candidate, commit interactions per mutation type, update the projected
tree and revision together, broadcast the event) under `_stateLock` ensures that
revision, state, and interaction set always advance as a single boundary. Handler
IDs are allocated for the interaction candidate before the payload is built and
registered within the same locked boundary, so handler IDs in the emitted event are
always consistent with the server's handler registry. This applies uniformly to patch,
full replace, snapshot, and resync: every event or response carries a consistent
triple of (revision, state, interactions).

The canonical operation ordering (replace-node deepest-first, remove descending,
insert ascending) ensures that positional `DisplayPath` addresses remain valid
throughout sequential application without index recomputation. The server
uniqueness contract and client preflight enforcement together guarantee that no
single patch contains conflicting or ambiguous operations.

Retaining full snapshots for baseline, reconnect, and fallback keeps ADR-36's
server-canonical projection and burst ordering intact and bounds the new
complexity: anything the differ cannot express efficiently degrades to the
existing, known-correct full-replace path.

This unblocks value-changing display content: re-projecting changed content is sent
as a positional patch the browser applies in place — so a Tabler progress bar
animates smoothly via its own CSS
transition. The patch protocol is a projection-layer decision; it does not
constrain the component authoring API, which remains a separate concern.

How a value-changing piece of content *locates its place* in the projected state
so it can be updated after the fact — as opposed to *how the change is projected*,
decided here — is a separate decision recorded in
[ADR-46](46_placement-discovery-for-mutable-projected-content.md); `ui.slot` is
its first consumer.

## Consequences

### Positive

- Incremental canvas updates carry only the positional diff in the node tree for
  append and value-change cases.
- The `canvas.add` loop's node-tree wire payload drops from O(*n*²) to
  O(appended).
- Value-changing display components become feasible with smooth CSS transitions for
  positional-identity-preserving updates (value changes, appends).
- Focus, scroll, and bound interactions survive updates on nodes whose positional
  identity is preserved (the motivating append and value-change cases); mid-list
  structural edits may shift positional identity — see Negative / trade-offs.
- `DisplayPath` and the existing `_stateLock` are reused; no new addressing model
  or lock.
- The baseline and initial-burst ordering are preserved. Existing event types
  retain their semantics; payloads gain a `revision` field and the client adds a
  revision gate, but the reconnect flow (full snapshot delivery) is structurally
  the same.
- No new equality infrastructure is needed — existing record types and
  `IEquatable` implementations supply the structural comparison the differ requires
  (implementation dependency, not architectural invariant).
- The contiguity-first revision gate (malformed → resync, then match / stale /
  gap) and the resync ordering invariant ensure self-healing convergence after
  lost or malformed patches without reconnecting the SSE stream or losing
  `control.*` commands.
- Preflight validation (node-type checks, replace-node descendant suppression,
  server-uniqueness-contract enforcement, DOM API operand validity, render-node
  security policy, and interaction-set validation) makes patch application
  atomic — no partial DOM mutations or DOM/binding split-brain state on failure.
  Full-state validation applies the same safety bar before snapshot, replace, or
  resync DOM replacement.

### Negative / trade-offs

- Worst-case server-side diff cost is O(tree size), not O(changed), when
  `canvas.set` rebuilds the tree from fresh factories. Even the append case incurs
  O(existing children) per append for prefix scanning (cumulative O(*n*²) in server
  CPU), though the wire payload is O(appended). An implementation may optimize via
  mutation metadata tracking.
- A new `canvas.patch` event type (a second amendment to ADR-36's canvas event
  family after ADR-43). Existing event types gain a `revision` field (payload
  schema amendment).
- A client patch applier — the only DOM-mutating client path besides `rawHtml` —
  raises the testing and review bar: snapshot/patch/full-replace must converge
  identically, and the applier must never use `innerHTML`.
- The client must implement reconciled interaction binding (registry keyed by
  `(DisplayPath, event)` replacing the current append-only `addEventListener`
  pattern) and validate the full interaction set during patch preflight. One
  handler per (target, event) pair; if multiple handlers per pair are needed in
  the future, the binding identity model will need revision.
- The server holds a per-canvas last-projected state (node tree + committed
  interaction set with delegate references + revision counter) — small extra
  memory. The committed set retains `Action` delegate references for no-op
  comparison, preventing the interactions from being garbage-collected while a
  canvas is active.
- The no-op check for interactions uses delegate reference identity, which is
  conservative: typical JavaScript usage patterns (inline arrow functions, re-
  captured closures) produce new delegate instances on each render, causing
  revision bumps even when nothing logically changed. Non-interactive content is
  unaffected.
- The canonical operation ordering, type-checked preflight with virtual child
  counts and replace-node descendant suppression, server-uniqueness-contract
  enforcement, and canvas-scoped resync add implementation and testing surface.
  Protocol-level test examples for multi-insert, multi-remove, and mixed scenarios
  are required.
- The positional diff produces suboptimal (suffix-wide) patches for mid-list
  insert/remove/reorder. The complete-event-byte-size fallback threshold mitigates
  this.
- The protocol gains three projection paths (snapshot, patch, replace) whose
  convergence parity must be held as a tested invariant.
- Full-state validation re-validates the entire incoming tree and interaction set
  on the client before every snapshot/replace/resync DOM replacement — O(tree
  size) per full-state event. This is acceptable because full-state events are
  infrequent (initial burst, reconnect, resync, fallback) rather than per-mutation,
  but it adds client CPU on those paths and must apply the same well-formedness,
  DOM-operand, security-policy, and interaction-set rules as patch preflight to
  avoid being a validation bypass.
- The initial client preflight implementation may deep-clone the current canvas
  root to enforce the hard atomicity contract before mutating the live DOM. That
  makes append-heavy sessions O(canvas size) per patch on the client, so the wire
  payload improves from O(*n*²) while client CPU can still grow cumulatively
  O(*n*²). This is accepted for DuetsPad's debug-pad scope; a future
  implementation can replace the clone with a virtual DOM/child-count preflight
  model while preserving the same validation and atomicity contract.
- Contiguous-revision violations, lost events, and other malformed patches must be
  logged with telemetry and trigger resync, as they may indicate server/client
  version mismatch rather than transient network issues.

### Deferred

The following items are explicitly deferred to later ADRs or implementation
design:

- **Stable keys for logical item identity.** The reconciler uses positional child
  matching; mid-list insert/remove and reordering cause DOM identity to shift
  across logical items. A later ADR may introduce an optional stable-key mechanism.
  Note: this may require changes to the authoring API, render-node metadata, and/or
  diff policy.
- **Incremental interaction semantics.** Full interaction-set re-send means an
  append loop of interactive nodes produces O(*n*²) total interaction records. A
  later ADR may introduce incremental add/remove/update operations if this becomes
  a measured problem.
- **Handler ID stability for interactive no-op.** The no-op check uses delegate
  reference identity, which is conservative for typical JavaScript usage (inline
  arrow functions produce new delegate instances on each render). If unnecessary
  revision bumps for interactive content become a measured problem, the script-
  engine interop layer could cache `Action` wrappers per JavaScript function
  reference, or the interaction registry could reuse handler IDs for matching
  `(DisplayPath, event, Action)` tuples. Both approaches carry complexity (cache
  invalidation, delegate lifetime management) and are deferred.
- **Canvas-scoped resync mechanism.** The ordering safety invariant, buffer
  management (including dual entry-count / byte-size cap and overflow behavior),
  response shape (`name` + state + interactions + revision), and revision-gate
  applicability to the resync response are decided in this ADR (see Decision). The
  concrete mechanism is deferred: subscriber identity issuance, authentication
  boundary, endpoint shape, POST-vs-SSE delivery, retry policy, backoff, disconnect
  race handling, and error surfacing. Canvas name routing must also be addressed —
  ADR-43 constrains canvas names to non-empty strings but does not restrict
  characters, so URL path-segment routing is unsafe (names containing `/`, `?`, or
  requiring Unicode normalization would be ambiguous); the resync endpoint design
  must either place the name in the request body/query or define URL-safe name
  constraints (potentially as an ADR-43 amendment).
