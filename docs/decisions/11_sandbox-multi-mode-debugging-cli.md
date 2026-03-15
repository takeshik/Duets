# ADR-11: Sandbox as a Multi-Mode Debugging CLI

## Status

Partially superseded by [ADR-16](16_samples-directory-and-sandbox-role-clarification.md)

## Context

`Duets.Sandbox` was a minimal demo application that simply started an HTTP server and ran a readline loop. As the core library matured, a richer interactive environment became necessary for:

- Verifying TypeScript transpilation and evaluation behavior end-to-end
- Testing completion results from `TypeScriptService.GetCompletions`
- Starting and stopping the web REPL server on demand
- Supporting autonomous operation by AI coding agents (e.g. Claude Code) that run shell commands as tools

The key constraint for agent use is that interactive stdin polling is inherently sequential: an agent using a Bash tool cannot easily interleave writes to stdin with reads from stdout in the same process. A persistent server process requires background process management (`&`, `kill`, startup polling), which is fragile and noisier than a simple in/out pipeline.

## Decision Drivers

- **Agent operability**: AI agents should be able to drive the sandbox without managing background processes.
- **Human operability**: Developers should have an interactive REPL for exploratory use.
- **Stateful sessions**: Variables defined in one `eval` must be visible in subsequent ones (shared `ScriptEngine` instance).
- **Minimal friction**: No new runtime dependencies; no generated config files.

## Considered Alternatives

### A: Single interactive REPL (stdin readline loop)

- Pro: Simple implementation.
- Con: Requires stdin polling; AI agents cannot easily use it without pseudo-terminal tricks.

### B: Persistent HTTP daemon for agent access

- Pro: Agent can POST commands to a running server via `curl`.
- Con: Requires background process management (`&`, startup wait, `kill`), which adds noise and fragile coordination.

### C: Multi-mode CLI with JSONL batch mode

- Pro: Each mode is stateless at the process level; agents use the batch mode by piping JSONL to stdin and reading JSONL from stdout — no background process management needed.
- Pro: Human interactive mode (REPL) and one-shot modes (`eval`, `complete`) coexist without compromise.
- Pro: State (script engine variables, registered types, server lifecycle) persists within a single batch session.
- Con: Agents cannot see intermediate results mid-batch; each round-trip requires a new invocation.

## Decision

Implement a multi-mode CLI (Alternative C). The entry point dispatches on the first CLI argument:

| Mode | Invocation | Output |
|---|---|---|
| `repl` (default) | no args | human-readable, interactive |
| `eval` | `eval <code>` | single JSON object |
| `complete` | `complete <src> [pos]` | single JSON object |
| `serve` | `serve [port]` | blocks until Ctrl+C |
| `batch` | `batch` | JSONL in → JSONL out |

The batch mode accepts a stream of JSON operation objects (one per line) and writes one JSON result per operation to stdout. Supported operations: `eval`, `complete`, `register`, `types`, `server-start`, `server-stop`, `server-status`, `reset`, `help`.

## Rationale

The JSONL batch mode gives AI agents a simple, reliable interface: write a sequence of commands to a file, pipe it in, read all results at once. No process management, no timing dependencies. The same `ScriptEngine` instance services the whole batch, so variables accumulate across `eval` calls exactly as in the interactive REPL.

One-shot modes (`eval`, `complete`) are convenient for single queries where startup cost is acceptable.

The interactive REPL uses `:command` syntax (similar to GHCi / Scala REPL) to distinguish meta-commands from TypeScript code.

## Consequences

- **Positive**: Agents can exercise transpilation, completions, type registration, and server lifecycle without process management overhead.
- **Positive**: The interactive REPL is a first-class mode, not an afterthought.
- **Negative / trade-offs**: Agents performing exploratory multi-step workflows must either batch all steps upfront or pay per-invocation startup cost (Jint initialization + TypeScript compiler loading) for separate invocations.
