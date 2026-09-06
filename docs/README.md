# Documentation

This directory separates current architecture from historical design decisions. User-facing guidance remains close
to the repository or package it describes.

## For users

- Start with the repository [README](../README.md) for the package map, quick start, and runnable examples.
- See the [Duets core guide](../src/Duets/README.md) for session lifecycle, execution, declarations, and
  backend-neutral extension points.
- See the [Duets.Jint guide](../src/Duets.Jint/README.md) for runtime selection, CLR interop, server-side
  completions, and runtime assets.
- See the [DuetsPad guide](../src/Duets.Pad/README.md) for browser-pad usage, surfaces, UI helpers, security setup,
  and configuration.
- See the [HttpHarker guide](../src/HttpHarker/README.md) for server and middleware usage.
- Browse [`samples/`](../samples/) for executable file-based applications grouped by package.

## For maintainers

- [Contributing](../CONTRIBUTING.md) is the shared source of truth for human and agent development,
  verification, documentation, and CI workflows.
- [Agent guidance](../AGENTS.md) adds repository navigation, safety rules, and agent-specific
  end-to-end verification requirements.
- [Architecture](architecture/) describes the current system, module boundaries, data flow, state models, protocols,
  and security boundaries.
- [Architecture Decision Records](decisions/) preserve the context, alternatives, and rationale behind durable design
  choices. Use the [ADR index](decisions/index.md) to find relevant decisions.
