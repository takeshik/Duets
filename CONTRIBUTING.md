# Contributing to Duets

This guide is the shared development and verification reference for human contributors and coding
agents. Package usage belongs in the package READMEs, current internal structure belongs in
`docs/architecture/`, and durable design rationale belongs in the ADRs.

## Development environment

The repository requires the .NET 10 SDK. Confirm the active SDK before restoring:

```bash
dotnet --version
```

The repository also pins Biome in `mise.toml` and CSharpier in `dotnet-tools.json`. With
[mise](https://mise.jdx.dev/) installed, prepare the auxiliary tools with:

```bash
mise install
dotnet tool restore
```

Alternatively, open the repository in its devcontainer. The container provides .NET 10 and mise,
forwards port 17375 for DuetsPad, and restores .NET tools and packages after creation.

## Restore

Restore from the repository root using the committed dependency locks:

```bash
dotnet tool restore
dotnet restore --locked-mode
```

If a dependency change intentionally updates a lock file, run `dotnet restore --force-evaluate`,
review every resulting `packages.lock.json` change, and then verify that locked restore succeeds.
Do not commit incidental dependency drift.

## Build, format, and test

The normal local verification sequence is:

```bash
dotnet run scripts/format.cs
dotnet run scripts/format-check.cs
dotnet run scripts/docs-check.cs
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
```

`scripts/format.cs` applies safe formatter and analyzer fixes through `dotnet format`, CSharpier,
and Biome. It deliberately does not blanket-apply behavior-sensitive suggestions. Review every
remaining suggestion and either implement it when correct or suppress it with a reason.

`scripts/format-check.cs` is the non-mutating check used by CI. Run it after the mutating formatter
so the working tree, rather than CI, records any required changes.

`scripts/docs-check.cs` validates local link targets and heading anchors in every Markdown file
except ADR records, the sample catalog, retired live paths, trailing whitespace, and whitespace
errors in the current Git diff. ADR records are historical text; their links are validated by the
ADR check below with the same resolver (`scripts/_markdown.cs`).

The resolver supports a deliberate Markdown subset rather than full CommonMark: inline links whose
destination is one token without spaces or parentheses (optionally in angle brackets), closed by
`)` on the same line and without a title; reference definitions `[label]: target` on their own
line; and full, collapsed, and shortcut reference usages, with labels matched case-insensitively.
Footnotes are a reference `[^label]` and a definition `[^label]: text` at the start of its own line,
whose text may hold links of the kinds above; a footnote reference without a definition, or a
definition that nothing references, is an error. Code spans and backslash-escaped brackets are not
links. Anything that starts like a link but
falls outside this subset is reported as an error rather than ignored, so a link the check cannot
read cannot pass silently.
ADR-specific validation is `scripts/adr-check.cs`, which checks the structural rules of
`docs/decisions/README.md`, each against what its kind reaches (README, "How the rules apply"). On
every record it checks numbering, headings, status vocabulary, reciprocal relations, Maintenance
Note placement and entry shape, links, and index rows. On records open in the change — absent from
the base or `Proposed` there, or without `--base`, `Proposed` now — it checks section layout, the
title-derived filename, references to later records, and that no Maintenance Note is present. With
`--base <revision>` it checks the change rules between that revision and the working tree: no
removed records, allowed status transitions, no change to the historical text, title, or filename of
a record that has left `Proposed`, no change to such a record's body unless the same change appends
a `## Maintenance Note` entry, keeps the sections the template names, and adds no reference to a
later record, Maintenance Note entries never removed, reworded, or reordered, and relation entries
kept once a record leaves `Proposed` and established only at acceptance against a record in the
permitted state. Whether an editorial revision actually preserved the decision is a question for
review, not for the check: it sees that a body changed and that a note was written, never whether
the note is true. The base is read fail-closed: a record whose base Status cannot be read fails the
change rules. It verifies structure only and never semantic correctness; `--self-test` runs its
scenarios in a temporary repository. Both checks use only repository files and local Git metadata;
they do not call AI services or the network. External links are excluded so transient network
failures cannot make the required check nondeterministic.

### Test ownership

Place regression tests with the component whose behavior they cover:

| Production area | Primary test project |
|---|---|
| `src/Duets/` and `src/Duets.Jint/` | `tests/Duets.Tests/` |
| `src/Duets.Pad/` | `tests/Duets.Pad.Tests/` |
| `src/HttpHarker/` | `tests/HttpHarker.Tests/` |
| `src/Duets.Sandbox/` | `tests/Duets.Tests/` for CLI and session behavior; `tests/Duets.Pad.Tests/` for the DuetsPad protocol client |
| `src/shared/` | Every test project affected by the changed shared source |
| `tests/shared/` | Every test project that compiles the changed test-support source |

Cross-component protocol tests belong to the project that owns the public boundary under test.

Tests use xUnit v3 on Microsoft.Testing.Platform. The explicit solution form is:

```bash
dotnet test --solution Duets.slnx
```

Filter a project by fully qualified class or method with the platform-native options, not VSTest's
`--filter`:

```bash
dotnet test --project tests/Duets.Tests/Duets.Tests.csproj -- \
  --filter-class Duets.Tests.DuetsSessionTests

dotnet test --project tests/Duets.Tests/Duets.Tests.csproj -- \
  --filter-method Duets.Tests.DuetsSessionTests.EvaluateAsync_allows_reentrant_engine_callbacks
```

When reusing a preceding Release build, add `--no-build --configuration Release` before the `--`.

## Samples and end-to-end checks

Samples are self-contained .NET file-based applications. Run one from the repository root:

```bash
dotnet run samples/Duets/minimal-eval.cs
dotnet run samples/Duets.Pad/duetspad.cs
dotnet run samples/HttpHarker/hello-http.cs
```

See [`samples/README.md`](samples/README.md) for the complete catalog. A first run of a sample or
test that uses Babel, TypeScript, Monaco, or Tabler may require network access to populate its
configured cache.

Add or update a sample when a user-visible or script-visible feature introduces a new usage path.
A sample supplements, but does not replace, a regression test. For browser behavior, verify the
relevant interaction in a real browser and report that separately from automated checks.

`Duets.Sandbox` supplies agent-friendly batch, completion, and server modes for initialized-stack
checks. Send `{"op":"help"}` in batch mode or see [`AGENTS.md`](AGENTS.md#end-to-end-verification-with-duetssandbox)
for the command catalog. Sandbox checks supplement the owning test project.

## Documentation responsibilities

Keep each kind of information with its designated owner:

| Change | Required documentation review |
|---|---|
| Public type or member | Add or update its XML documentation. Packable libraries treat missing public documentation as a build error. |
| Package workflow or user-visible behavior | Review the relevant package `README.md`; update it when the change affects how users configure or use the package. |
| New usage path | Add or update the owning package's file-based sample and its entry in `samples/README.md`. |
| Module boundary, dependency, state model, protocol, security boundary, or whole-system data flow | Update the relevant page under `docs/architecture/`; update its landing page when navigation or the whole-system view changes. |
| Durable design choice or trade-off | Add an ADR under `docs/decisions/` following [its governance](docs/decisions/README.md) and update `docs/decisions/index.md`; a change to what a decision means is expressed by a new ADR plus the existing ADR's Status metadata. The body of a record that has left `Proposed` changes only through an editorial revision, on the owner's instruction and under the conditions the governance states. |
| Development or CI workflow | Update this guide and the workflow or agent guidance that consumes it. |

Use the [documentation landing page](docs/README.md) to locate the current owner before adding new
material. Do not copy architecture explanations into a package guide or present historical ADR
context as current operating instructions.

Repository content, including source comments, documentation, ADRs, and commit messages, is written
in English.

## Before committing

Review the complete diff and keep each commit to one coherent change. At minimum:

1. Run `git diff --check`.
2. Run `dotnet run scripts/format.cs`, then `dotnet run scripts/format-check.cs`,
   `dotnet run scripts/docs-check.cs`, and `dotnet run scripts/adr-check.cs -- --base HEAD`.
3. For source changes, run the Release build and all affected tests. Run the full test suite when
   the change crosses project boundaries or affects shared sources.
4. Run the relevant sample or Sandbox scenario for user-visible behavior, in addition to tests.
5. Review README, sample, XML documentation, architecture, and ADR obligations from the table above.
6. For package-output changes, run a Release `dotnet pack` for every affected packable project and
   inspect the resulting package contents.
7. Re-read the staged diff before creating the commit.

Documentation-only changes do not require product tests unless they alter executable examples or
build configuration. They still require link, spelling, formatting, and package-content checks
appropriate to the files changed.

## CI correspondence

The [Publish workflow](.github/workflows/publish.yml) runs the same core gates on pull requests and
updates to `main`:

| CI step | Local equivalent |
|---|---|
| Restore tools | `dotnet tool restore` |
| Restore packages | `dotnet restore --locked-mode` |
| Check code format | `dotnet run scripts/format-check.cs` |
| Check documentation | `dotnet run scripts/docs-check.cs` |
| Build | `dotnet build --no-restore --configuration Release` |
| Test and produce TRX | `dotnet test --no-build --configuration Release -- --report-trx` |

CI resolves and caches the current Babel and TypeScript test assets before running tests, so an
uncached local full-suite run may perform the same network fetches. The workflow also has
conditional packaging and publishing steps; those steps are not a substitute for the local gates
above.
