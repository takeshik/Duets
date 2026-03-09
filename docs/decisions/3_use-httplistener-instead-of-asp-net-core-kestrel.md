# ADR-3: Use HttpListener Instead of ASP.NET Core / Kestrel

## Status

Accepted

## Context

Duets provides a web-based REPL UI that requires an HTTP server to serve static assets, handle eval requests, and stream type declarations via SSE. The choice of HTTP server technology determines the minimum dependency footprint imposed on host applications.

## Decision Drivers

- **Universal embeddability** — Duets targets any .NET application: mobile (iOS, Android, MAUI), game engines (Unity, Godot), desktop, and server. The HTTP layer must not prevent embedding in any of these environments.
- **Minimal dependency footprint** — Host applications should not be forced to pull in large framework dependencies they do not otherwise need.
- **Sufficient functionality** — The server must support basic routing, static file serving, SSE, and concurrent request handling.

## Considered Alternatives

### A: ASP.NET Core / Kestrel

- Pro: Feature-rich, high performance, widely documented, middleware ecosystem
- Con: Massive dependency graph; not available or practical in Unity, Godot, or mobile .NET runtimes; forces the host into ASP.NET Core's hosting model

### B: System.Net.HttpListener

- Pro: Part of the base class library; zero additional dependencies; available on all .NET runtimes that support the BCL; simple API sufficient for Duets' needs
- Con: Lower-level API; no built-in routing, middleware, or static file serving; platform-specific quirks (e.g. requires URL ACL reservation on Windows)

### C: Third-party lightweight server (e.g. EmbedIO)

- Pro: More features than raw HttpListener; smaller than ASP.NET Core
- Con: Additional NuGet dependency; less control over behavior; risk of abandonment

## Decision

Use `System.Net.HttpListener` as the HTTP server foundation, with a thin middleware pipeline (HttpHarker) built on top to provide routing and static file serving.

## Rationale

Universal embeddability is the top priority. ASP.NET Core's dependency graph makes it impractical or impossible in constrained environments like Unity and mobile runtimes, which rules out option A despite its maturity. Option C adds a third-party dependency for features that are straightforward to build on HttpListener. Option B satisfies all three drivers: it is universally available, adds zero dependencies, and provides enough low-level capability to build the required functionality.

## Consequences

- **Positive**: Duets can be embedded in any .NET application without imposing framework dependencies on the host; the HTTP layer remains simple and auditable
- **Negative / trade-offs**: Missing conveniences (routing, middleware, static files) must be built manually; HttpListener has platform-specific behaviors that may require workarounds
