# ADR-55: Scale Architecture Documentation by Purpose and Module

## Status

Accepted — partially supersedes [ADR-1](1_design-documentation-strategy.md) (singleton architecture snapshot form
and path)

## Context

ADR-1 established three documentation layers: `AGENTS.md` as the agent entry point, `docs/architecture.md` as the
current architecture snapshot, and `docs/decisions/` as the append-only decision history. That division kept the
entry point small while giving current state and historical rationale distinct owners.

The singleton architecture snapshot no longer scales as its second layer. DuetsPad has grown into the repository's
largest subsystem and its rendering, state, HTTP/SSE protocol, and security contracts now dominate the file. ADR-48
already identified this documentation pressure when DuetsPad became the separate `Duets.Pad` package. The same file
also contains the core and Jint component inventories, the HttpHarker boundary, developer-tool descriptions, and
packaging notes. A reader interested in one module must therefore load and navigate unrelated detail.

Package-facing guidance already has a different owner: `src/Duets.Pad/README.md` and `src/HttpHarker/README.md`
explain how to consume those packages. Splitting architecture documentation must preserve that distinction rather
than creating competing user guides.

The repository is browsed directly on GitHub and has no generated documentation site. Its established project and
package identifiers include dots and Pascal casing, such as `Duets.Pad` and `Duets.Jint`.

## Decision Drivers

- **Selective reading** — Humans and agents should be able to load one module's architecture without unrelated
  subsystem detail.
- **Whole-system orientation** — A short repository-wide view must continue to explain boundaries and dependency
  direction.
- **Clear ownership** — Current architecture, package usage, and historical rationale must not compete as canonical
  sources.
- **Naming consistency** — Documentation paths representing modules should use the identifiers already used by the
  solution, projects, packages, namespaces, tests, and samples.
- **Proportional structure** — Large subsystems need room to split without forcing placeholder directories on small
  modules.
- **Low maintenance burden** — The structure must remain useful without adding a documentation-site tool or generated
  navigation.
- **Stable history** — Existing ADR paths and historical text must remain intact.

## Considered Alternatives

### A: Retain one architecture file

- Pro: Preserves every existing path and requires no navigation layer.
- Pro: Keeps the architecture snapshot mechanically simple.
- Con: Selective reading continues to degrade as DuetsPad and future subsystems grow.
- Con: Unrelated concerns remain coupled to one file, encouraging either an oversized overview or omitted detail.

### B: Create package directories directly below `docs/`

- Pro: Gives each package an obvious place to accumulate documentation.
- Pro: Resembles the source and package topology.
- Con: It organizes by implementation owner before reader purpose, mixing architecture, guides, operations, and API
  material unless another convention is added inside every package directory.
- Con: It would introduce path spellings such as `duets-pad` that are not repository identifiers, or expose unusual
  dotted and cased names at the top documentation level without saying what kind of documentation they contain.
- Con: It encourages symmetric directories even when a module has only one short architecture description.

### C: Adopt a published product-documentation hierarchy

- Pro: A site generator could provide navigation, aliases, and conventional `index.md` pages.
- Pro: Product areas could own extensive guides and API reference independently.
- Con: The repository does not currently publish a documentation site, so the toolchain and navigation metadata would
  be overhead without an established consumer.
- Con: This does not by itself resolve the boundary between contributor architecture and co-located package guidance.

### D: Organize by purpose first and module second

- Pro: `architecture/` and `decisions/` state why a document exists before identifying its module.
- Pro: A repository-wide architecture landing can link selectively to module pages.
- Pro: Only DuetsPad needs a nested architecture directory today; smaller modules can remain single pages.
- Pro: GitHub renders a directory `README.md` automatically, so no site generator or duplicate index file is needed.
- Con: The former architecture path changes and all current references must be updated atomically.
- Con: Contributors must maintain navigation and cross-links across several files.

## Decision

Choose alternative D while retaining ADR-1's three semantic layers:

1. `AGENTS.md` remains the short agent entry point.
2. `docs/architecture/` becomes the current architecture documentation set. Its `README.md` is the whole-repository
   snapshot and navigation entry point.
3. `docs/decisions/` remains the flat, stable, append-only decision history.

Add `docs/README.md` as a routing page for the repository's documentation. It does not become a fourth design layer;
it directs users to onboarding and package guides, and maintainers to current architecture and decision history.

Within `docs/architecture/`, organize by module only after the architecture purpose has been established:

- `Duets.md`, `Duets.Jint.md`, and `HttpHarker.md` each describe one module's current architecture.
- `Duets.Pad/README.md` is the DuetsPad architecture landing.
- DuetsPad's independent rendering/state, protocol, and security concerns use topic pages below that directory.

Paths representing projects or packages use their exact established identifiers and casing. Generic topic filenames
use lowercase descriptive names. A module receives a directory only after it has multiple substantial pages; the
documentation tree is not made artificially symmetric.

Package READMEs remain the canonical owners of getting-started instructions, public feature guidance, and
configuration. Architecture pages describe internal ownership, boundaries, data flow, state, protocol, and security,
and link to package READMEs rather than reproducing their usage material.

Move `docs/architecture.md` completely to the new documentation set and update current repository links and agent
instructions in the same change. Do not retain a forwarding stub solely for internal links. Mentions in older ADRs
remain historical text; they are not rewritten to make the past decision appear to have used the new path.

This decision partially supersedes ADR-1 only where it fixes the architecture layer to one physical file and path.
ADR-1's layer responsibilities and rationale remain in force. It does not supersede ADR-48: DuetsPad remains a
same-solution package, and its package-facing documentation remains in `src/Duets.Pad/README.md`.

## Rationale

Purpose-first organization makes the top-level choice meaningful for readers: architecture explains current state,
decisions explain why, and plans sequence unfinished work. Applying module structure inside architecture then gives
DuetsPad room to grow without making its size dictate the layout of unrelated documentation.

The whole-repository `README.md` within `architecture/` preserves the holistic snapshot that ADR-1 requires. Module
and topic pages make selective loading possible, directly addressing the pressure that the singleton file now
creates. Exact module names avoid introducing a third spelling alongside the package name `Duets.Pad` and product
name DuetsPad.

Keeping user guidance co-located with packages retains the boundary established by ADR-48 and HttpHarker's existing
README. Retaining flat ADR paths avoids breaking their numbering, cross-references, and historical role. The chosen
structure therefore changes only the part that has stopped scaling.

## Consequences

- **Positive**:
  - The architecture landing remains short enough to provide a whole-system view.
  - Humans and agents can load architecture for one module or DuetsPad concern selectively.
  - Package usage, current architecture, and decision rationale have explicit, non-overlapping owners.
  - DuetsPad can gain further topic pages without expanding the repository-wide overview.
  - Existing ADR files and their historical references remain stable.
- **Negative / trade-offs**:
  - Current links to `docs/architecture.md` and repository skills that assume one output file must be migrated.
  - Architecture changes may require updating more than one page and its navigation link.
  - Relative links to ADRs differ between the architecture landing, module pages, and nested DuetsPad pages.
  - Without a site generator, navigation quality depends on maintaining the README links manually.
