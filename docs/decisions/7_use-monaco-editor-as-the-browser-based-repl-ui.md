# ADR-7: Use Monaco Editor as the Browser-based REPL UI

## Status

Accepted

## Context

Duets needs a REPL interface through which developers write and execute TypeScript code. The interface must support TypeScript-aware editing — at minimum syntax highlighting and completions for .NET types registered by the host application. Two broad paradigms were considered: a self-contained browser-based UI that the library serves directly, and an approach that delegates the editing interface to an external tool (VS Code) while the library exposes an API endpoint.

## Decision Drivers

- **Self-contained deployment** — The REPL should work without requiring any external editor or tool to be installed and running on the developer's machine
- **TypeScript editing quality** — Syntax highlighting, completions, and basic TypeScript awareness are expected; a plain `<textarea>` is insufficient
- **Universal accessibility** — The interface should be reachable from any device with a browser, regardless of what development tools are installed
- **No additional runtime dependencies** — Consistent with the embeddability goal ([ADR-3](3_use-httplistener-instead-of-asp-net-core-kestrel.md)); the UI mechanism should not introduce external process or runtime requirements

## Considered Alternatives

### A: VS Code as the interface (library exposes API, VS Code connects as client)

In this model, Duets exposes only a backend API. A running VS Code instance connects to it — acting as the editing and display surface, with Duets driving execution via an extension or LSP-based integration.

- Pro: Developers get the full VS Code editing experience; no need to build or embed a UI; completions and language features come from VS Code's existing TypeScript support
- Con: Requires developing and publishing a dedicated VS Code extension; users must install the extension to use the REPL; the communication protocol between VS Code and the library host adds architectural complexity

### B: Browser-based Monaco Editor

Monaco Editor is the editor component that powers VS Code. It runs entirely in a browser and provides VS Code-equivalent TypeScript editing capabilities as a self-contained web page served by the library.

- Pro: Works in any browser with no installation; Monaco provides VS Code-level TypeScript editing out of the box; the REPL UI is embedded as static resources within the library assembly; accessible from any device that can reach the host application's HTTP port
- Con: Requires an HTTP server; Monaco must be loaded from a CDN or cache; adding a web UI introduces front-end assets (HTML, JS) into the library

### C: Terminal REPL (readline-style)

A terminal-based REPL is not a true alternative for this decision — it addresses a different interface modality and does not solve the TypeScript-aware editing and completion problem. A terminal REPL could be provided alongside the web UI as a complementary interface.

## Decision

Use Monaco Editor as a browser-based REPL UI, served as embedded static resources by the library.

## Rationale

The VS Code approach (A) provides the richest possible editing experience but requires VS Code to be installed and running on the developer's machine, plus a dedicated extension to be developed and installed. This is an external dependency that adds friction and conflicts with the self-contained deployment goal. A self-contained web UI (B) requires nothing beyond a browser, which is universally available.

Monaco Editor is the natural choice for a browser-based TypeScript editor: it is the editor engine behind VS Code, providing equivalent editing quality, and it has first-class TypeScript language service integration that enables `.d.ts`-based completions. No other browser-based editor component was evaluated — Monaco is the de facto standard for this use case, and its TypeScript integration directly enabled the completion mechanism without additional infrastructure.

## Consequences

- **Positive**: REPL is accessible from any browser without additional installation; Monaco delivers VS Code-level TypeScript editing in a self-contained page; the web-based interface can host additional views (diagnostics, type browser, etc.) in the future
- **Negative / trade-offs**: Requires an HTTP server component and CDN-fetched assets; the library carries embedded web assets (HTML, JS); Duets is coupled to Monaco's API and release cadence
