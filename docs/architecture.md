# Architecture Overview

Duets is an embeddable TypeScript console for .NET. It is designed to be added to any .NET application — including
mobile, game engines, and other constrained environments — for live debugging and runtime scripting. The scripting
language is TypeScript ([ADR-2](decisions/2_use-typescript-as-the-scripting-language.md)), which transpiles to
JavaScript at eval time.

## Core Design Constraint

**No ASP.NET Core / Kestrel dependency.** Duets must remain embeddable in hosts that cannot or should not pull in the ASP.NET Core stack (e.g. Unity, Godot, .NET iOS/Android). The HTTP layer is built on `System.Net.HttpListener` via the HttpHarker library ([ADR-3](decisions/3_use-httplistener-instead-of-asp-net-core-kestrel.md), [ADR-9](decisions/9_wrap-httplistener-in-a-dedicated-middleware-library.md)).

## Module Structure

### Duets (core library)

The main library consists of the following components:

- **DuetsSession** — Canonical entry point and top-level context ([ADR-25](decisions/25_session-as-canonical-entry-point.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). Owns `TypeDeclarations`, the active `ITranspiler`, and an abstract `ScriptEngine` as a unit. The core package does not choose a runtime backend by itself; callers configure the backend through `DuetsSessionConfiguration` extensions supplied by backend packages.
- **TypeDeclarations** — Thread-safe, transpiler-agnostic runtime store for type declarations ([ADR-25](decisions/25_session-as-canonical-entry-point.md)). Owns CLR type registration, namespace placeholders, raw `.d.ts` registration, and change notifications. Exposes two narrow views: `ITypeDeclarationProvider` (snapshot + change events) and `ITypeDeclarationRegistrar` (registration commands). Uses `ClrDeclarationGenerator` internally.
- **ClrDeclarationGenerator** — Uses reflection to generate TypeScript type declarations (`.d.ts`) from .NET types. Called by `TypeDeclarations` when a CLR type is registered ([ADR-8](decisions/8_use-addextralib-to-inject-dts-declarations-for-completions.md)).
- **ITranspiler** — Engine-neutral transpilation boundary ([ADR-10](decisions/10_extract-itranspiler-interface-for-scriptengine.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). Concrete implementations may be hosted by different JavaScript runtimes or replaced by future wasm-backed approaches.
- **ScriptEngine** — Abstract runtime-neutral execution facade ([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). `Execute` and `Evaluate` always transpile before running, track `$_` and `$exception`, expose console events, and surface runtime values through `ScriptValue` instead of engine-specific value types.
- **ScriptValue** — Runtime-neutral wrapper around a JavaScript value ([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)). Provides the minimal cross-runtime operations Duets needs (`IsUndefined`, `IsNull`, `IsObject`, `ToObject`, display string).
- **ReplService** — Wires everything together into a web-based REPL ([ADR-7](decisions/7_use-monaco-editor-as-the-browser-based-repl-ui.md)). Serves the Monaco editor UI as embedded resources, provides an SSE endpoint for live type declaration updates, and a `POST /eval` endpoint that transpiles and executes code. Depends on `ITypeDeclarationProvider` for the declaration SSE stream, not on a specific runtime backend.

### Duets.Jint

The Jint integration package contains the existing Jint-centered implementation, now outside the core package
([ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)):

- `JintScriptEngine`
- `TypeScriptService`
- `BabelTranspiler`
- `ScriptTypings`
- `ExtensionMethodRegistry`
- `DuetsSessionConfigurationExtensions`

Jint remains the backend with full extension-method support via `MemberAccessor`
([ADR-26](decisions/26_extension-method-support-via-member-accessor-hook.md)).

### HttpHarker (HTTP server library)

A lightweight HTTP server built on `System.Net.HttpListener` with a middleware pipeline ([ADR-9](decisions/9_wrap-httplistener-in-a-dedicated-middleware-library.md)). It is a separate library with its own namespace and may be extracted into its own repository in the future. See [../src/HttpHarker/README.md](../src/HttpHarker/README.md) for details.

### Duets.Sandbox (developer / agent debugging CLI)

An internal console application for end-to-end verification of the Duets stack.
It is not intended for end users or as a deliverable ([ADR-11](decisions/11_sandbox-multi-mode-debugging-cli.md), [ADR-16](decisions/16_samples-directory-and-sandbox-role-clarification.md)). All commands run against a fully-initialized TypeScript engine with stdlib, `typings` built-ins, and `AllowClr`. Modes:

| Mode | Invocation | Description |
|---|---|---|
| `repl` | *(default)* | Interactive REPL; TypeScript lines are evaluated, `:commands` manage state |
| `complete` | `complete <src> [--position n]` | One-shot completions at position; outputs a JSON object |
| `serve` | `serve [--port n]` | Starts the web REPL server; blocks until Ctrl+C |
| `batch` | `batch` | JSONL in → JSONL out; agent-friendly stateful session |

The batch mode is designed for use by AI coding agents: the agent writes a sequence of JSON operation objects to stdin and reads JSON results from stdout, with no background process management required.

At the moment the sandbox still defaults to the Jint backend; the package split in ADR-27 makes backend selection
possible, but a runtime-selection CLI surface is follow-up work.

### samples/ (usage examples)

Runnable file-based app examples (`.cs` files at repository root level) showing standard library usage ([ADR-16](decisions/16_samples-directory-and-sandbox-role-clarification.md)). Each file is self-contained and executable via `dotnet run samples/<file>.cs`. These are the recommended starting point for new users.

## Data Flow

### Eval (`POST /eval`)

```mermaid
flowchart LR
    U["User\n(Monaco Editor)"]
    RS[ReplService]
    TS["ITranspiler\n(runtime-hosted)"]
    SE["ScriptEngine\n(runtime backend)"]

    U -->|"POST /eval\nTypeScript source"| RS
    RS -->|Transpile| TS
    TS -->|JavaScript source| RS
    RS -->|Evaluate| SE
    SE -->|result / error| RS
    RS -->|"JSON { result, ok }"| U
```

### Type Registration (`SSE /type-declaration-events`)

```mermaid
flowchart LR
    Host["Host app\nRegisterType(typeof(T))"] -->|register| TD[TypeDeclarations]
    TD -->|generate| CG["ClrDeclarationGenerator\n.NET type → .d.ts"]
    TD -->|"change event"| Monaco["Monaco Editor\n(addExtraLib)"]
    TD -->|"optional mirror"| TS["TypeScriptService\n(language service)"]
```

## Runtime Dependencies

TypeScript compiler (`typescript.js`), Monaco Editor loader (`loader.js`), and optionally the ES5 standard library
(`lib.es5.d.ts`) are fetched from unpkg on first use and cached in the system temp directory for 7 days
([ADR-6](decisions/6_fetch-and-cache-runtime-js-assets-from-cdn.md), [ADR-18](decisions/18_pluggable-asset-source-abstraction.md)).
This avoids bundling large JS files in the library assembly. `lib.es5.d.ts` is only fetched when a runtime-hosted
`TypeScriptService` injects it for server-side completions ([ADR-12](decisions/12_language-service-host-rewrite-and-nolib.md)).

## Versioning and CI

Versions are managed by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) ([ADR-17](decisions/17_versioning-strategy-and-ci.md)). Releases are triggered by `v{major}.{minor}.{patch}` Git tags and publish a NuGet package to GitHub Packages. Development builds carry a `-dev.{height}+g{commit}` prerelease suffix (SemVer 2.0).

## Key Dependencies

| Package | Role |
|---|---|
| [Jint](https://github.com/sebastienros/jint) | JavaScript runtime backend used by `Duets.Jint` ([ADR-4](decisions/4_use-jint-as-the-javascript-engine.md), [ADR-27](decisions/27_split-javascript-runtime-backends-from-duets-core.md)) |
| [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) | Automated versioning from Git history and tags ([ADR-17](decisions/17_versioning-strategy-and-ci.md)) |
