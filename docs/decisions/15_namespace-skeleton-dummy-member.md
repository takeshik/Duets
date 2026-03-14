# ADR-15: Namespace Skeleton Visibility via `$name` Dummy Member

## Status

Accepted

## Context

`typings.scanAssembly` registers lightweight namespace skeleton declarations (`declare namespace System.IO { }`) so that users can discover available namespaces in the TypeScript language service before committing to full type registration. However, the TypeScript language service does not include empty `declare namespace` blocks in completion results. A namespace with no members is filtered out and never surfaces as a completion candidate. This defeats the stated purpose of `scanAssembly`.

The requirement is to make namespace names visible in completions immediately after `scanAssembly`, without registering any type members.

## Decision Drivers

- **Completion visibility**: namespace names must appear in TypeScript completions after `scanAssembly` without requiring full type registration.
- **Automatic cleanup**: any workaround artifact (e.g., a dummy member) must not persist in completions once real type declarations for that namespace are registered.
- **No Jint runtime changes**: the solution must work within the existing Jint interop model and must not require modifications to Jint's internal type resolution.

## Considered Alternatives

### A: `$name` string const dummy member

Include `const $name: 'System.IO';` inside the skeleton namespace declaration to make it non-empty:

```typescript
declare namespace System.IO {
    const $name: 'System.IO';
}
```

When `RegisterType` is called for any type whose namespace matches, the skeleton file is updated in-place (via `$host.addFile` with the same file name) to an empty declaration, removing `$name`. The real type declaration then keeps the namespace visible.

- Pro: trivial to implement; reliably makes the namespace appear in completions.
- Pro: `$name` is removed automatically when real types arrive, so it does not linger.
- Con: until real types are registered, `$name` appears alongside namespace names in completions (noise).

### B: `$use()` function on the namespace

Declare a `$use()` method inside the skeleton namespace. Calling `System.IO.$use()` would trigger the equivalent of `typings.useNamespace(System.IO)` and then remove itself.

- Pro: more ergonomic and discoverable — the completion itself acts as an invitation to load the namespace.
- Con: not implementable. In Jint, `System.IO` resolves to a `NamespaceReference`, which is an internal Jint type. Custom JavaScript properties cannot be attached to it at runtime. The TypeScript d.ts can declare `$use()`, but `System.IO.$use` would be `undefined` when actually invoked — a misleading and broken experience.

### C: Stub type declarations (type names without members)

Register one-line stubs such as `declare class File {}` for each type in the assembly, instead of namespace-level skeletons.

- Pro: type names appear directly in completions, giving richer discovery.
- Con: significantly higher implementation cost (enumerating types, generating stubs, de-duplicating with eventual real declarations).
- Con: the intermediate state (stubs with no members) may confuse users who rely on completions inside those types.

## Decision

Use the `$name` dummy member approach (Alternative A). The skeleton content is:

```typescript
declare namespace System.IO {
    const $name: 'System.IO';
}
```

`TypeScriptService` tracks skeleton-carrying namespaces in `_pendingSkeletonNamespaces` (`Dictionary<string, string>`, namespace → file name) and covered namespaces in `_coveredNamespaces` (`HashSet<string>`). When `RegisterType` is called:

1. If the type's namespace is in `_pendingSkeletonNamespaces`, the skeleton file is overwritten with an empty declaration via `$host.addFile`, the entry is moved from `_pendingSkeletonNamespaces` to `_coveredNamespaces`.
2. If the namespace is already in `_coveredNamespaces`, no action is taken.

`RegisterNamespaceSkeleton` skips namespaces already in either set.

## Rationale

The `$name` approach is the only option that satisfies all three decision drivers within the constraints of the TypeScript language service and Jint's interop model. Alternative B (`$use()`) was considered preferable from a UX standpoint but is fundamentally blocked by Jint's `NamespaceReference` being a closed internal type. Alternative C provides richer completions but at a disproportionate implementation cost for what is a transitional state.

The `$` prefix on the dummy member name is a conventional signal for "internal / framework-generated" in JavaScript ecosystems, reducing the likelihood of name collision with actual CLR types.

The in-place file update (`$host.addFile` with the same file name) relies on the TypeScript language service performing an incremental re-analysis of only the changed file, keeping the performance impact of cleanup negligible.

## Consequences

- **Positive**: namespace names appear in TypeScript completions immediately after `scanAssembly`, enabling lightweight discovery before full type registration.
- **Positive**: `$name` is cleaned up automatically and does not pollute completions once real type declarations are in place.
- **Negative / trade-offs**: `$name` is visible in completions during the window between `scanAssembly` and actual type registration, adding a small amount of noise.
- **Negative / trade-offs**: the in-place file update depends on the TypeScript language service correctly processing a file content replacement under the same file name; this behavior is not part of a formal API contract.
