# ADR-12: Rewrite LanguageServiceHost and Provide Standard Library via CDN

## Status

Accepted

## Context

`language-service.js` initializes the TypeScript language service used by `TypeScriptService.GetCompletions`. The original implementation was written against an older TypeScript API and was missing several methods required by `LanguageServiceHost`.

This was surfaced when `GetCompletions` was exercised repeatedly in the batch mode introduced by ADR-11. The first call succeeded, but every subsequent call threw:

```
Property 'fileExists' of object is not a function
```

Inspection of `typescript.js` (5.9.3) confirmed the root cause: inside `createLanguageService`, the compiler host is assembled by directly delegating to the `LanguageServiceHost` without null-guarding `fileExists`:

```js
fileExists: (fileName) => host.fileExists(fileName),
```

Because `fileExists` was absent from `$$host`, every program rebuild (triggered when a file version changed) threw immediately.

A secondary finding was that `typescript.js` does **not** bundle the full TypeScript standard library (lib.es5.d.ts, lib.dom.d.ts, etc.). `libMap` maps library names to file names only; no file content is embedded. The only bundled lib content is `barebonesLibContent`, a minimal stub used internally by `ts.transpile` — and it is not exported from the bundle.

## Decision Drivers

- **Correctness**: `GetCompletions` must work reliably across multiple calls in a single session.
- **Completeness of host**: All methods required by `LanguageServiceHost` and its base `ModuleResolutionHost` must be implemented.
- **JS standard library completions**: Completions for JavaScript built-ins (`Array`, `string`, `Promise`, `Math`, etc.) must work. Completing only on registered .NET types is insufficient — users naturally write JS alongside .NET interop code.

## Considered Alternatives

### A: Add only `fileExists` to the existing host

- Pro: Minimal change.
- Con: Other missing methods (`readFile`, `directoryExists`, `getDirectories`, `realpath`, `useCaseSensitiveFileNames`) may surface as further runtime errors. Leaves the host in a partially-specified state.

### B: Full rewrite with `noLib: true`

- Pro: Implements every method called by `createLanguageService` as observed in source.
- Pro: Avoids attempting to load lib files that cannot be provided from the bundle alone.
- Con: No completions for JavaScript standard library globals (`Array`, `string`, `Promise`, etc.). This is a significant gap: users who type `[1,2,3].` or `"hello".` get no suggestions, making the language service only half-useful.

### C: Full rewrite + fetch standard lib from CDN

- Pro: Implements the full host interface correctly.
- Pro: JS standard library completions work.
- Con: Adds one more CDN-fetched asset (`lib.es5.d.ts`) alongside `typescript.js` and `monaco-loader.js`, but this is consistent with the existing strategy ([ADR-6](6_fetch-and-cache-runtime-js-assets-from-cdn.md)).

## Decision

Full rewrite with CDN-sourced standard library (Alternative C). The host is reimplemented as an IIFE with a private `_files` map (virtual file system) and all required methods:

- `fileExists` / `readFile` — delegate to `_files`
- `getScriptSnapshot` / `getScriptVersion` — use stored content and an integer version counter
- `directoryExists` / `getDirectories` / `realpath` — safe no-op defaults
- `useCaseSensitiveFileNames` — returns `false`
- `getCompilationSettings` — `skipLibCheck: true`, `target: ESNext`, `module: None`

`TypeScriptService` exposes a public `InjectStdLibAsync()` method that fetches `lib.es5.d.ts` from unpkg, caches it in the temp directory for 7 days, and injects it into the language service host via `$$host.addFile`. Injection is **opt-in**: the Monaco-based web REPL runs its own TypeScript language service client-side and does not need server-side standard library declarations; `InjectStdLibAsync()` is intended for callers that use `GetCompletions` directly (e.g. `Duets.Sandbox`).

## Rationale

`lib.es5.d.ts` is a self-contained declaration file (no internal cross-references) that covers the JavaScript built-ins users most commonly need completions for. Fetching it follows the same CDN-fetch-and-cache pattern already established for `typescript.js` ([ADR-6](6_fetch-and-cache-runtime-js-assets-from-cdn.md)), so it adds minimal architectural complexity. Limiting the host to `noLib: true` would make the language service only marginally useful — the combination of .NET type completions and JS standard library completions is what makes it practical.

## Consequences

- **Positive**: `GetCompletions` works correctly across any number of calls within a session.
- **Positive**: The host implementation is explicitly aligned with the `LanguageServiceHost` interface as used by TypeScript 5.9.3.
- **Positive**: JS standard library completions (`Array`, `string`, `Math`, `Promise`, etc.) work alongside registered .NET types.
- **Negative / trade-offs**: ES2015+ globals (e.g. `Map`, `Set`, `Promise`) are not covered by `lib.es5.d.ts` alone. Extending coverage to later ECMAScript editions is possible by injecting additional lib files, but is out of scope for now.
- **Note**: Standard library injection is opt-in via `InjectStdLibAsync()`. Callers that do not need `GetCompletions` (e.g. Monaco-based web REPL which handles completions client-side) do not need to call it.
