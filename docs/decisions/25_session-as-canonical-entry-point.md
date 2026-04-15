# ADR-25: Session as Canonical Entry Point

## Status

Accepted — API shape partially superseded by ADR-27.
The session ownership model and `TypeDeclarations` independence remain in effect.
`DuetsSession.CreateAsync` now requires an explicit transpiler factory and
`DuetsSessionConfiguration` delegate; the zero-argument default and the
`RegisterTypeBuiltins()` session method described in the Consequences below
were removed as part of the backend split.

## Context

Three problems with the pre-session API motivated this redesign:

**1. Initialization complexity.** The minimum useful path required callers to
instantiate and wire multiple objects before evaluating a single expression:

```csharp
var declarations = new TypeDeclarations();
using var ts = new TypeScriptService(declarations);
await ts.ResetAsync();
using var engine = new ScriptEngine(null, ts);
```

Both `TypeScriptService` and `BabelTranspiler` require an async initialization step
after construction (constructors were public at the time; both are now private and
accessible only via `CreateAsync`). This could not be merged into a synchronous
constructor, so the two-step sequence was unavoidable unless a factory absorbed it.
Callers had no obvious way to know the correct order or which combinations were valid.

**2. Declaration pipeline exposed at the wrong level.** `TypeDeclarations` is a shared
store consumed by `TypeScriptService` (language-service mirroring) and `ReplService`
(SSE streaming). In the pre-session API, the caller was responsible for creating it
and threading the same instance through every consumer. This leaked an internal
coordination detail into application-level code.

Before this redesign, `TypeScriptService` itself owned declaration responsibilities
(`RegisterType`, `RegisterDeclaration`, declaration events, and snapshot APIs). That
made `TypeScriptService` the unavoidable central dependency for features — such as
`ReplService`, `ScriptTypings`, and `ScriptBuiltins` — that did not actually need the
TypeScript compiler. Extracting `TypeDeclarations` as an independent component was
necessary to break this artificial coupling, and the session design is what gives that
component a clear owner.

**3. No first-class isolated context.** `ScriptEngine` serialises access via internal
locks, preventing true concurrent use. Supporting multiple simultaneous evaluation
contexts required instantiating every component independently with no framework help.

## Decision Drivers

- The minimum usage path should require one object and one `await`.
- Transpiler swappability must be preserved as an explicit design axis.
- `TypeDeclarations` must be independent of any specific `ITranspiler` so it can be
  used with `BabelTranspiler`, `TypeScriptService`, or any future transpiler.
- Ownership of the declaration pipeline must be explicit, not implicit global state.
- Multiple isolated evaluation contexts must be achievable without additional ceremony.

## Considered Alternatives

### A: Default transpiler via null parameter on ScriptEngine constructor

Detect `null` for the transpiler and have `ScriptEngine` create a `BabelTranspiler`
internally.

- Pro: No new type introduced.
- Con: `BabelTranspiler` requires async initialization, which a synchronous constructor
  cannot perform. Deferring to first use forces either sync-over-async or converting
  `Evaluate` to `async`, both of which are poor designs.

### B: Static TypeDeclarations.Default shared instance

Introduce `TypeDeclarations.Default` and have all consumers fall back to it when no
instance is provided.

- Pro: Callers that do not care about isolation need not pass `TypeDeclarations`
  anywhere.
- Con: Components couple implicitly through global mutable state. The wiring works by
  coincidence — all parties happen to default to the same global — rather than by
  explicit design. This is structurally equivalent to a Service Locator.

### C: TypeScriptService owns TypeDeclarations internally

`TypeScriptService` creates its own `TypeDeclarations` and exposes it via a property.

- Con: Contradicts the goal of keeping `TypeDeclarations` independent of any
  `ITranspiler`. When `BabelTranspiler` is the default (see Decision), it has no
  declaration subscription; attaching declarations to `TypeScriptService` makes the
  advanced path inconsistent with the default.

### D: Session object as top-level context

Introduce a session type that owns `TypeDeclarations`, the active `ITranspiler`, and
`ScriptEngine` as a unit, created exclusively via `CreateAsync`.

