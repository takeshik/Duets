# ADR-24: `typings` API Redesign — `usingNamespace` and `import-` Prefix

## Status

Accepted — supersedes ADR-13 (method names) and ADR-21 (`importNamespace` / `useNamespace` split)

## Context

ADR-21 added `typings.importNamespace(ns)` with the intent of providing a single call that gives
both runtime access and TypeScript completions. However, investigation revealed that the premise
was incorrect:

- Jint's `importNamespace('System.IO')` is semantically equivalent to the expression `System.IO`
  when `AllowClr` is configured — both return a `NamespaceReference` for the same path.
- `AllowClr` adds root CLR namespaces (`System`, `Microsoft`, etc.) as non-enumerable properties
  of `globalThis`, but **does not scatter individual types as globals**. Types inside a namespace
  still require `System.IO.File` (qualified) or `new IO.File(...)` (via a local alias) — never
  bare `new File(...)`.
- Therefore `typings.importNamespace` was little more than a wrapper around `typings.useNamespace`,
  providing no runtime capability beyond what `importNamespace(ns)` already gives for free.

The original ADR-21 intent — equivalent to C#'s `using System.IO;`, which **scatters types into
the current scope** — was never implemented. The actual `using` semantics require:

1. Calling `importNamespace(ns)` to load the assembly and enable runtime access.
2. Iterating all types in the namespace and calling `TypeReference.CreateTypeReference(engine, type)`
   + `engine.SetValue(name, typeRef)` to scatter each type as a global.
3. Registering `declare var TypeName: typeof Ns.TypeName;` declarations for TypeScript completions.

Additionally, the existing `use-` prefix on methods (`useType`, `useAssembly`, `useAssemblyOf`,
`useNamespace`) was inconsistent with the `import-` prefix used on the most prominent method
(`importNamespace`). All `use-` methods have the same semantic intent as `importType`: "bring into
TypeScript scope". Renaming them to `import-` makes the API coherent.

## Decision Drivers

- The `using System.IO;` semantics (scatter types as globals) must be explicitly supported.
- Method names must be consistent in prefix and semantic intent.
- The implementation must use only public Jint APIs (`TypeReference.CreateTypeReference`,
  `Engine.SetValue`, `Engine.Call`).

## Considered Alternatives

### A: Keep `useNamespace` and add `usingNamespace` as a new method

Retain the existing `useNamespace` for completions-only, and add `usingNamespace` for
`using`-equivalent full scatter.

- Pro: No breaking change for existing callers of `useNamespace`.
- Con: Two nearly identical methods causes confusion; `useNamespace` loses much of its
  reason to exist since `usingNamespace` subsumes it.

### B: Replace `useNamespace` with `usingNamespace`; rename `use-` to `import-`

Remove `useNamespace`; add `usingNamespace` for the full `using` semantics.
Rename `useType` → `importType`, `useAssembly` → `importAssembly`, `useAssemblyOf` → `importAssemblyOf`.

- Pro: Clean, non-overlapping API surface.
- Pro: `import-` prefix unifies all "bring into TypeScript scope" methods.
- Con: Breaking change for any existing callers of `useType`, `useAssembly`, `useAssemblyOf`,
  `useNamespace`.

## Decision

**Alternative B** — replace `useNamespace` with `usingNamespace`; rename `use-` methods to
`import-`.

### Final `typings` API surface

| Method | Behavior |
|---|---|
| `typings.importType(typeRef)` | Register a single type for completions. |
| `typings.importNamespace(ns)` | Register all types in a namespace for completions; return the namespace reference. |
| `typings.usingNamespace(ns)` | C# `using` equivalent: register types + scatter each as a global variable. |
| `typings.importAssembly(asm)` | Register all public types in an assembly for completions. |
| `typings.importAssemblyOf(typeRef)` | Same as `importAssembly`, deriving the assembly from a type reference. |
| `typings.scanAssembly(asm)` | Register namespace skeletons only (no type members). |
| `typings.scanAssemblyOf(typeRef)` | Same as `scanAssembly`, deriving the assembly from a type reference. |

### `usingNamespace` implementation

When `usingNamespace(ns)` is called:

1. Resolve the namespace string (from a `NamespaceReference` or a string argument).
2. If a string was given and `importNamespace` is configured, call it to load the assembly.
3. Call `RegisterNamespaceTypes(ns)` to register `.d.ts` declarations for all types in the namespace.
4. If `exposeGlobal` is configured: for each type, call `exposeGlobal(type.Name, type)` and
   accumulate `declare var TypeName: typeof Ns.TypeName;` lines. Register the accumulated
   declarations via `RegisterDeclaration`.

`exposeGlobal` is injected by `ScriptBuiltins.RegisterTypeBuiltins` as:

```csharp
Action<string, Type> exposeGlobal = (name, type) =>
{
    var typeRef = TypeReference.CreateTypeReference(jintEngine, type);
    jintEngine.SetValue(name, typeRef);
};
```

This uses only public Jint APIs.

## Rationale

The `usingNamespace` name was chosen over `importNamespace` (already taken by the
completions-only variant) or `using` (a C# reserved keyword that would be confusing in a
TypeScript context). The name is self-documenting as the TypeScript analogue to C#'s `using`.

The `import-` prefix rename is consistent because every method in the group brings types into
the TypeScript (completion) scope — the same semantic as a TypeScript `import`. The `scan-`
prefix is retained because scanning produces only namespace skeletons, which is meaningfully
different.

## Consequences

- **Positive**: `typings.usingNamespace('System.IO')` now correctly replicates C#'s
  `using System.IO;` — each type becomes accessible as a global (`new FileInfo(...)`) and
  completions are registered.
- **Positive**: Consistent `import-` prefix across all "bring into TypeScript scope" methods.
- **Positive**: Implementation uses only public Jint APIs, stable across Jint version updates.
- **Negative / breaking**: `useType`, `useAssembly`, `useAssemblyOf`, `useNamespace` are removed;
  callers must migrate to `importType`, `importAssembly`, `importAssemblyOf`, `usingNamespace`.
