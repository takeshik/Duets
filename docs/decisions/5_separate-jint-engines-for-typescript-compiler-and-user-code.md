# ADR-5: Separate Jint Engines for TypeScript Compiler and User Code

## Status

Accepted

## Context

Duets uses Jint (a JavaScript engine for .NET) in two distinct roles: running the TypeScript compiler to transpile and provide completions, and executing user-written scripts. These two roles have different lifetimes, configurations, and security requirements.

## Decision Drivers

- **Isolation** — The TypeScript compiler's global state (the `ts` object, language service host, registered files) must not be polluted by user code, and vice versa
- **Independent configuration** — The user code engine needs `AllowClr` to expose .NET types, while the compiler engine should remain sandboxed
- **Independent lifecycle** — The compiler engine is reset via `TypeScriptService.ResetAsync()`; the user code engine is configured by the consumer with their own values and assemblies

## Considered Alternatives

### A: Single shared Jint engine

- Pro: Simpler setup; less memory usage
- Con: User code can accidentally overwrite compiler globals (e.g. `ts`); `AllowClr` on the shared engine exposes .NET types to the compiler context; engine reset destroys user-registered values

### B: Two independent Jint engines

- Pro: Complete isolation between compiler and user code; each can be configured and reset independently
- Con: Higher memory usage (two engine instances); TypeScript compiler JS is large (~5 MB parsed)

## Decision

Use two independent Jint engines: one owned by `TypeScriptService` for the compiler, one owned by `ScriptEngine` for user code execution.

## Rationale

Isolation is non-negotiable. The TypeScript compiler must remain in a predictable state to produce correct transpilation and completions. User code is arbitrary and untrusted from the compiler's perspective. A single engine would require careful namespacing and discipline to avoid collisions, which is fragile. The memory cost of a second engine is acceptable given that Duets is a debugging/scripting tool, not a resource-constrained production service.

## Consequences

- **Positive**: TypeScript compiler state is fully protected from user code; each engine can be configured with different security policies; `TypeScriptService` and `ScriptEngine` can be developed and tested independently
- **Negative / trade-offs**: Two engine instances consume more memory; the transpilation result must be passed as a string from one engine to the other (no shared object graph)
