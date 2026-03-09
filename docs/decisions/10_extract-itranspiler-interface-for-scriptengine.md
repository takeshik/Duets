# ADR-10: Extract ITranspiler Interface for ScriptEngine

## Status

Accepted

## Context

`ScriptEngine` previously had no knowledge of TypeScript: callers were required to transpile manually (`ts.Transpile(code)`) and pass the resulting JavaScript to `ScriptEngine.Execute`/`Evaluate`. This meant every call site that wanted TypeScript-aware evaluation had to hold a direct reference to `TypeScriptService`. It also made `ScriptEngine` impossible to use with a stub or alternative transpiler in tests without depending on the full `TypeScriptService`.

## Decision Drivers

- `ScriptEngine` should encapsulate the transpile-then-execute pattern; passing a transpiler is required
- `TypeScriptService` remains the rich, concrete implementation (SSE, `RegisterType`, `GetCompletions`)
- No DI framework; plain constructor injection
- No new heavy abstractions; minimal interface surface
- Test code should be able to inject a stub transpiler without spinning up a full `TypeScriptService`

## Considered Alternatives

### A: No interface; extend ScriptEngine with a TypeScriptService overload

- Pro: Avoids a new abstraction
- Con: Creates a direct dependency from `ScriptEngine` to `TypeScriptService`; the two components are architecturally independent (separate engines, independent lifecycles, [ADR-5](5_separate-jint-engines-for-typescript-compiler-and-user-code.md)) — a hard reference would conflict with this design intent

### B: ITranspiler interface with only Transpile; shared types at namespace level

- Pro: Minimal; `ScriptEngine` stays decoupled from `TypeScriptService`; stub implementations are trivial (one-method interface); `CompilerOptions` and `Diagnostic` become independently usable
- Con: `CompilerOptions` and `Diagnostic` move out of `TypeScriptService` (minor disruption to callers who used `TypeScriptService.CompilerOptions`)

### C: Func&lt;string, string&gt; delegate instead of an interface

- Pro: Even simpler; no new type at all
- Con: Cannot carry the full optional-parameter signature (`CompilerOptions`, `diagnostics`, `fileName`); loses discoverability and documentation; cannot be implemented by a named class in tests

## Decision

Extract `ITranspiler` (Alternative B). Define `CompilerOptions` and `Diagnostic` at namespace level. `ScriptEngine` requires `ITranspiler` as a mandatory constructor parameter. `Execute` and `Evaluate` always transpile via the injected transpiler; there are no separate `ExecuteTypeScript`/`EvaluateTypeScript` methods.

## Rationale

Alternative A would create an upward dependency (`ScriptEngine` → `TypeScriptService`) that violates the isolation design established in ADR-5. Alternative C loses too much signature richness for minimal gain. Alternative B introduces exactly one new public type (`ITranspiler`), promotes two records to namespace level, and gives `ScriptEngine` clean TypeScript-aware methods without any coupling to the TypeScript compiler implementation.

## Consequences

- **Positive**: `ScriptEngine.Execute` and `Evaluate` always encapsulate the transpile step; test code can inject a trivial stub; `TypeScriptService` remains the rich implementation; `ReplService` is simplified (no manual `Transpile` call at the call site)
- **Negative / trade-offs**: `CompilerOptions` and `Diagnostic` are no longer nested in `TypeScriptService` — existing callers using `TypeScriptService.CompilerOptions` must update to unqualified `CompilerOptions` (same namespace). This is a minor breaking change in the public API.
