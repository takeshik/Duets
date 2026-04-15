# ADR-27: Split JavaScript Runtime Backends from Duets Core

## Status

Accepted — the Okojo extension-method limitation noted in Consequences was subsequently
resolved via `OkojoExtensionMethodRegistry`.

## Context

Duets started as a single library whose public API, implementation, and tests all assumed Jint as the only JavaScript
runtime. That coupling made it difficult to support alternative runtimes such as Okojo, even though the higher-level
Duets concepts (`DuetsSession`, `ITranspiler`, `TypeDeclarations`, REPL services) are not inherently tied to one JS
engine.

Okojo introduces an additional constraint: it requires .NET 10 and uses a different CLR interop model from Jint. The
project needs a structure where Duets core remains engine-agnostic, while concrete runtime integrations can evolve at
their own pace and with their own target framework requirements.

## Decision Drivers

- Keep `Duets` itself independent from any specific JavaScript runtime
- Support both Jint and Okojo without forking the higher-level Duets APIs
- Preserve the existing `ITranspiler` abstraction as an engine-neutral boundary
- Allow backend-specific CLR interop implementations and backend-specific limitations
- Keep the stricter .NET requirement isolated to the Okojo integration package
- Make tests reflect the new boundary instead of relying on Jint internals everywhere

## Considered Alternatives

### A: Keep Jint inside `Duets` and add optional Okojo code paths

- Pro: Smaller short-term diff and fewer package moves
- Pro: Existing tests and documentation would require fewer updates
- Con: The core assembly would still be architecturally Jint-centric
- Con: Okojo-specific requirements and interop differences would leak into the main package
- Con: Future runtimes would repeat the same coupling problem

### B: Accept runtime objects directly through `DuetsSession` / `ScriptEngine` constructors

- Pro: Minimal abstraction layer
- Pro: Easy to wire up in simple samples
- Con: Concrete runtime types would leak into core APIs and overloads
- Con: Backend-specific setup concerns (CLR access, built-ins, initialization) would still be pushed onto callers
- Con: The public surface would become harder to evolve as more runtimes are added

### C: Split runtime integrations into dedicated backend packages

- Pro: Keeps `Duets` core runtime-agnostic
- Pro: Allows Jint and Okojo to expose different setup helpers and target frameworks
- Pro: Keeps `ITranspiler` and session concepts reusable across backends
- Con: Requires moving code, updating tests, and revising documentation
- Con: Some former assumptions about assembly boundaries and runtime-specific test behavior must be rewritten

## Decision

Choose **Alternative C**.

`Duets` becomes an engine-agnostic core package. Concrete JavaScript runtime integrations live in dedicated backend
packages. The first concrete backend is `Duets.Jint`, which preserves the existing Jint-centered behavior. The split
also paves the way for additional backends such as `Duets.Okojo` to be introduced without touching the core package.

The core package keeps the session model, declaration store, transpiler abstraction, REPL/web services, and runtime-
neutral `ScriptEngine` / `ScriptValue` abstractions. Backend packages provide concrete engine implementations, built-in
type registration behavior, CLR interop adapters, and runtime-hosted transpiler implementations.

## Rationale

The main design pressure is not “support one more runtime” but “stop baking one runtime into every layer of the
library.” A dedicated backend split localizes the places where runtime differences actually matter: value wrappers, CLR
interop, namespace/type exposure, extension method dispatch, and JS-hosted transpiler execution.

Keeping `ITranspiler` in core is important. Transpilation is conceptually “source-to-source” work and remains a stable
boundary even if the implementation happens to run inside Jint or Okojo. This also leaves room for a future
TypeScript-7-or-later wasm-based transpiler without changing the core session contract.

Using backend-provided `DuetsSessionConfiguration` extensions instead of passing runtime objects directly into
`DuetsSession` keeps the core surface narrower and prevents Jint/Okojo setup details from leaking into the common API.

## Consequences

- **Positive**: `Duets` no longer references Jint directly and can host multiple runtime integrations cleanly.
- **Positive**: `Duets.Jint` preserves the existing Jint-centered behavior, including extension-method dispatch.
- **Positive**: Additional backends can set their own target-framework requirements without affecting the core package.
- **Positive**: Tests can distinguish engine-agnostic behavior from backend-specific behavior more explicitly.
- **Negative / trade-offs**: Some previous tests and assumptions that treated Jint implementation details as core
  behavior had to be rewritten.
- **Negative / trade-offs**: Runtime-specific transpiler implementations now live outside the core package, so assembly
  boundaries changed for reflection- and assembly-based tests.
