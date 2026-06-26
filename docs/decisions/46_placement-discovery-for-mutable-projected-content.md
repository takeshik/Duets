# ADR-46: Placement Discovery for Mutable Projected Content

## Status

Accepted

## Context

ADR-45 made Canvas projection revisioned and incrementally patchable, explicitly to enable
value-changing display content. ADR-45 decides *how* a change is projected to the browser, but not
*how the changed content is found* in the first place. When a piece of already-projected content
must change in place after it was emitted, the server has to locate where that content currently
sits in the authoritative projected state so it can replace just that subtree.

That is not trivial:

- The projected state spans **multiple surfaces** — Canvas trees (ADR-45) and Timeline entry bodies
  (ADR-36) — so the locator must work uniformly across both.
- A single logical piece of content may have been placed in **more than one location**.
- Surrounding siblings may have **shifted** since the content was emitted (later `canvas.add`,
  `canvas.clear`, trim, etc.), so an index captured at emit time would be stale.

The first consumer that needs this is the `ui.slot` mutable handle (a script reassigns
`slot.content`), but the question — and the decision below — is independent of that feature.

## Decision Drivers

- Locate placements correctly after intervening mutations shift sibling indices.
- Work uniformly across surfaces and across multiple placements of the same content.
- Keep the projected render tree the single source of truth; avoid a parallel structure that can
  drift out of sync with it.

## Decision

Tag mutable content with a stable identity, rendered as a **marker element**
(`data-duetspad-slot="{id}"`) wrapping the content, and **locate its placements by searching the
authoritative projected state for that marker** at update time — every Canvas tree and every
Timeline entry body. The marked subtree is replaced and re-projected through the existing per-surface
paths (Canvas via the ADR-45 patch protocol, Timeline via ADR-36 `timeline.update`), rebasing any
interactions under the marker (ADR-41).

The **rejected alternative** is a maintained index mapping identity → placement(s). Search over
authoritative state was chosen because that state is already the single source of truth: an index is
a second structure that must be kept coherent across every mutation (add / clear / set / trim),
whereas search is inherently correct after any shift, supports multiple placements for free, and
needs no invalidation when a placement disappears — a vanished marker is simply not found, so the
update is a no-op. The cost is an O(tree) walk per update, negligible at debug-pad scale.

Identity and mutability live on the **handle, not the render nodes**: an update produces a new
subtree and a new projection rather than mutating a node, preserving the structurally-equatable,
display-only node contract (ADR-35) that the ADR-45 differ and no-op check depend on.

## Consequences

- `ui.slot` is the first consumer; any future in-place-updatable primitive reuses this identity
  model without a per-surface or per-feature mechanism.
- The marker adds one transparent wrapper element and one path segment to interactions inside it;
  both are accounted for in interaction rebasing.
- A maintainer must not replace marker search with a placement index without re-deriving the
  shift-robustness, multi-placement, and no-invalidation properties it provides — nor make render
  nodes mutable to support in-place updates.
- The search does not descend into a marker it has matched, so for a given identity only the
  outermost occurrence in any branch is updated; content nested inside its own marker is matched once.
- The `data-duetspad-slot` attribute is the fixed locator contract. The wrapper's tag and styling
  (and the per-surface update mechanics) are implementation governed by ADR-45 / ADR-36 / ADR-41 and
  the slot implementation and tests, not fixed here.
