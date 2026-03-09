# ADR-8: Use addExtraLib to Inject .d.ts Declarations for Completions

## Status

Accepted

## Context

Once Monaco Editor was selected as the REPL UI ([ADR-7](7_use-monaco-editor-as-the-browser-based-repl-ui.md)), the question became how to provide completions for .NET types within Monaco. Jint alone cannot provide static analysis — it is an execution engine, not a type checker. Completions require a TypeScript-aware language service.

## Decision Drivers

- **No external runtime dependency** — An approach requiring a separate process (Node.js, tsserver) would conflict with the embeddability goal ([ADR-3](3_use-httplistener-instead-of-asp-net-core-kestrel.md), [ADR-4](4_use-jint-as-the-javascript-engine.md)) and undo the self-containment benefit of choosing Monaco ([ADR-7](7_use-monaco-editor-as-the-browser-based-repl-ui.md))
- **Sufficient completion quality** — Completions should cover the public API surface of registered .NET types with reasonable fidelity; gaps falling back to `any` are acceptable for a debugging/scripting tool
- **Simplicity** — The completion mechanism should be self-contained and not require complex inter-process communication or lifecycle management
- **Runtime flexibility** — Consumers register types at runtime (`ts.RegisterType(typeof(Math))`); the type information mechanism must work with any type provided at runtime

## Considered Alternatives

### A: External LSP server (tsserver/Node.js, as used in the VS Code interface approach)

This is the completion mechanism implied by the VS Code-as-interface paradigm rejected in [ADR-7](7_use-monaco-editor-as-the-browser-based-repl-ui.md). A tsserver process runs alongside the application; VS Code or another LSP client connects to it for completions.

- Pro: Full TypeScript language service capabilities (completions, diagnostics, go-to-definition, refactoring); battle-tested
- Con: Requires Node.js runtime to be installed; introduces a separate process with IPC overhead and lifecycle management; incompatible with the Monaco-based self-contained UI; effectively rejected as part of the [ADR-7](7_use-monaco-editor-as-the-browser-based-repl-ui.md) interface decision

### B: Monaco's built-in TypeScript language service with `addExtraLib`

- Pro: Runs entirely in the browser — no external process or runtime needed; Monaco ships with a TypeScript language service that supports completions, hover, and diagnostics out of the box; `addExtraLib` is a well-documented API for injecting additional `.d.ts` declarations; `.d.ts` generation from .NET types via reflection is straightforward (single class: `ClrDeclarationGenerator`)
- Con: Limited to Monaco's capabilities (no cross-file refactoring, no plugin extensibility like tsserver plugins); `.d.ts` generation via reflection cannot accurately represent all .NET patterns (ref/out parameters, pointers, complex overload collapse) — these fall back to `any`

### C: Build a custom completion engine on Jint's runtime object graph

- Pro: Could provide REPL-style completions based on actual runtime values (e.g. `obj.` showing live members)
- Con: No static analysis — only works for already-evaluated objects; cannot provide completions for code not yet executed; essentially a different feature (runtime inspection) rather than a language service

## Decision

Use Monaco Editor's built-in TypeScript language service for completions, injecting `.d.ts` declarations for registered .NET types via `addExtraLib`. Generate the `.d.ts` content at runtime using reflection (`ClrDeclarationGenerator`).

## Rationale

The external runtime dependency constraint is decisive. Option A (tsserver) provides the richest language service but requires Node.js, which is incompatible with the embeddability goal that drives the entire project. Option C (Jint runtime inspection) solves a different problem — it cannot provide completions for unexecuted code.

Option B leverages the TypeScript language service that Monaco already includes, requiring no additional dependencies. The `addExtraLib` API provides a clean injection point for `.d.ts` declarations. The `.d.ts` generation via reflection is necessarily imperfect — some .NET patterns (ref/out, pointers, extension methods) cannot be accurately represented in TypeScript — but the accepted trade-off is that unmappable constructs fall back to `any`, which is tolerable for a debugging/scripting tool where the user can always cast explicitly.

The design of the completion pipeline was influenced by the prior investigation (see Context), which concluded that "TypeScript LSP + `.d.ts` JIT generation" was the most viable path, and that Monaco's `addExtraLib` was the standard mechanism for this pattern.

## Consequences

- **Positive**: Completions work entirely in-browser with no external runtime; consumers register types with a single call and get editor completions immediately; the `.d.ts` generator is a single maintainable class; type declarations are updated via SSE when new types are registered
- **Negative / trade-offs**: Some .NET patterns cannot be accurately represented in TypeScript (ref/out, pointers, complex overloads collapse); reflection-based generation has a per-type runtime cost; completion capabilities are bounded by what Monaco's built-in language service supports (no tsserver plugins); if richer language service features are needed in the future, a hybrid approach (Monaco for basic completions + optional tsserver for advanced features) could be explored
