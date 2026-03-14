# ADR-13: Script Built-ins and the `typings` Object

## Status

Accepted

## Context

`TypeScriptService.RegisterType` allows callers to add .NET type declarations to the TypeScript language service. Prior to this change, the only way to invoke this from within a running script was `importTypeDefs`, a function that `SandboxSession.RegisterBuiltins()` injected into the script engine. This arrangement had several problems:

- `importTypeDefs` was defined in `Duets.Sandbox`, an application-layer project, rather than in the `Duets` core library. Any host embedding `Duets` had to re-implement the same wiring.
- The function name was too verbose for a frequently typed operation.
- Registration was only possible at the type level. In practice, users want to register an entire namespace (e.g., all of `System.IO`) in one call rather than listing every type individually.
- `Math` and `Enumerable` were pre-registered unconditionally, which is opinionated and removed the user's choice.

## Decision Drivers

- **Library reusability**: Built-in script functions for managing type declarations should be provided by `Duets`, not by each embedding application.
- **Ergonomics**: Functions used frequently in interactive sessions must be short and easy to type.
- **Namespace-level registration**: Users should be able to register all public types in a namespace without enumerating them.
- **Assembly scanning**: Users should be able to load an assembly and inspect its namespace structure before committing to full type registration.
- **AllowClr neutrality**: The library must not force any `AllowClr` configuration. CLR accessibility policy is the embedding application's responsibility.

## Considered Alternatives

### A: Top-level global functions (`use`, `scanAssembly`, `useNamespace`, …)

- Pro: Shortest call syntax.
- Con: Pollutes the global namespace. Name collisions with user-defined variables are plausible for common names like `use`.
- Con: Naming the namespace-registration function without implying C++ `using namespace` semantics is harder at the top level.

### B: `typings` host object with methods

- Pro: Groups all type-declaration management under one well-named namespace, eliminating ambiguity.
- Pro: `typings.useNamespace(System.Net.Http)` is unambiguously "add typings for this namespace", not "bring names into scope".
- Pro: Method names inside an object can be shorter and more context-dependent (e.g., `use`, `scanAssembly`) without losing clarity.
- Con: One extra level of indirection compared to top-level globals.

### C: Keep `importTypeDefs` in the sandbox

- Pro: No change to the library boundary.
- Con: Every embedding application re-implements the same boilerplate. Contradicts the goal of a reusable library.

## Decision

Introduce a `typings` global object (Alternative B) provided by the `Duets` core library. The object is an instance of `ScriptTypings`, registered into the `ScriptEngine` by calling `RegisterTypeBuiltins(TypeScriptService)`. The `typings` object exposes four methods:

| Method | Description |
|---|---|
| `typings.use(assemblyQualifiedName)` | Register a single type by assembly-qualified name |
| `typings.scanAssembly(assemblyName)` | Load assembly; register namespace skeleton declarations (no type members) so namespaces appear in TS completions |
| `typings.useAssembly(assemblyName)` | Load assembly; register all public types |
| `typings.useNamespace(ns)` | Register all public types in the given namespace; accepts either a Jint `NamespaceReference` (e.g., `System.Net.Http`) or a plain string |

`SandboxSession` is simplified to call `RegisterTypeBuiltins` during initialization; `RegisterBuiltins()` and the pre-registration of `Math` and `Enumerable` are removed.

## Rationale

The `typings` prefix resolves the naming tension cleanly. In the TypeScript / npm ecosystem, third-party type declaration packages are distributed under the `@types/` scope (e.g. `@types/node`) and are colloquially called "typings". The term predates `@types/`: before DefinitelyTyped moved to the scoped package model, a tool called `typings` was the standard way to install `.d.ts` files. As a result, "typings" is widely understood to mean "TypeScript type declarations", making it an immediately recognizable name for an object whose sole purpose is managing `.d.ts` registration. Under this prefix, `useNamespace` is unambiguously about adding type declarations for a namespace, not about bringing its names into scope (which would be the connotation of a top-level `useNamespace` or `using namespace`).

Accepting both `NamespaceReference` and `string` in `useNamespace` decouples the function from `AllowClr` configuration. When `AllowClr` includes the assembly, users can write the ergonomic `typings.useNamespace(System.Net.Http)` form and benefit from editor completion on the argument. When `AllowClr` is restricted or the assembly has not been exposed via CLR interop, `typings.useNamespace("System.Net.Http")` works as a fallback.

`AllowClr` configuration is intentionally left to embedding applications. The `Duets` library sets no default; `SandboxSession` opts into `AllowClr()` (all assemblies) because it is a developer sandbox where broad CLR access is expected. Other hosts can restrict or omit CLR access entirely via the `Action<Options>? configure` parameter of `ScriptEngine`.

`scanAssembly` generates lightweight namespace skeleton declarations (`declare namespace System.Net.Http { }`) so that namespace traversal is completable in the TypeScript language service before any types are fully registered. This lets users discover available namespaces cheaply, then call `useNamespace` or `useAssembly` selectively.

## Consequences

- **Positive**: `Duets` now ships built-in script functions for type-declaration management. Embedding applications do not need to re-implement this wiring.
- **Positive**: The `typings` API is ergonomic for interactive use and scales from single-type to full-assembly registration.
- **Positive**: `useNamespace` works with or without `AllowClr`, making it usable in any host configuration.
- **Positive**: Removing pre-registration of `Math` and `Enumerable` gives applications full control over what types are declared.
- **Negative / trade-offs**: The `NamespaceReference` form of `useNamespace` requires the assembly to be present in `AllowClr`; callers using a restricted CLR policy must use the string form instead.
- **Negative / trade-offs**: `scanAssembly` followed by `useNamespace(System.Net.Http)` is a two-step workflow; callers who want a one-step "load and register all types" path should use `useAssembly` instead.
