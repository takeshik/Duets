# ADR-53: Fluent `dump` via Layered Runtime, Typing, and Completion

## Status

Accepted

Supersedes the global-function-only syntax and the prototype/completion conclusions of
[ADR-20](20_dump-as-global-function-not-prototype-extension.md). The DuetsPad ownership and
render options established by [ADR-35](35_duetspad-rendering-model.md) remain unchanged.

## Context

DuetsPad exposes `dump<T>(value, options?): T` so scripts can render an intermediate value to
the Timeline without breaking an expression chain. The global form preserves the input type,
but it is awkward inside a fluent chain: `dump(query.where(...)).select(...)` must wrap the
intermediate expression instead of reading in evaluation order.

ADR-20 rejected `value.dump()` after an earlier implementation combined an
`Object.prototype` method with `interface Object { dump(): this }`. The runtime method worked,
but Monaco's TypeScript completions intentionally omitted members inherited only from
`Object` on concrete inferred types. ADR-20 also considered a custom Monaco completion
provider independently and rejected it because a completion item alone supplies no semantic
return type.

The alternatives were treated as mutually exclusive, but they solve different problems:

- JavaScript owns runtime member dispatch.
- TypeScript declarations own semantic type inference.
- Monaco completion providers own discoverability when the TypeScript worker deliberately
  filters a valid inherited member from its completion list.

Combining those layers allows `value.dump()` to be both discoverable and type preserving.

## Decision Drivers

- Preserve the exact receiver type after `dump()` so chained completions remain available.
- Offer `dump` after member-access completion without replacing Monaco's TypeScript worker.
- Support JavaScript values, primitives, and CLR wrappers exposed through the script runtime.
- Keep the existing global `dump(value, options?)` form for compatibility and nullish values.
- Avoid enumerable prototype pollution and avoid suggesting the method in comments or literals.
- Keep `dump` and its options owned by DuetsPad rather than core Duets.

## Considered Alternatives

### A: Retain only the global generic function

- Pro: No prototype modification or Monaco-specific completion code.
- Pro: Works for null and undefined.
- Con: Intermediate chain values must be wrapped, which obscures evaluation order and differs
  from the LINQPad-style fluent experience.

### B: Add only a prototype method and `dump(): this` declaration

- Pro: Minimal implementation and natural runtime syntax.
- Con: Monaco omits `Object`-inherited members from completions on concrete inferred values.
- Con: Polymorphic `this` from a global `Object` augmentation did not reliably preserve the
  concrete receiver through the Monaco worker used by DuetsPad.

### C: Add only a Monaco completion provider

- Pro: The method becomes discoverable after a dot.
- Con: A completion item does not define TypeScript semantics; without a declaration the call
  is unresolved or loses its return type.
- Con: No corresponding runtime member exists.

### D: Rewrite `value.dump()` to `dump(value)` during transpilation

- Pro: Avoids modifying `Object.prototype`.
- Con: Requires the language service and every transpiler backend to share a syntax transform
  so runtime code, diagnostics, and completions agree.
- Con: Adds parser-level machinery for one convenience method and still needs custom completion
  handling.

### E: Layer runtime dispatch, generic receiver typing, and Monaco discovery

- Pro: Each layer addresses one concern through its native extension point.
- Pro: The TypeScript worker remains authoritative for the type of the completed source.
- Pro: No transpiler changes are required.
- Con: Deliberately adds one non-enumerable method to `Object.prototype`.
- Con: Requires a narrow Monaco completion provider in DuetsPad's browser assets.

## Decision

Choose Alternative E while retaining Alternative A as a compatibility and nullish-value
fallback.

DuetsPad defines a non-enumerable `Object.prototype.dump` method during session bootstrap. The
method is a strict function that delegates to the session's existing global `dump`, forwards
the per-call options, and returns its original receiver unchanged:

```js
Object.defineProperty(Object.prototype, "dump", {
  configurable: true,
  enumerable: false,
  writable: true,
  value: function (options) {
    "use strict";
    dump(this, options);
    return this;
  },
});
```

Strict mode keeps primitive receivers primitive rather than returning a boxed wrapper. The
descriptor follows ordinary JavaScript built-in conventions: the method can be replaced or
removed within the isolated session, but it does not appear in `for...in` enumeration.

DuetsPad registers this ambient declaration:

```ts
interface Object {
  dump<T>(this: T, options?: { maxDepth?: number; maxItems?: number }): T;
}
```

The explicit `this: T` parameter infers `T` from the call-site receiver, and the return type
restores that exact type for the next member access. The declaration, not the completion item,
is the sole source of semantic type information.

A dedicated browser helper registers a TypeScript completion provider triggered by `.`. It
adds only the `dump()` candidate and its replacement range. It rejects comments, strings, and
regular-expression tokens, honors cancellation, and does not attempt to calculate or override
the expression's type. After insertion, Monaco reparses the source and resolves the call using
the ambient declaration above.

The global `dump(value, options?)` remains supported. It is required for `null`, `undefined`,
objects with a null prototype, and receivers whose own `dump` member intentionally shadows the
prototype method.

## Rationale

The successful design comes from composing mechanisms that ADR-20 evaluated separately. A
prototype method alone cannot force Monaco to advertise an inherited `Object` member, while a
completion provider alone cannot give the inserted call a type or runtime implementation. The
ambient generic-receiver declaration bridges those layers: the provider supplies discovery,
then TypeScript independently infers and preserves the receiver type.

This preserves Monaco's semantic authority and avoids a transpiler-specific rewrite. Keeping
the provider narrow and token aware contains the editor customization, while the
non-enumerable descriptor minimizes the observable effect of the intentional prototype
extension.

## Consequences

- **Positive**: Scripts can use `query.where(...).dump().select(...)` with completions before
  and after `dump()`.
- **Positive**: JavaScript objects, primitives, and compatible CLR wrappers share the same
  fluent syntax.
- **Positive**: Existing `dump(value, options?)` scripts remain source compatible.
- **Positive**: Runtime behavior, TypeScript typing, and Monaco discovery are independently
  testable.
- **Negative / trade-off**: DuetsPad intentionally reserves `Object.prototype.dump` inside its
  isolated script realm.
- **Negative / trade-off**: An own or runtime-provided `dump` member shadows DuetsPad's method;
  the global function remains the unambiguous escape hatch.
- **Negative / trade-off**: `null`, `undefined`, and null-prototype objects cannot use the
  fluent form.
- **Negative / trade-off**: The Monaco provider relies on the current TypeScript worker
  filtering `Object`-inherited members. If a future Monaco/TypeScript version exposes them,
  `dump` may appear twice and the provider must be re-evaluated during that dependency update.
