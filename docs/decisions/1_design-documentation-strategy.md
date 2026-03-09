# ADR-1: Design Documentation Strategy

## Status

Accepted

## Context

Duets is a library developed primarily by a solo maintainer assisted by AI coding agents (Claude Code). Design decisions need to be recorded in a way that serves both the AI agent (which loads AGENTS.md each session) and human contributors.

As the project grows, design context accumulates. Without a structured approach, this context either bloats AGENTS.md (wasting the agent's context window) or gets lost entirely.

## Decision Drivers

- **AI agent efficiency** — The agent should get relevant context without reading irrelevant detail every session
- **Scalability** — The approach must work as the number of decisions grows from single digits to dozens
- **Low maintenance burden** — Solo developer; the process must not become overhead that discourages documentation
- **Structured readability** — Both humans and AI benefit from consistent, predictable document structure

## Considered Alternatives

### A: Inline everything in AGENTS.md

- Pro: Single file, always loaded into agent context
- Con: Grows unbounded; wastes context window on details irrelevant to the current task

### B: Free-form docs/ only

- Pro: Flexible; no template overhead
- Con: No consistent structure for agents to parse; harder to find specific decisions

### C: 3-layer structure (AGENTS.md + architecture overview + ADRs)

- Pro: Each layer serves a distinct purpose; agents can selectively read deeper docs when needed
- Con: More files to maintain; requires discipline to keep layers in sync

## Decision

Adopt the 3-layer structure (option C):

- **AGENTS.md** — Agent entry point. Project map, build commands, and pointers to deeper docs. Kept short.
- **docs/architecture.md** — Current snapshot of the overall architecture. Updated when architectural changes land.
- **docs/decisions/** — Individual ADRs. One decision per file, append-only. Old decisions are superseded, not rewritten.

## Rationale

The primary consumer is an AI coding agent that loads AGENTS.md every session. A thin entry point with links to structured deeper documents lets the agent pull in only what it needs — satisfying the efficiency driver. ADR's fixed template is well-suited for both AI generation and AI consumption, addressing structured readability. The architecture overview complements ADRs by providing the holistic view that individual decision records cannot, keeping each ADR focused and small (low maintenance burden). Option B was close but lacks the structural consistency that makes AI consumption reliable.

## Consequences

- **Positive**: AGENTS.md stays small and focused; design context is preserved and discoverable; the fixed ADR template enables AI-assisted drafting and reading
- **Negative / trade-offs**: Three layers require discipline to keep in sync; architecture.md must be updated when ADRs affect the overall architecture, which adds a manual step
