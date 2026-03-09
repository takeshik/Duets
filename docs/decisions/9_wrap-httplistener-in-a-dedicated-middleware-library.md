# ADR-9: Wrap HttpListener in a Dedicated Middleware Library

## Status

Accepted

## Context

After deciding to use `System.Net.HttpListener` ([ADR-3](3_use-httplistener-instead-of-asp-net-core-kestrel.md)), the REPL web interface was initially built directly on the raw `HttpListener` API. This worked for the initial feature set, but it became clear that raw `HttpListener` usage would not scale for future needs — adding features required increasingly ad-hoc request dispatching, and hosting additional web interfaces alongside the REPL (e.g. diagnostics pages) would compound the complexity.

The design of [Rin](https://github.com/mayuki/Rin), an ASP.NET Core request/response inspector that serves its web UI as a middleware component, influenced the approach. Providing the REPL web interface as a middleware rather than owning the entire HTTP server seemed like a more composable design. This led to extracting the HTTP infrastructure into a middleware-based library, with the REPL becoming one middleware among potentially many.

## Decision Drivers

- **Composability** — The REPL web interface should be a middleware component, not the sole owner of the HTTP server; other web interfaces should be hostable on the same server
- **Separation of concerns** — Duets' core logic (TypeScript transpilation, code execution, type declaration generation) should not be coupled to HTTP infrastructure
- **Reusability** — The HTTP server layer is general-purpose and may be useful beyond Duets
- **Future extractability** — The library should be structured so it can be published as an independent package without major refactoring

## Considered Alternatives

### A: Use raw HttpListener directly within the Duets project

- Pro: Simpler project structure; no separate assembly; no abstraction overhead
- Con: Request dispatching becomes ad-hoc as features grow; hosting multiple web interfaces requires manual multiplexing; HTTP utilities are coupled to Duets; cannot be reused independently

### B: Separate middleware-based library within the same solution

- Pro: Independent project with its own namespace; middleware pipeline enables composable web interface hosting; can be extracted to its own repo/package later; clear dependency direction (Duets depends on the library, not vice versa)
- Con: Additional project to maintain; must keep the API general-purpose rather than optimizing for Duets' specific needs

## Decision

Extract the HTTP infrastructure into a separate middleware-based library within the Duets solution, with no dependency on Duets.

## Rationale

The raw `HttpListener` approach (option A) worked initially but showed clear scaling limitations — adding routes and hosting multiple interfaces required increasingly tangled dispatch logic. Inspired by Rin's pattern of serving a web UI as an ASP.NET Core middleware, the REPL web interface was restructured as a middleware component. This required a middleware pipeline, which naturally became a separate library (option B) since the HTTP infrastructure is generic and not specific to Duets. The clear dependency direction (the HTTP library knows nothing about Duets) makes future extraction straightforward.

## Consequences

- **Positive**: Clean dependency graph; REPL web interface is a composable middleware rather than a monolithic HTTP handler; additional web interfaces can be added without rearchitecting; the library can be extracted to its own repository and NuGet package when ready
- **Negative / trade-offs**: An additional project to maintain; the library's API must remain general-purpose, which may occasionally require workarounds for Duets-specific needs
