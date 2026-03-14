# AGENTS.md

Guidelines for coding agents when working on the Duets repository.

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
- `Directory.Build.props` — Shared build properties (TFM, nullable, etc.)
- `src/`
  - `Duets/` — Core library: TypeScript REPL for .NET
    - `TypeScriptService.cs` — Runs TS compiler on Jint; transpile + completions
    - `ScriptEngine.cs` — Jint wrapper for executing transpiled user code
    - `ClrDeclarationGenerator.cs` — Generates `.d.ts` from .NET types via reflection
    - `ReplService.cs` — Web REPL: Monaco UI, SSE type updates, `/eval` endpoint
    - `Resources/ReplStaticFiles/` — Embedded web assets (`index.html`, `language-service.js`)
  - `HttpHarker/` — Standalone lightweight HTTP server library (may be extracted to its own repo)
    - `HttpServer.cs` — `HttpListener`-based server with middleware pipeline
    - `HttpServerExtensions.cs` — Extension methods (C# 14 `extension` blocks)
    - `ActionContext.cs` — Request/response wrapper for route handlers
    - `Middlewares/` — Built-in middleware (routing, embedded resources, errors)
  - `Duets.Sandbox/` — Sample application and multi-mode debugging CLI
  - `shared/` — `internal` utility code shared across all projects via `<Compile Include>` (not a separate assembly); place cross-project internal helpers here
- `docs/`
  - `architecture.md` — Architecture overview (current snapshot)
  - `decisions/` — Architecture Decision Records (ADRs)
- `tests/`
  - `Duets.Tests/` — Unit tests (xUnit)

## Architecture & Design

- [docs/architecture.md](docs/architecture.md) — Current architecture snapshot. Read this before making structural changes.
- [docs/decisions/](docs/decisions/) — Architecture Decision Records (ADRs). ADR-N is at `docs/decisions/<N>_*.md`.

When a session involves a design decision (new component, technology choice, API design trade-off, etc.), draft an ADR in `docs/decisions/` at the end of the session. If the decision affects the overall architecture, update `docs/architecture.md` to reflect the new state.

## Language

All repository content **must be in English**: source code, comments, commit messages, documentation, ADRs, and any other text files. This applies regardless of the language used in conversation with the agent.

## Code Style

Code style is enforced mechanically via `jb cleanupcode`. Rules are defined in `.editorconfig` and `.DotSettings` — do not duplicate them here.

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

Tests use [xUnit](https://xunit.net/) and live in `tests/Duets.Tests/`.

## End-to-end verification with Duets.Sandbox

`Duets.Sandbox` provides a JSONL batch mode for agent-friendly end-to-end verification of the full stack (transpilation, completions, type registration, web server). Use this to validate changes without writing test code.

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

Diagnostic output (initialization messages) goes to stderr; stdout contains only JSONL results, making it straightforward to parse with standard tools.
