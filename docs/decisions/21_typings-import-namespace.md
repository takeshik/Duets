# ADR-21: `typings.importNamespace` — Namespace Import with Completion Registration

## Status

Accepted

## Context

Jint provides a global `importNamespace(ns: string)` function when the engine is configured with
`AllowClr`. This function returns a `NamespaceReference` that enables runtime access to CLR types
(calling static methods, constructing instances, etc.), but it has no integration with Duets's
TypeScript declaration system. Calling `importNamespace('System.IO')` grants script-level access
to `System.IO` types at runtime without registering any `.d.ts` declarations, so editor completions
remain absent.

Registering completions requires a separate `typings.useNamespace(...)` call, resulting in the
verbose two-step pattern:

```ts
var IO = importNamespace('System.IO');
typings.useNamespace(IO);
```

In addition, investigation revealed that Jint registers `importNamespace` as
`writable: false, configurable: false` on the global object. `Engine.SetValue` silently fails to
override it, and any workaround requires accessing non-public Jint internals (`Engine.Realm`,
`GlobalObject.FastSetDataProperty`, etc.).

## Decision Drivers

- Scripts should be able to import a namespace and get completions in a single call.
- Users already familiar with Jint's `importNamespace` should be directed to an equivalent that
  also provides completions, without requiring prior knowledge of Jint internals.
- The implementation must not depend on non-public Jint APIs that could break on version updates.
- Both string names and pre-existing `NamespaceReference` values should be accepted, consistent
  with how other `typings.*` methods handle polymorphic arguments.

## Considered Alternatives

### A: Add `typings.importNamespace` and silently override `globalThis.importNamespace`

Wrap Jint's `importNamespace` with a Duets delegate and replace the global via
`GlobalObject.FastSetDataProperty` (accessed through reflection or `UnsafeAccessor`).

- Pro: Calling `importNamespace(...)` automatically registers completions — no API change for
  users already using the global.
- Con: Depends on non-public Jint internals (`Engine.Realm`, `GlobalObject.FastSetDataProperty`).
  A Jint version bump can break this silently.
- Con: `FastSetDataProperty` bypasses ECMAScript property descriptor semantics, making the
  behavior difficult to reason about.

### B: Add `typings.importNamespace` only; leave `globalThis.importNamespace` unchanged

Expose `ImportNamespace(JsValue)` on `ScriptTypings`, callable as `typings.importNamespace(ns)`.
The Jint-provided `globalThis.importNamespace` continues to work for runtime-only access and is
documented as such. The original callable is captured at `RegisterTypeBuiltins` time via
`Engine.Call` (public API) and stored as a `Func<JsValue, JsValue>` in `ScriptTypings`.

- Pro: Zero dependency on non-public Jint internals.
- Con: Users who call the bare `importNamespace(...)` do not automatically get completions and
  must migrate to `typings.importNamespace(...)`.

### C: Intercept via Jint Options (e.g. `InteropOptions`)

Hook into `Options.Interop.CreateTypeReferenceObject`, `WrapObjectHandler`, or a hypothetical
`ImportNamespaceFactory` to intercept namespace creation.

- Pro: No runtime patching of global properties.
- Con: `Jint.Options.InteropOptions` has no namespace-import-specific hook; the available hooks
  fire for all type/object access, not only `importNamespace` calls. Implementation would require
  fragile heuristics.

## Decision

**Alternative B** — add `typings.importNamespace(ns)` and leave `globalThis.importNamespace`
unchanged.

`typings.importNamespace` accepts a namespace name string or an existing `NamespaceReference`.
When given a string, it delegates to the captured original callable and then calls `UseNamespace`
on the result. When given a `NamespaceReference`, it calls `UseNamespace` directly and returns
the reference unchanged.

`globalThis.importNamespace` is documented as "runtime access only, no completion registration"
in `ScriptEngineInit.d.ts`. A test asserts this property explicitly to guard against accidental
future overrides.

## Rationale

Alternative A achieves seamless UX but couples the implementation to Jint's internal object
model. The `non-writable, non-configurable` constraint is intentional on Jint's part; bypassing
it by force is an unsupported use of the library that any minor Jint release could break. The
risk is disproportionate to the convenience gained.

Alternative C has no viable hook point in the current Jint v4 `InteropOptions` surface.

Alternative B is the only approach that relies solely on Jint's public API (`Engine.Call`,
`Engine.GetValue`, `Engine.SetValue`). The trade-off — requiring an explicit `typings.` prefix —
is acceptable because the two-call pattern it replaces (`importNamespace` + `useNamespace`) is
clearly worse, and `typings.importNamespace` is easy to discover from the `.d.ts` declaration.

## Consequences

- **Positive**: `typings.importNamespace('System.IO')` combines runtime access and completion
  registration in one call; both string and `NamespaceReference` arguments are accepted.
- **Positive**: No dependency on Jint non-public internals; the implementation is stable across
  Jint version updates.
- **Positive**: The behavioral boundary between `globalThis.importNamespace` (runtime only) and
  `typings.importNamespace` (runtime + completions) is explicit and tested.
- **Negative / trade-offs**: Users who call `importNamespace(...)` without the `typings.` prefix
  do not get completions automatically; they must use `typings.importNamespace(...)` instead.
