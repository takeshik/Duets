# ADR-19: Babel Transpiler as TypeScript 7 Migration Path

## Status

Accepted

## Context

Duets currently runs the official TypeScript compiler (`typescript.js`) inside a Jint engine to
transpile TypeScript to JavaScript and to provide a server-side language service for completions
([ADR-4](4_use-jint-as-the-javascript-engine.md), [ADR-10](10_extract-itranspiler-interface-for-scriptengine.md)).

TypeScript 7.0 rewrites the compiler in Go (`@typescript/native-preview`). The resulting package
ships only a native binary; it does not include a JavaScript bundle or any programmatic JS API
(`ts.transpile`, `ts.createLanguageService`). TypeScript 6.x is the last JavaScript-based major
release and will continue to receive maintenance, but pinning to it indefinitely is not a viable
long-term strategy for a library that aims to support current TypeScript language features.

The `ITranspiler` interface ([ADR-10](10_extract-itranspiler-interface-for-scriptengine.md))
already decouples `ScriptEngine` from any specific compiler implementation, and the `IAssetSource`
abstraction ([ADR-18](18_pluggable-asset-source-abstraction.md)) decouples asset fetching. Both
provide natural extension points for introducing an alternative transpiler backend.

## Decision Drivers

- **Forward compatibility** — Must not be permanently blocked on TypeScript 6.x
- **Universal embeddability** — Cannot depend on a native binary, subprocess, or Node.js runtime; must run anywhere .NET runs ([ADR-3](3_use-httplistener-instead-of-asp-net-core-kestrel.md), [ADR-4](4_use-jint-as-the-javascript-engine.md))
- **TypeScript runtime construct support** — Must handle `enum`, `namespace`, and constructor parameter properties, all of which have runtime semantics and cannot be erased by a simple type stripper
- **Minimal architectural disruption** — The switch should be exercisable through the existing `ITranspiler` abstraction without changes to `ScriptEngine`, `ReplService`, or consumers

## Considered Alternatives

### A: Pin to TypeScript 6.x indefinitely

- Pro: Zero implementation cost; current architecture unchanged
- Con: No path to TypeScript 7 language features; falls further behind as the ecosystem moves to 7.x; dependency on a deprecated release line

### B: `ts-blank-space` (Bloomberg) as type stripper

- Pro: Pure JS, small bundle, minimal overhead
- Con: Does not support `enum` or `namespace` (both have runtime semantics); requires Node.js ≥ 18.0.0; its own ESM format complicates embedding in Jint

### C: `sucrase`

- Pro: Fast; handles `enum` and constructor parameter properties
- Con: No standalone browser/UMD bundle — must be pre-bundled; silently erases `namespace` declarations rather than transforming them, producing broken runtime behaviour

### D: `@babel/standalone` running in Jint

- Pro: Self-contained UMD bundle designed explicitly for non-Node environments; handles `enum`, `namespace`, and constructor parameter properties via `@babel/preset-typescript`; bundle size (~8–9 MB unminified) comparable to `typescript.js` (~8.65 MB); actively maintained by Meta; independent TypeScript parser — not affected by the TypeScript compiler's implementation changes
- Con: Not the official TypeScript compiler; may lag on new TS syntax until Babel updates; `namespace` scope-sharing across files has limitations; no language service API (completions require the TypeScript transpiler)

### E: Port typescript-go (type erasure subset) to C#

- Pro: Pure .NET; no external runtime dependency; would eventually support type checking as well as transpilation
- Con: TypeScript's parser and emitter represent months of engineering effort to port; Duets' REPL use case does not require type checking, making the cost disproportionate to the benefit

### F: Invoke the `tsgo` native binary as a subprocess

- Pro: Full TypeScript 7 feature parity
- Con: Requires a native binary to be installed; incompatible with the universal embeddability requirement (Unity, Godot, iOS, Android)

## Decision

Introduce `BabelTranspiler`, a new `ITranspiler` implementation that runs `@babel/standalone` in a
dedicated Jint engine. It serves as both a proof that `ITranspiler` is replaceable and as the
practical migration path for TypeScript 7.

`TypeScriptService` continues to provide language service features (completions, type declaration
management, SSE delivery) that Babel cannot replicate. `BabelTranspiler` is available as an opt-in
alternative for scenarios where `typescript.js` is unavailable or undesirable.

> **Note (ADR-25):** When ADR-25 introduced `DuetsSession`, `BabelTranspiler` became the **default**
> transpiler for `DuetsSession.CreateAsync()`. `TypeScriptService` is now the opt-in path for callers
> who need server-side completions. The analysis and rationale in this ADR remain valid; only the
> default selection changed.

## Rationale

`@babel/standalone` is the only identified alternative that satisfies all three hard constraints
simultaneously: it is a self-contained JS bundle (no native binaries, no Node.js), it handles
TypeScript runtime constructs that a pure type-stripper cannot, and it slots into `ITranspiler`
without architectural changes. Alternatives B and C fail on runtime construct support; alternatives
E and F fail on embeddability or cost.

The fact that Babel maintains its own TypeScript parser independently of the official compiler is
a key property: `BabelTranspiler` will continue to function regardless of what Microsoft does with
the TypeScript compiler's distribution format.

Pinning to TypeScript 6.x (alternative A) was explicitly rejected as a dead end rather than a
migration strategy.

## Consequences

- **Positive**: Duets is no longer architecturally blocked by the TypeScript 7 transition; `ITranspiler` replaceability is demonstrated with a working implementation and test coverage; `Duets.Sandbox` gains a runtime `set-transpiler` command for exercising both backends
- **Negative / trade-offs**: `BabelTranspiler` does not provide language service features; the Monaco web REPL requires the TypeScript transpiler for completions and type declaration SSE; `BabelTranspiler` depends on Babel's TypeScript support staying current with the TS language specification
