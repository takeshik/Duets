# ADR-28: Unified `CreateAsync` API and Backend Auto-Discovery via `DuetsBackendRegistry`

## Status

Accepted

## Context

Following ADR-27, `DuetsSession.CreateAsync` had this signature:

```csharp
public static Task<DuetsSession> CreateAsync(
    Func<TypeDeclarations, Task<ITranspiler>> transpilerFactory,
    Action<DuetsSessionConfiguration> configure)
```

The minimal call was:

```csharp
using var session = await DuetsSession.CreateAsync(
    async _ => await BabelTranspiler.CreateAsync(),
    config => config.UseJint());
```

Two problems with this shape:

**1. Asymmetry.** The engine is configured through `DuetsSessionConfiguration` (a builder), but the transpiler is a separate positional argument. There is no obvious reason for this split — both are session-level choices.

**2. Awkward first argument.** When the transpiler does not need `TypeDeclarations` (as is the case for `BabelTranspiler`), callers must write `async _ => await BabelTranspiler.CreateAsync()` — an async wrapper that ignores its argument — because the parameter type always includes `TypeDeclarations`. The parameter exists specifically for `TypeScriptService`, which does need it, but it forces boilerplate on callers who do not.

A natural improvement is to move the transpiler selection into `DuetsSessionConfiguration` alongside the engine, giving both choices a symmetric home. The follow-on question is whether to require explicit configuration at all: since `Duets.Jint` is by far the most common (and currently the only) backend, zero-configuration initialization is a desirable target.

## Decision Drivers

- Callers who add `Duets.Jint` should need no setup code beyond an `await` to get a working session.
- Engine and transpiler selection should be symmetric; neither should occupy a privileged positional argument.
- Transpilers that need `TypeDeclarations` (e.g. `TypeScriptService`) must remain expressible.
- Backend packages must be able to register defaults without requiring user code; the registration mechanism should not introduce circular dependencies.
- `DuetsBackendRegistry` is a global registration surface with different semantics from per-session configuration; the two should not be mixed in the same type.

## Considered Alternatives

### A: Add a `Func<Task<ITranspiler>>` overload to `CreateAsync`

Add a second overload where the transpiler factory ignores `TypeDeclarations`:

```csharp
// Enables method-group syntax:
using var session = await DuetsSession.CreateAsync(
    BabelTranspiler.CreateAsync,
    config => config.UseJint());
```

- Pro: Minimal change; no new types.
- Pro: Eliminates the `async _ => await` boilerplate for the common case.
- Con: Engine and transpiler remain asymmetric — one is a positional argument, one is in the builder.
- Con: Two overloads with similar shapes make the API harder to explain; callers must know which signature applies.
- Con: Zero-configuration initialization (`CreateAsync()`) is still not achievable.

### B: Coupled backend model — engine default includes default transpiler

Move the transpiler into `DuetsSessionConfiguration`, and associate a default transpiler with each engine. Calling `UseJint()` would implicitly select both the engine and the default transpiler (BabelTranspiler), matching the existing behavior. Auto-discovery would operate at the "backend" level: one backend = one engine + one paired default transpiler.

- Pro: Makes `UseJint()` sufficient for the common case without a separate `UseBabel()` call.
- Pro: Prevents callers from accidentally pairing an incompatible transpiler with an engine.
- Con: The coupling is artificial at the interface level. `ITranspiler` is a pure source-to-source boundary; `ScriptEngine` is the execution boundary. Neither interface depends on the other, and concrete `ITranspiler` implementations (`BabelTranspiler`, `TypeScriptService`) manage their own internal runtimes separately from the session engine.
- Con: Engine-specific defaults would need a registry structure, making the ADR-27 backend split harder to extend.

### C: Orthogonal engine and transpiler discovery via `DuetsBackendRegistry`

Move the transpiler into `DuetsSessionConfiguration` as an independent axis. Introduce `DuetsBackendRegistry`, a separate public static class where backend packages register default factories for engine and transpiler independently. `Duets.Jint` uses `[ModuleInitializer]` to register both on assembly load. `DuetsSession.CreateAsync` gains an optional `Action<DuetsSessionConfiguration>?` parameter; when nothing is configured, both factories are resolved from the registry.