- Pro: A single `await` produces a fully ready evaluation context. `TypeDeclarations`
  ownership is explicit and local to the session. Concurrent use is addressed by
  creating independent sessions. Transpiler swappability is preserved — the session
  accepts an async factory to select and configure the transpiler, or creates the
  default internally. The factory receives the session-owned `TypeDeclarations`
  before the transpiler is constructed, guaranteeing a single shared declaration store.
- Con: Introduces a new top-level type. Callers composing components manually must
  migrate to the session API or continue using the lower-level components explicitly.

## Decision

Introduce `DuetsSession` as the canonical entry point for the library.

**Session ownership.** The session owns:
- `TypeDeclarations` — the runtime declaration store
- The active `ITranspiler` — selected and initialized by `CreateAsync`
- `ScriptEngine` — the user-code execution context

One session is one isolated evaluation context. Multiple concurrent sessions each hold
their own engine with no shared locking.

**TypeDeclarations as a standalone component.** `TypeDeclarations` is an independent,
transpiler-agnostic declaration store. Its normal lifetime owner is the session, but it
remains a concrete public type so advanced callers can access it directly via
`session.Declarations` (`ITypeDeclarationProvider` / `ITypeDeclarationRegistrar`).
`TypeScriptService` depends only on `ITypeDeclarationProvider` and subscribes to
`DeclarationChanged` to mirror declarations into its language service; it holds no
registration APIs. `ReplService` and `ScriptTypings` / `ScriptBuiltins` depend on the
same narrow interfaces.

**Default transpiler.** `DuetsSession.CreateAsync()` uses `BabelTranspiler` by default.
The comparison is:

| Path | Transpiler | Declaration pipeline visible? |
|------|-----------|-------------------------------|
| Default | `BabelTranspiler` | No — Babel has no declaration subscription |
| Opt-in | `TypeScriptService` | Only when `session.Declarations` is accessed |

`TypeScriptService` was the only option before `BabelTranspiler` was introduced
(ADR-19). Choosing Babel as the new default reflects the goal that the minimum path
requires no knowledge of declarations, completions, or language services. Callers who
need server-side completions opt in explicitly by selecting `TypeScriptService` via the
factory or builder passed to `CreateAsync`.

**Built-in type registration.** CLR type registration (`RegisterTypeBuiltins`, the
`typings` global, `clrTypeOf`) is opt-in. The caller explicitly requests it; only at
that point does `session.Declarations` become relevant. The common path has no
knowledge of the declaration pipeline.

**Lower-level APIs.** `TypeScriptService`, `BabelTranspiler`, `TypeDeclarations`, and
`ScriptEngine` remain public and composable for advanced hosts that need direct control.
`TypeScriptService` and `BabelTranspiler` are created via their respective `CreateAsync`
factory methods; direct construction is intentionally not exposed.

## Rationale

The session is the natural owner of `TypeDeclarations` because it is already the
natural owner of `ScriptEngine` and the transpiler. All three have identical lifetime
and scope. Placing them in one object makes ownership explicit and removes the wiring
burden from callers without introducing any implicit coupling.

The static `Default` alternative was rejected because invisible coordination through
shared mutable global state is harder to reason about than explicit parameter passing.
The TypeScriptService-owns-declarations alternative was rejected because it
contradicts the goal of keeping `TypeDeclarations` independent of any specific
transpiler.

Choosing `BabelTranspiler` as the default removes the declaration pipeline from the
common path entirely. A Babel-based session initializes with a single async call, with
no interaction between transpiler setup and type registration at all.

The current design is effectively single-session, and the most common use case is
expected to remain single-session. Per-session engine initialization therefore carries
no additional cost compared to today.

## Consequences

- **Positive**: The minimum usage path is `await DuetsSession.CreateAsync()` followed
  by `Evaluate`. No knowledge of `TypeDeclarations`, transpilers, or initialization
  order is required.
- **Positive**: Multiple independent sessions are first-class, enabling concurrent use
  without lock contention.
- **Positive**: `TypeDeclarations` ownership is clear, local to the session, and
  accessible only when the caller needs it.
- **Positive**: `TypeScriptService` is reduced to TypeScript-specific concerns
  (transpilation and language service). Declaration registration APIs are removed from
  it entirely.
- **Negative / trade-offs**: Existing code that composes components manually must
  migrate. The lower-level API remains available for advanced cases.
