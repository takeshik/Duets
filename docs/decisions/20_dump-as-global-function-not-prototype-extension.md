# ADR-20: `dump` Implemented as a Generic Global Function, Not a Prototype Extension

## Status

Accepted

## Context

The REPL needs a way to output an intermediate value to the output pane without breaking an expression chain.
The natural JavaScript idiom would be `someExpr.dump()`, which requires adding a method to `Object.prototype`.

An `Object.prototype.dump` implementation was written and shipped, but the method never appeared in Monaco Editor
completions regardless of the value type — including plain object literals (`{x: 1}`), .NET wrapper objects, and
primitive types. Removing it and replacing with a different form was therefore necessary.

## Decision Drivers

- The method must be discoverable through Monaco's TypeScript completion system.
- For chain use, the return type must match the input type: `dump(expr)` must return `T`, not `any`, so that
  further property accesses after the call still have correct completion context.
- The implementation must not require changes to Monaco-specific code in the web front-end.

## Considered Alternatives

### A: `Object.prototype.dump` with `interface Object { dump(): this }`

Add the method at runtime via `Object.defineProperty` and declare it in `ScriptEngineInit.d.ts` using TypeScript
declaration merging (`interface Object { dump(opts?): this }`), then push the declaration to Monaco via `addExtraLib`.

- Pro: Natural chaining syntax — `someExpr.dump().nextProperty`.
- Pro: Works at runtime for any value regardless of type.
- Con: Monaco's `addExtraLib` does **not** propagate `interface Object` augmentations to specific inferred types.
  A variable typed as `{x: number}` does not inherit the merged `Object` member in completions; only variables
  explicitly typed as `Object` would see `dump`. This is fundamental TypeScript/Monaco behavior: the language
  service intentionally hides inherited `Object` members on non-`Object`-typed expressions to reduce noise.
- Con: Even `hasOwnProperty`, a built-in `Object` method, does not appear on `{x: 1}.` in this context,
  confirming the limitation is structural and not fixable from the declaration side.

### B: Monaco `registerCompletionItemProvider` with a `.` trigger

Register a Monaco completion provider that fires on `.` and injects `dump` into the completion list for every
expression, independent of TypeScript's type inference.

- Pro: `dump` reliably appears in completions regardless of value type.
- Con: The provider returns a plain `CompletionItem` with no type information, so TypeScript treats the result
  of `expr.dump()` as `any`. This breaks chained completions: after `expr.dump()`, further `.` completions
  become empty because the type is lost. The `T.dump(): T` constraint cannot be expressed this way.
- Con: Adds Monaco-specific ad-hoc wiring in the front-end that has no equivalent in the server-side language
  service, creating a split completion model.

### C: Global function `dump<T>(value: T, opts?): T`

Declare `dump` as a top-level generic function in `ScriptEngineInit.d.ts` and implement it as a global variable
in `ScriptEngineInit.js`.

- Pro: TypeScript generics fully preserve the concrete inferred type: `dump({x: 1})` returns `{x: number}`,
  so chained completions (`dump(expr).someProperty`) work correctly.
- Pro: Global functions appear in Monaco completions without any special registration.
- Pro: No changes to the Monaco front-end are required.
- Con: Call-site syntax changes from `expr.dump()` to `dump(expr)` — less fluent for deep chains.
- Con: Does not attach to arbitrary objects via `.`; callers must wrap the expression rather than insert inline.

## Decision

Implement `dump` as a global generic function (Alternative C):

```ts
declare function dump<T>(value: T, opts?: { depth?: number; compact?: boolean }): T;
```

```js
var dump = function (value, opts) {
    __consoleImpl__('log', inspect(value, opts));
    return value;
};
```

## Rationale

The core constraint is that Monaco completions must work and the return type must be `T`. Alternative A fails on
completions due to an immovable limitation in how TypeScript's language service handles `interface Object`
merging for inferred types. Alternative B satisfies discoverability but loses type precision, making it worse
than useless for chained expressions. Only a generic global function can satisfy both requirements with the
existing infrastructure and no front-end modifications.

The ergonomic cost (`dump(expr)` vs `expr.dump()`) is real but minor: in a REPL the expression being inspected
is typically the outermost wrapper, not a middle link in a long chain.

## Consequences

- **Positive**: `dump` appears in Monaco completions as a global function and preserves the full inferred type
  of its argument, enabling correct completions after the call.
- **Positive**: No Monaco-specific code is needed; the declaration in `ScriptEngineInit.d.ts` is sufficient.
- **Negative / trade-offs**: The call site is `dump(x)` rather than `x.dump()`. Multi-level chains require
  wrapping the sub-expression: `dump(a.b).c` instead of `a.b.dump().c`.
- **Negative / trade-offs**: The `Object.prototype` slot is never used, so `x.dump()` at runtime would throw
  unless the caller imports or polyfills the method themselves.
