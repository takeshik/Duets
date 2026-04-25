# ADR-29: JSDoc Provider Abstraction for CLR Declaration Generator

## Status

Accepted

## Context

`ClrDeclarationGenerator` generates TypeScript type declarations for .NET CLR types. Previously these
declarations contained only structural information derived from reflection (member names, parameter types,
return types). No prose documentation was included.

Adding .NET XML documentation to the generated TSDoc comments would expose summaries, `@param`, and
`@returns` annotations in editor IntelliSense. However, XML documentation can come from multiple sources:

- An `.xml` file adjacent to the assembly on disk (produced at build time).
- A NuGet package downloaded at runtime (for third-party or framework assemblies).
- In principle, any other future source (embedded resources, remote endpoints, etc.).

Furthermore, NuGet fetches are asynchronous. Docs may arrive after the first batch of type declarations
has already been registered, requiring those declarations to be regenerated.

## Decision Drivers

- Support multiple documentation sources without forcing callers to aggregate them manually.
- Isolate fetch/parse failures per source so that one broken provider does not silence the others.
- Allow docs to be added incrementally (async NuGet download) and trigger declaration refresh.
- Keep the `Duets` core package free of runtime-specific policy (e.g. hardcoded NuGet URLs).

## Considered Alternatives

### A: Accept raw XML string directly in `ClrDeclarationGenerator`

Pass the XML documentation content as a constructor argument or method parameter on the generator
itself.

- Pro: Simple — one concept, no extra interfaces.
- Con: Only one source at a time; caller must aggregate XML from multiple assemblies before constructing
  the generator.
- Con: Async loading becomes the caller's responsibility with no refresh mechanism.
- Con: A parse failure in one XML file would either propagate or require the caller to handle it.

### B: `IJsDocProvider` interface with `JsDocProviders` composite

Define `IJsDocProvider` with a single `Get(MemberInfo) → string?` method. Provide:

- `XmlDocumentationProvider` — parses an XML documentation string; optionally downloads and caches a
  NuGet nupkg to extract the XML file.
- `JsDocProviders` — composite registry that holds multiple providers and tries them in registration
  order, returning the first non-null result. Exposes a `ProviderAdded` event for declaration refresh.

- Pro: Composable — any number of sources, added independently.
- Pro: Per-provider exception isolation; one failing source does not affect others.
- Pro: `ProviderAdded` event decouples the async NuGet fetch from the declaration refresh mechanism.
- Pro: Callers can implement custom providers without changing core types.
- Con: More types to introduce compared to option A.

## Decision

Option B: `IJsDocProvider` interface with `JsDocProviders` composite (`JsDocProviders`, `XmlDocumentationProvider`).

## Rationale

Multiple documentation sources are a realistic and expected use case — a session may register types
from different assemblies, some with local XML files and others requiring NuGet downloads. A single XML
string cannot represent this multiplicity.

The async timing problem is significant: NuGet downloads complete after session initialization, so docs
arrive after initial type declarations are already registered. The `ProviderAdded` event on `JsDocProviders`
lets `DuetsSession` wire a single `TypeDeclarations.RefreshDeclarations` callback once, without
`JsDocProviders` needing to know about `TypeDeclarations`. This keeps the dependency direction correct.

Per-provider exception isolation (`try/catch` per provider in `JsDocProviders.Get`) is a reliability
requirement: transient parse failures or network errors in one provider must not degrade completions for
other registered types.

## Consequences

- **Positive**: Documentation from any number of sources (local files, NuGet, custom) can be added in
  any order without restructuring the session.
- **Positive**: Async NuGet fetch is safe; declarations are refreshed automatically when docs become
  available.
- **Positive**: Custom `IJsDocProvider` implementations (e.g. fetching from an internal docs API) can
  be added without modifying core types.
- **Negative / trade-offs**: `ProviderAdded` creates a temporal coupling — callers must wire the event
  before any providers are added, or they risk missing the initial registration. `DuetsSession` handles
  this by wiring the callback during construction before exposing `JsDocProviders` to user code.
