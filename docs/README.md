# Documentation

This directory separates current architecture from historical design decisions. User-facing guidance remains close
to the repository or package it describes.

## For users

- Start with the repository [README](../README.md) for the package map, quick start, and runnable examples.
- See the [DuetsPad guide](../src/Duets.Pad/README.md) for browser-pad usage, surfaces, UI helpers, security setup,
  and configuration.
- See the [HttpHarker guide](../src/HttpHarker/README.md) for server and middleware usage.
- Browse [`samples/`](../samples/) for executable file-based applications grouped by package.

## For maintainers

- [Architecture](architecture/) describes the current system, module boundaries, data flow, state models, protocols,
  and security boundaries.
- [Architecture Decision Records](decisions/) preserve the context, alternatives, and rationale behind durable design
  choices. Use the [ADR index](decisions/index.md) to find relevant decisions.
