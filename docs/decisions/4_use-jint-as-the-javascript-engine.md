# ADR-4: Use Jint as the JavaScript Engine

## Status

Accepted

## Context

Duets needs a JavaScript engine to run the official TypeScript compiler (`typescript.js`) and to execute user scripts (transpiled JS). The engine must be embeddable in any .NET application, including mobile and game engine environments where native code restrictions may apply.

Jint was already in use when this decision was revisited. The goal was to confirm whether a more suitable alternative existed among the available .NET-embeddable JavaScript engines.

## Decision Drivers

- **Pure .NET implementation** — No native binaries; must work on all platforms where .NET runs, including AOT-compiled environments and platforms with JIT restrictions (iOS)
- **ECMAScript spec compliance** — Must execute the full TypeScript compiler (`typescript.js`, ~5 MB) which requires broad ES2015+ support; the engine's JS spec coverage directly limits what Duets can run
- **CLR interop** — User scripts need to call .NET methods and access .NET objects; the engine must support exposing host objects naturally
- **Maturity and active maintenance** — The engine must be actively maintained to track evolving JS/ES standards, and mature enough for production use
- **Sandboxing capabilities** — Ability to impose execution limits (time, statement count, recursion depth) is a positive factor, especially for isolating the compiler engine from user code

## Considered Alternatives

### A: Jint

- Pro: Pure C# implementation; no native dependencies; strong CLR interop (`AllowClr`); actively maintained; ES2015+ support via Acornima parser; explicit sandboxing support (execution limits, memory constraints)
- Con: Slower than native engines for compute-heavy workloads; large scripts (like `typescript.js`) have noticeable parse/startup time

### B: V8 via ClearScript or similar bindings

- Pro: Full spec compliance; excellent performance; battle-tested
- Con: Requires native V8 binaries per platform; large binary size; incompatible with many mobile/game engine runtimes; complicates deployment and AOT scenarios

### C: Other managed engines (Jurassic, NiL.JS, YantraJS)

Several other pure C# JavaScript engines exist:

- **Jurassic** — Emphasizes standard compliance and performance, but has less active maintenance and weaker ES2015+ support than Jint
- **NiL.JS** — Declares ES2015 support; a viable managed alternative but less widely adopted and with weaker CLR interop story than Jint
- **YantraJS** — Another managed engine; less mature ecosystem

- Pro: Pure C# like Jint; no native dependencies
- Con: Each has gaps relative to Jint in some combination of ES spec coverage, CLR interop, maintenance activity, or sandboxing support; none were evaluated beyond information gathering as Jint already satisfied all drivers

### D: Node.js hosting (Jering.Javascript.NodeJS, etc.)

- Pro: Full Node.js ecosystem access; complete ES/TS support; can run npm packages directly
- Con: Requires Node.js runtime to be installed; introduces a process-boundary dependency; fundamentally incompatible with the embeddability goal (mobile, game engines, single-process deployment)

## Decision

Use Jint as the JavaScript engine for both the TypeScript compiler and user code execution.

## Rationale

The pure .NET implementation requirement is the strongest driver, as it directly enables the universal embeddability goal (see [ADR-3](3_use-httplistener-instead-of-asp-net-core-kestrel.md)). This rules out V8 bindings (option B) and Node.js hosting (option D), both of which introduce native or external runtime dependencies incompatible with the target environments.

Among pure .NET engines (options A and C), Jint has the strongest combination of ES spec coverage, CLR interop, active maintenance, and community adoption. The other managed engines were surveyed but not evaluated in depth, as Jint — already in use — satisfied all decision drivers. Jint's built-in sandboxing support (execution time limits, statement count limits, recursion depth) is a useful bonus that supports engine isolation between the compiler and user code, though it was not the primary selection criterion.

## Consequences

- **Positive**: Duets works on any platform that runs .NET, with no native binary management; Jint's `AllowClr` provides natural .NET interop for user scripts; sandboxing features enable safe compiler engine isolation
- **Negative / trade-offs**: TypeScript compiler startup is slower than with V8; compute-heavy user scripts may hit performance limits; Duets is coupled to Jint's ES spec support and bug surface; if Jint's spec coverage proves insufficient in the future, switching to another managed engine would require significant effort
