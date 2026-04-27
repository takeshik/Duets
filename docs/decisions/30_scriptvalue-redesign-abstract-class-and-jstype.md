# ADR-30: ScriptValue Redesign — Abstract Class

## Status

Accepted

## Context

The original `ScriptValue` design used an adapter pattern: a `public sealed class ScriptValue`
held a raw `object` value alongside an `IScriptValueAdapter` that provided all operations
(type checks, equality, display, CLR conversion). This approach had several structural problems:

- `IScriptValueAdapter` was a hand-written vtable that reimplemented what C# inheritance provides
  for free. Every method took `object rawValue` and cast it internally, losing type safety.
- Predicates like `IsUndefined(object rawValue)` ignored their argument in the sentinel
  implementations (adapters for `ScriptValue.Undefined` and `ScriptValue.Null`), violating the
  contract implied by the method signature.
- `ScriptValue(IScriptValueAdapter, object)` was a public constructor that allowed any caller to
  construct arbitrary adapter/value combinations.
- `Is{Undefined,Null,Object}()` instance methods were redundant with equality comparisons,
  creating multiple ways to express the same checks.
- The absence of `==` / `!=` operators meant cross-value comparisons (engine-backed undefined vs
  the static `Undefined` sentinel) returned `false` incorrectly.

## Decision Drivers

- Remove the adapter indirection; let C# inheritance handle dispatch
- Make `==` / `!=` work correctly across sentinel and engine-backed values
- Keep the cross-backend comparison boundary explicit (throw, not silently lie)
- Support a future Okojo backend without changes to the core type

## Considered Alternatives

### A: Keep the adapter pattern, fix the contract violations

Fix `IsUndefined(rawValue)` to use `ReferenceEquals(rawValue, _marker)`, fix `AreEqual` similarly.
Add `==` / `!=` operators with sentinel-aware logic in `ScriptValue.Equals`.

- Pro: Minimal surface change
- Con: Perpetuates the type-unsafe `object rawValue` cast-everywhere design
- Con: The adapter-as-vtable abstraction remains conceptually wrong; inheritance solves this better

### B: Make ScriptValue an abstract class (chosen)

Remove `IScriptValueAdapter`. `ScriptValue` becomes abstract; sentinel values (`Undefined`, `Null`)
are private nested concrete classes; backend packages provide their own concrete subclass
(e.g. `JintScriptValue`).

- Pro: Each subclass is fully self-describing; no cross-cutting adapter indirection
- Pro: Backend-specific types (e.g. `JsValue`) are held as typed fields, not `object`
- Pro: `EqualsCore` / `GetHashCodeCore` hooks give backends full control with clear contracts
- Con: Requires backends to subclass `ScriptValue` rather than instantiate it directly

### C: Add a TypeOf property exposing JS typeof semantics

Add `public abstract JsType TypeOf { get; }` and a `JsType` enum to let callers distinguish
value kinds without casting to backend types.

- Pro: Provides a uniform type discriminant across backends
- Con: Distinguishing `JsType.Function` from `JsType.Object` requires knowing whether a value
  is callable. Jint exposes no public API for this — both `IsCallable` and `ICallable` are
  `internal`. Any approximation using concrete types (`is Function or BindFunction`) is
  white-box knowledge that misses `NamespaceReference`, proxied callables, and any future
  callable types Jint may add. There is no bounded enumeration through the public API.
- Con: Null/undefined checks — the only actual use cases at this point — are already handled
  by `== ScriptValue.Null` / `== ScriptValue.Undefined`, leaving `TypeOf` with no consumers.

## Decision

Choose **Alternative B**, without Alternative C.

`ScriptValue` is an `abstract class`. `IScriptValueAdapter.cs` is deleted. The public constructor
`ScriptValue(IScriptValueAdapter, object)` is removed.

### ScriptValue abstract members

```csharp
public abstract object? ToObject();
public abstract override string ToString();
```

`Is{Undefined,Null,Object}()` are removed. Null/undefined identity goes through `==`.

### Equality contract

`Equals` handles sentinel values without requiring protected hooks:

- `this is UndefinedValue` path covers all undefined comparisons regardless of backend.
- `this is NullValue` delegates to `other.EqualsCore(this)`, letting the engine decide if
  it represents null.
- `EqualsCore` in engine backends handles both same-backend values and the two public sentinels
  via `ReferenceEquals(other, ScriptValue.Null / Undefined)`.
- Cross-backend comparison (neither value is a sentinel) throws `InvalidOperationException`.

### Backend contract

Each backend subclass overrides `EqualsCore` and `GetHashCodeCore`. For the two sentinels:

```csharp
if (ReferenceEquals(other, ScriptValue.Null))      return /* this value is null */;
if (ReferenceEquals(other, ScriptValue.Undefined)) return /* this value is undefined */;
```

`GetHashCodeCore` must return `1` for null values and `0` for undefined values to satisfy the
`Equals`/`GetHashCode` contract across sentinel and engine-backed instances.

## Consequences

- **Positive**: `IScriptValueAdapter` is gone; type safety is restored in backend code.
- **Positive**: `==` / `!=` now work correctly for cross-adapter (sentinel ↔ engine) comparisons.
- **Positive**: `Is{Undefined,Null,Object}()` are removed; the API surface is smaller and cleaner.
- **Positive**: Adding a new backend (e.g. Okojo) requires only subclassing `ScriptValue` and
  implementing `EqualsCore` with the two sentinel reference checks.
- **Negative / trade-off**: `ScriptValue` is no longer directly instantiable; callers that
  previously used `new ScriptValue(adapter, rawValue)` must use the appropriate backend subclass.
