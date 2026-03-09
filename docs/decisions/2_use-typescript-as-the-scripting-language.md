# ADR-2: Use TypeScript as the Scripting Language

## Status

Accepted

## Context

Duets needs a scripting language for the REPL. The language must be familiar to developers, interoperate naturally with .NET types, and be implementable without heavy external dependencies. A prior attempt at building a custom domain-specific language for a similar tool was abandoned — users prefer a known language over learning a proprietary one.

## Decision Drivers

- **Familiarity** — Users should not need to learn a new language; an existing, widely-known language lowers the adoption barrier
- **.NET type system interoperability** — The language must work naturally with typed .NET APIs; ideally, the type system on the scripting side reflects .NET types to enable meaningful completions
- **Ecosystem maturity** — Standard library, tooling, and community resources should be readily available
- **Embeddability without heavy dependencies** — The language runtime must remain embeddable in constrained environments (mobile, game engines); large framework dependencies are not acceptable
- **Clear implementation path** — The mechanism for running the language on .NET must be well understood and viable before committing

## Considered Alternatives

### A: Custom/proprietary language

- Pro: Full control over syntax and semantics; can be tailored to .NET interop
- Con: Users must learn a new language (high adoption barrier); tooling (editors, completions, documentation) must be built from scratch; prior attempt confirmed this is not viable

### B: C# scripting (Microsoft.CodeAnalysis.Scripting / Roslyn)

- Pro: Native .NET language; perfect type system alignment; developers targeting .NET likely already know C#
- Con: Roslyn is a massive dependency; unavailable or non-functional in constrained runtimes (mobile, game engines); conflicts with the embeddability goal

### C: Lua

- Pro: Lightweight; commonly embedded in game engines (a key target environment for Duets); small runtime footprint
- Con: Dynamic typing makes .NET interop documentation and completions difficult; the language spec is permissive in ways that complicate mapping to .NET's type system; existing .NET+Lua libraries focus on app-level DSL scripting (where deep .NET interop is not a goal), not on the interactive debugging/inspection use case

### D: Plain JavaScript

- Pro: Ubiquitous; no compilation step; JS engine can execute it directly
- Con: Dynamic typing provides no static information about .NET types; completions and type-aware editing are not feasible without a type layer on top

### E: TypeScript

- Pro: Superset of JavaScript — familiar to a large developer population; static type system maps naturally to .NET types, enabling meaningful editor completions; compiles to JavaScript, meaning the implementation path (TS compiler → JS → Jint) is clear and well-understood; mature ecosystem and tooling
- Con: Requires running the TypeScript compiler at runtime (startup cost, additional complexity); the TS type system cannot perfectly represent all .NET patterns (ref/out, overloads, generics edge cases)

## Decision

Use TypeScript as the scripting language. TypeScript source is transpiled to JavaScript at eval time by the official TypeScript compiler running on a pure .NET JavaScript engine.

## Rationale

Custom languages (A) are ruled out by prior experience. C# scripting (B) is ruled out by the dependency and platform constraints that define the project. Lua (C) was briefly considered given its game engine prevalence, but its loose type system and the focus of existing .NET+Lua libraries on a different use case make it unsuitable. Plain JavaScript (D) lacks the type information needed for meaningful .NET interop completions.

TypeScript (E) combines JavaScript's familiarity with a static type system that aligns with .NET's typed API surface. The transpile-to-JavaScript design was an important reassurance at decision time: the implementation path was well understood (official TS compiler runs as JS, Jint executes the output), reducing technical risk. The typed nature of TypeScript also enables a `.d.ts`-based completion system in the editor.

## Consequences

- **Positive**: Developers familiar with TypeScript or JavaScript can use Duets with minimal onboarding; the TS type system enables editor completions for registered .NET types; the TypeScript ecosystem (documentation, tooling, community) is available to users
- **Negative / trade-offs**: TypeScript compiler startup has a non-trivial cost (~5 MB JS, parsed on Jint); TS type system cannot fully represent all .NET patterns; Duets is tied to TypeScript's evolution and Jint's ES spec coverage
