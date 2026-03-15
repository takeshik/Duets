# ADR-16: Introduce samples/ Directory and Clarify Duets.Sandbox Role

## Status

Accepted (partially supersedes [ADR-11](11_sandbox-multi-mode-debugging-cli.md))

## Context

A code review identified that `Duets.Sandbox` carried too many responsibilities:
it served simultaneously as a sample application, a reference implementation,
a developer debugging CLI, and an AI-agent-friendly batch execution platform.
This made it unclear, from the outside, which parts represented standard library
usage and which were internal verification tooling.

A concrete symptom was the `eval` command (ADR-11), which intentionally used a
minimal initialization path (bare `ScriptEngine`, no `AllowClr`, no
`RegisterTypeBuiltins`) to serve as a "getting started" snippet. This diverged
from every other command, which used the fully-initialized `SandboxSession`.
The inconsistency confused both human reviewers and automated checks.

## Decision Drivers

- **Clarity of intent**: readers should immediately know whether a piece of code
  is "how to use this library" or "how to test/debug this library."
- **Low structural overhead**: adding a sample project should not complicate the
  dependency graph or build configuration.
- **Runnable out of the box**: samples should be executable without additional
  setup steps.
- **Sandbox consistency**: all Sandbox commands should behave uniformly so the
  CLI can be treated as a reliable debugging surface.

## Considered Alternatives

### A: Keep everything in Duets.Sandbox; add comments

- Pro: No new files or directories.
- Con: The problem is structural, not documentary; comments do not change
  where a reader looks first.

### B: Add a separate Duets.Samples project (.csproj)

- Pro: Standard .NET project layout; IDE integration out of the box.
- Con: Requires a new .csproj, a Duets.slnx entry, and Directory.Build.props
  consideration — overhead disproportionate to the content at launch.

### C: samples/ directory with .NET file-based apps

- Pro: Each sample is a self-contained `.cs` file runnable via
  `dotnet run samples/<file>.cs`.
- Pro: `#:project` directive allows referencing the local Duets project without
  publishing to NuGet.
- Pro: No .csproj, no solution entry; the directory name alone signals "examples."
- Con: File-based apps are a relatively recent .NET feature; tooling support in
  IDEs is still maturing.

## Decision

Adopt Alternative C. Introduce a `samples/` directory at the repository root
containing file-based app examples. The initial set mirrors the Quick Start
section of the README:

| File | Demonstrates |
|---|---|
| `minimal-eval.cs` | `TypeScriptService` + `ScriptEngine` basics |
| `with-type-registration.cs` | `AllowClr` + `typings` built-in |
| `web-repl.cs` | Full web REPL with `ReplService` |

Simultaneously, remove the `eval` command from `Duets.Sandbox`. Its role as a
minimal usage example is now covered by `samples/minimal-eval.cs`. With `eval`
gone, all remaining Sandbox commands (`batch`, `repl`, `serve`, `complete`) go
through `SandboxSession`, giving uniform initialization behavior.

`Duets.Sandbox` is explicitly not intended for end users or as a deliverable;
it is internal developer and agent tooling.

## Rationale

A `samples/` directory is a leaf node in the dependency graph — nothing depends
on it — so it cannot complicate downstream builds. The file-based app format
keeps each example self-contained and immediately runnable, satisfying the
"low friction" driver without a new project file.

Removing `eval` resolves both the role-conflation and the initialization
inconsistency in one move, rather than patching around them.

## Consequences

- **Positive**: `samples/` gives new users a clear, minimal entry point without
  needing to read Sandbox internals.
- **Positive**: All Sandbox commands now share a single initialization path,
  making the CLI a consistent debugging surface.
- **Positive**: The README Quick Start section links to runnable files rather
  than inert code blocks.
- **Negative / trade-offs**: File-based app IDE tooling (e.g. run/debug buttons)
  is less mature than full project support; developers may need to fall back to
  `dotnet run` from the terminal.
- **Negative / trade-offs**: Each sample must declare its own `using` directives
  and `#:project` reference; there is no shared "preamble" across files.
