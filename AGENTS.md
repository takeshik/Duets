# AGENTS.md

Guidelines for coding agents when working on the Duets repository.

> **Note:** `CLAUDE.md` is a symlink to this file (`AGENTS.md`). Edits via
> either path modify `AGENTS.md` on disk. When staging changes, always use
> `git add AGENTS.md` explicitly — `git add CLAUDE.md` will not work because
> Git tracks the symlink target, not the symlink itself.

> **IMPORTANT — Language policy (read before responding):**
> - **Chat responses** (reviews, explanations, summaries, plans, status updates):
    > respond in **the same language the user used**. Never default to English.
> - **Repository content** (source code, comments, commits, docs, ADRs):
    > always write in **English only**.

## Build & Run

```bash
# Build the entire solution
dotnet build

# Run the sample application
dotnet run --project src/Duets.Sandbox

# Run tests
dotnet test
```

The solution targets **.NET 10**. The SDK version may be pinned via `mise.toml`.

## Project Structure

- `Duets.slnx` — Solution file (XML-based slnx format)
- `Directory.Build.props` — Shared build properties (TFM, nullable, etc.) applied to all projects
- `src/`
  - `Duets/` — Core library (public API)
    - `Resources/ReplStaticFiles/` — Embedded web assets compiled as `EmbeddedResource` and served by `ReplService` at
      runtime
  - `HttpHarker/` — Standalone lightweight HTTP server library (may be extracted to its own repo)
  - `Duets.Sandbox/` — Multi-mode debugging CLI (batch, repl, serve, complete); not part of the public API
  - `shared/` — `internal` utility code shared across all projects via `<Compile Include>` (not a separate assembly);
    place cross-project internal helpers here
- `samples/` — Runnable file-based app examples (`.cs` files; run with `dotnet run samples/<file>.cs`)
- `docs/`
  - `architecture.md` — Architecture overview (current snapshot)
  - `decisions/` — Architecture Decision Records (ADRs)
- `tests/`
  - `Duets.Tests/` — Unit tests (xUnit v3)

## Architecture & Design

- [docs/architecture.md](docs/architecture.md) — Current architecture snapshot. Read this before making structural
  changes.
- [docs/decisions/index.md](docs/decisions/index.md) — ADR index: Title, Keywords, and Abstract for all ADRs. Read this
  to identify relevant decisions before reading full ADRs.
- [docs/decisions/](docs/decisions/) — Architecture Decision Records (ADRs). ADR-N is at `docs/decisions/<N>_*.md`.

When a session involves a design decision (new component, technology choice, API design trade-off, etc.), draft an ADR
in `docs/decisions/` at the end of the session. If the decision affects the overall architecture, update
`docs/architecture.md` to reflect the new state.

## End-of-Session Checklist

Before committing, verify each item that applies to the changes made in this session. This checklist is mandatory — do
not skip items without explicit justification.

| Condition | Required action |
|-----------|-----------------|
| Any source change | `dotnet test` passes with no failures |
| New public API or behavior change | Test added or updated in `tests/Duets.Tests/` |
| New feature visible to script authors | `samples/` updated or new sample added |
| New user-facing feature or API added, or existing one changed | Review `README.md` and update if necessary; do not add content that does not pull its weight |
| Design decision made (new component, technology choice, API shape, trade-off) | ADR written in `docs/decisions/` |
| ADR added or updated | Row added/updated in `docs/decisions/index.md` |
| Architecture change (new layer, dependency, or data flow) | `docs/architecture.md` updated |

## Language

There are two distinct contexts with different language rules:

**Repository content** — source code, comments, commit messages, documentation, ADRs, and any other checked-in text
files — **must be in English**.

**Chat responses** — all assistant prose including reviews, explanations, summaries, plans, and status updates — **must
be in the same language the user used**. Do not default to English. These are conversational outputs, not repository
content, and the distinction must be respected even when the subject matter is code.

## Code Style

Code style is enforced mechanically via `jb cleanupcode`. Rules are defined in `.editorconfig` and `.DotSettings` — do
not duplicate them here.

After modifying code, run:

```bash
dotnet jb cleanupcode Duets.slnx --include="<changed files>"
```

## Key Dependencies

| Package | Purpose |
|---|---|
| [Jint](https://github.com/sebastienros/jint) | JavaScript engine — runs the TypeScript compiler and user scripts |
| [Mio](https://github.com/takeshik/Mio) | File path utilities (`DirectoryPath`, `FilePath`) |

## Testing

```bash
dotnet test
```

Tests use [xUnit v3](https://xunit.net/) and live in `tests/Duets.Tests/`. xUnit v3 runs on Microsoft.Testing.Platform; use `--filter-class`/`--filter-method` instead of `--filter`.

## End-to-end verification with Duets.Sandbox

`Duets.Sandbox` provides a JSONL batch mode for agent-friendly end-to-end verification of the full stack (transpilation,
completions, type registration, web server). Use this to validate changes without writing test code.

```bash
# Pipe JSONL operations to stdin; one JSON result per line is written to stdout.
echo '{"op":"eval","code":"1 + 2"}' | dotnet run --project src/Duets.Sandbox -- batch

# Multiple operations in one session (variables persist across eval calls):
printf '{"op":"eval","code":"const xs = [1,2,3]"}\n{"op":"eval","code":"xs.length"}\n' \
  | dotnet run --project src/Duets.Sandbox -- batch
```

Send `{"op":"help"}` to get the full list of supported operations and their fields:

```bash
echo '{"op":"help"}' | dotnet run --project src/Duets.Sandbox -- batch
```

Diagnostic output (initialization messages) goes to stderr; stdout contains only JSONL results, making it
straightforward to parse with standard tools.
