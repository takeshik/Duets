# ADR-40: DuetsPad Default Object Classification Policy

## Status

Accepted

## Context

ADR-35 defines the DuetsPad render-node model and an `IObjectRenderer`
pipeline with a default renderer fallback, but deliberately leaves the default
renderer's behavior for ordinary values unspecified. This ADR fixes one part of
that gap: how the default renderer *classifies* a value before presenting it.

The default renderer classifies values by the CLR type it receives. That CLR
type is sometimes an accident of engine marshaling rather than the author's
intent. The clearest case: a JavaScript object literal such as
`({ foo: 'abc' })` is marshaled by Jint to a dictionary-like dynamic value
(`System.Dynamic.ExpandoObject`), which is enumerable as key/value pairs. A
CLR object with the same conceptual shape — a thing with named members — is a
plain object. Classifying by the marshaled CLR type therefore treats the same
authoring intent as two different kinds of value.

ADR-32 positions DuetsPad as "LINQPad embedded in your app". LINQPad is the
motivation for wanting rich, shape-aware output, but the goal here is the
*classification policy*, not adopting LINQPad's concrete presentation.

## Decision Drivers

- Classify by conceptual shape, not by accidental CLR marshaling
- The same authoring intent should receive the same classification
- Distinguish a genuine key→value map from a named-member object
- Keep concrete presentation out of the ADR so it can evolve in code and tests

## Considered Alternatives

### A: Classify by the marshaled CLR type (status quo)

- Pro: no renderer changes
- Con: the same authoring intent is classified inconsistently, because a JS
  object literal arrives as a dictionary-like CLR value while a CLR object does
  not

### B: Classify a JS object literal as a map

- Pro: matches the dictionary-like CLR value it marshals to
- Con: a script author's `{ ... }` is conceptually a record of named members,
  not a key→value map

### C: Classify a JS object literal as a named-member object

- Pro: the same authoring intent is classified the same way
- Pro: genuine maps remain a distinct classification
- Con: the renderer must recognize the dynamic JS-object shape and distinguish
  it from genuine maps

### D: Fix concrete presentation shapes in this ADR

- Pro: maximal precision
- Con: over-specifies; concrete HTML, table/grid/list shapes, and per-type
  formatting are volatile and belong to implementation and tests, not an ADR

## Decision

Choose **Alternative C**. The default renderer classifies a value by its
conceptual shape, not by the CLR type that engine marshaling happens to
produce:

- A **JS object literal** is classified as a **named-member object** even though
  it is marshaled to a dictionary-like dynamic CLR value
  (`System.Dynamic.IDynamicMetaObjectProvider`, e.g. `ExpandoObject`). It is
  classified the same way as an ordinary CLR object, and converges with it.
- A **genuine map** — a CLR `IDictionary` / `IDictionary<,>` /
  `IReadOnlyDictionary<,>`, or a JS `Map` — is classified distinctly from a
  named-member object.

Concrete presentation is **not** fixed by this ADR. The HTML structure, element
and class names, the choice of table / grid / list shape, and per-type
formatting (`null`, enums, nesting, numeric alignment) are the responsibility of
the implementation and its tests. Where the boundary between *named-member
object*, *map*, and *collection* is ambiguous for a particular CLR shape (for
example a bare `KeyValuePair` sequence), the implementation chooses and
documents a fallback in code and tests — not here.

## Rationale

Classification should track authoring intent, not the engine's marshaling
artifacts; otherwise the same `{ ... }` an author writes is rendered as one kind
of value in isolation and a different kind inside a collection. Keeping concrete
presentation out of the ADR lets the markup and styling evolve, and confines
brittle formatting decisions to tests.

The one genuinely contestable call is whether a JS object literal is an
"object" or a "map". It is resolved toward "object" because a script author's
`{ ... }` is a record of named members; the dictionary-like marshaled
representation is an implementation detail of the engine boundary. Authors who
want map semantics use `Map` / `IDictionary`, which keep the map classification.

## Consequences

- **Positive**: the same authoring intent is classified the same way, in
  isolation and within collections
- **Positive**: concrete presentation can change in code and tests without ADR
  churn
- **Negative / trade-offs**: the renderer must recognize the dynamic JS-object
  shape and distinguish it from genuine maps; CLR shapes that sit on the
  boundary require a documented fallback in the implementation
- **Negative / trade-offs**: classification policy alone does not promise
  LINQPad parity, consistent with ADR-35
