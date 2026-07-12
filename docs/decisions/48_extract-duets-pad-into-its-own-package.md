# ADR-48: Extract DuetsPad into Its Own Package (`Duets.Pad`)

## Status

Accepted — partially supersedes [ADR-23](23_ci-and-package-publishing.md) (package topology,
`IsPackable` set, signing scope) and [ADR-16](16_samples-directory-and-sandbox-role-clarification.md)
(flat samples layout)

## Context

ADR-32 introduced DuetsPad inside the core `Duets` assembly, under the `Duets.Pad` namespace. Since
then, ADR-33 through ADR-47 grew the pad into the repository's largest subsystem: roughly seventy
source files plus about 4,300 lines of embedded web assets (HTML/CSS/JS), against a core library of
about twenty source files. The parent had become smaller than the child, which showed up as
documentation pressure (the DuetsPad portion of `architecture.md` dwarfed everything else) and as a
structural contradiction: the architecture's own core constraint — embeddability in constrained
hosts such as Unity, Godot, and mobile — was carried by a core assembly that bundled an HTTP server
dependency (HttpHarker) and a browser application's static assets into every embedder, including
hosts that only want eval and type registration.

The project is pre-1.0 with effectively no external consumers, so a packaging change is at its
cheapest point: the cost of moving public types across assemblies only grows once adoption starts.

A seam audit performed before the extraction found that the pad consumed **only public core API**
(including the `TaggedTemplateRegistry` helpers, which are public); no `InternalsVisibleTo` from
core to pad would be required.

## Decision Drivers

- **Minimal core footprint.** Hosts that embed only eval/typings should not carry HttpHarker, the
  pad's embedded web assets, or (on netstandard2.1) the `System.Text.Json` /
  `System.Threading.Channels` package references that only the pad needs.
- **Source compatibility for pad consumers.** The pad already lived in a distinct `Duets.Pad`
  namespace, so an assembly move need not be a source-level break.
- **Divergent change velocity.** The pad churns rapidly (rendering/protocol ADRs 35–47); the core
  session API is comparatively stable.
- **No encapsulation loss.** The extraction must not force `InternalsVisibleTo` from core to pad or
  otherwise weaken the core's internal boundaries.
- **Low process cost.** CI, versioning (Nerdbank.GitVersioning), and package publishing (ADR-23
  packs the whole solution) should keep working without rework.

## Considered Alternatives

### A: Keep DuetsPad inside `Duets`; restructure documentation only

- Pro: No breaking change, no new package, zero migration work
- Pro: The documentation-pressure symptom is solvable by splitting docs alone
- Con: Every embedder keeps carrying HttpHarker plus the web assets — the contradiction with the
  core embeddability constraint remains and deepens as the pad grows
- Con: The split only gets more expensive later; after NuGet adoption it becomes a hard breaking
  change for every consumer rather than a near-free one

### B: Extract DuetsPad into a separate repository

- Pro: Strongest isolation; fully independent release cadence and issue tracking
- Con: Pad protocol work regularly rides on core API changes in the same change set (e.g. ADR-44
  touched the session's completion registry and the pad's RPC endpoint together); cross-repo
  coordination would tax exactly the kind of change this project makes most often
- Con: Shared test support, samples, and the single-solution build/test loop would fragment
- Con: Premature while the pad's protocol and core APIs are still co-evolving

### C: Extract a `Duets.Pad` project/package within the same solution *(chosen)*

- Pro: Core sheds HttpHarker, the embedded web assets, and the pad-only netstandard2.1 package
  references; netstandard2.1 core needs only PolySharp
- Pro: Namespace `Duets.Pad` is unchanged — existing pad consumers fix a package reference and
  recompile; no `using` changes
- Pro: One solution keeps atomic cross-cutting commits, one test run, one CI pipeline; solution-wide
  `dotnet pack` picks the new package up with no workflow change
- Con: One more packable project to version and publish
- Con: A breaking change for existing consumers who got the pad transitively via `Duets`
- Con: Test access to internals requires additional `InternalsVisibleTo` grants

## Decision