- Pro: Engine and transpiler are symmetric in the API; both live in `DuetsSessionConfiguration`.
- Pro: `CreateAsync()` with no arguments works automatically once `Duets.Jint` is referenced.
- Pro: Reflects the actual interface-level independence: `BabelTranspiler` and `TypeScriptService` run their own internal Jint instance separate from the session `ScriptEngine`.
- Pro: `UseTranspiler(decls => TypeScriptService.CreateAsync(decls))` preserves the ability to pass `TypeDeclarations` to transpilers that need it.
- Con: Two registration methods (`RegisterDefaultEngine`, `RegisterDefaultTranspiler`) are `public` because `[ModuleInitializer]` runs in a different assembly. They are not intended for end-user calls but cannot be hidden without an internal accessor hack.

## Decision

Choose **Alternative C**.

`DuetsSession.CreateAsync` is simplified to:

```csharp
public static Task<DuetsSession> CreateAsync(Action<DuetsSessionConfiguration>? configure = null)
```

`DuetsSessionConfiguration` gains `UseTranspiler()` (two overloads: with and without `TypeDeclarations`) alongside the existing `UseEngine()`. Resolution order: explicit call beats registry default; missing config falls back to `DuetsBackendRegistry`; missing default throws with a clear message.

`DuetsBackendRegistry` is a dedicated public static class in `Duets` core. It exposes `RegisterDefaultEngine` and `RegisterDefaultTranspiler`, intended to be called from backend packages' `[ModuleInitializer]`-annotated methods. It is separate from `DuetsSessionConfiguration` because global registration and per-session configuration are different concerns; mixing static and instance semantics in one type would be confusing.

`Duets.Jint` registers both `JintScriptEngine` (engine) and `BabelTranspiler` (transpiler) via `JintBackendInitializer.[ModuleInitializer]` on assembly load. A `UseBabel()` extension is added to `DuetsSessionConfiguration` for explicit opt-in or when `BabelTranspilerOptions` need to be passed.

The resulting usage patterns:

```csharp
// Full auto-discovery (Duets.Jint referenced → Jint + Babel)
using var session = await DuetsSession.CreateAsync();

// Explicit engine options (e.g. CLR interop); transpiler discovered
using var session = await DuetsSession.CreateAsync(config => config
    .UseJint(opts => opts.AllowClr()));

// Explicit transpiler override (TypeScriptService needs TypeDeclarations)
using var session = await DuetsSession.CreateAsync(config => config
    .UseTranspiler(decls => TypeScriptService.CreateAsync(decls, injectStdLib: true))
    .UseJint(opts => opts.AllowClr()));
```

## Rationale

The transpiler and engine are orthogonal at the interface level. `ITranspiler` is a stateless source-to-source converter; `ScriptEngine` is a stateful execution environment. Neither interface references the other. The existing implementations (`BabelTranspiler`, `TypeScriptService`) happen to use Jint internally, but each creates its own engine instance, independent of the session `ScriptEngine`. No interface-level coupling exists that would justify the coupled backend model.

The concern raised during design — that an alternative engine (Okojo) could not use existing transpilers — was traced to an interpreter defect in Okojo's JavaScript runtime, not to any architectural constraint at the interface boundary. Interface-level orthogonality remains valid.

`[ModuleInitializer]` is the correct mechanism for assembly-load-time registration: it is deterministic, runs before any user code references the assembly, and requires no call from application code. The CA2255 warning (which advises against using `[ModuleInitializer]` in libraries) is suppressed because `DuetsBackendRegistry` is explicitly designed as a target for module-initializer calls from backend packages; this is the intended extension point, not an application entry point.

## Consequences

- **Positive**: `await DuetsSession.CreateAsync()` is now the complete minimum; no transpiler or engine code is required from the caller.
- **Positive**: Engine and transpiler configuration are symmetric inside `DuetsSessionConfiguration`.
- **Positive**: `TypeScriptService` and other transpilers that require `TypeDeclarations` remain fully expressible via `UseTranspiler(decls => ...)`.
- **Positive**: Additional backend packages follow the same `DuetsBackendRegistry` + `[ModuleInitializer]` pattern without modifying core.
- **Negative / trade-offs**: `RegisterDefaultEngine` and `RegisterDefaultTranspiler` must be `public` to be callable from `[ModuleInitializer]` in a different assembly, making them visible (though not intended) to end users.
- **Negative / trade-offs**: If two backend packages are referenced and both call `RegisterDefaultEngine`, the second registration throws. Callers in that situation must use explicit `UseEngine()` / `UseJint()` to resolve the ambiguity.