Extract DuetsPad into a new `src/Duets.Pad` project producing the NuGet package **`Duets.Pad`**,
which depends on `Duets` and `HttpHarker`. Specifics:

- The `Duets.Pad` **namespace is unchanged**; only the assembly/package moves.
- The pad's embedded web assets move with it; their manifest namespace becomes
  `Duets.Pad.Resources.StaticFiles`. Core keeps `language-service.js` and the
  `ScriptEngineInit` resources.
- Core (`Duets`) drops its HttpHarker project reference and the netstandard2.1-only
  `System.Text.Json` / `System.Threading.Channels` references, which move to `Duets.Pad`.
- No `InternalsVisibleTo` from `Duets` to `Duets.Pad`: the pad builds against public core API only.
- Pad tests move to a new `tests/Duets.Pad.Tests` project. Test-support sources needed by both test
  projects (`JintTestRuntime`, `IdentityTranspiler`) live in `tests/shared/` and are compiled into
  each test assembly via `<Compile Include>`, mirroring the `src/shared` convention.
  `DuetsPadProtocolClient` stays in `Duets.Sandbox` as the protocol's reference client;
  `Duets.Sandbox` and `Duets.Jint` grant `InternalsVisibleTo` to `Duets.Pad.Tests`.
- `samples/` is grouped per package (`samples/Duets/`, `samples/Duets.Pad/`, `samples/HttpHarker/`),
  partially superseding the flat `samples/<file>.cs` layout of ADR-16 (the directory's role and the
  file-based-app format are unchanged), and detailed DuetsPad documentation moves from the
  repository README to `src/Duets.Pad/README.md`, parallel to HttpHarker's per-package README.

This partially supersedes ADR-23's package topology, `IsPackable` opt-in set, and signing scope,
which described two packages (`Duets`, `HttpHarker`); ADR-27 had already added `Duets.Jint` without
recording the topology change. The packable set is now these **four packages**, each opting in with
`IsPackable=true` and strong-named in Release builds, with this dependency graph (arrows are NuGet
dependencies at matching versions):

| Package | Depends on |
|---|---|
| `Duets` | — |
| `HttpHarker` | — |
| `Duets.Jint` | `Duets` |
| `Duets.Pad` | `Duets`, `HttpHarker` |

ADR-23's CI/publish workflow itself — single `publish.yml`, snapshot on push to `main`, release on
`v*` tags, solution-wide `dotnet pack` — remains in force and covers the new package with no
workflow change.

## Rationale

The seam audit is what makes option C nearly free: unlike ADR-27's backend split, which had to
design new abstractions (`IScriptEngine`, `ScriptValue`) to create a boundary, the pad/core boundary
already existed as public API — the extraction materializes an existing layering rather than
inventing one. Given that, alternative A amounts to knowingly preserving a contradiction with the
project's own core constraint to avoid a cheap move, and its main virtue (avoiding a breaking
change) is worth the least it will ever be worth right now, pre-adoption.

Alternative B optimizes for an isolation the project does not yet need and pays for it in the
currency the project spends most — atomic changes that touch pad protocol and core session API
together. The same-repo package keeps that loop intact while still delivering the dependency and
footprint separation. Like HttpHarker, `Duets.Pad` may still be extracted to its own repository
later; this decision does not foreclose that.

## Consequences

- **Positive**:
  - The core `Duets` assembly matches the embeddability constraint again: no HTTP server
    dependency, no web assets, and a single netstandard2.1 polyfill package
  - The pad is versioned and documented as the product-sized subsystem it has become
    (`src/Duets.Pad/README.md`, per-package samples)
  - The dependency direction core ← pad is now compiler-enforced rather than convention
- **Negative / trade-offs**:
  - Existing pad consumers must add a `Duets.Pad` package reference (namespace unchanged, so no
    code edits)
  - One more package in the publish set (no CI change needed; ADR-23's solution-wide pack applies)
  - `InternalsVisibleTo` grants for `Duets.Pad.Tests` widen internals exposure of `Duets.Jint` and
    `Duets.Sandbox` to one more test assembly
  - `README.md`, `AGENTS.md`, and `docs/architecture.md` must track the new module layout (updated
    with this change)
