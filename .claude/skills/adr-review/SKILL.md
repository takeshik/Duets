---
name: adr-review
description: Use at the end of a working session to assess whether any decisions made during the session warrant a new ADR. Invoke when the user says "review for ADRs", "check if we need ADRs", "end of session ADR check", or similar. Also invoke when the user asks whether a decision or design choice should be documented as an ADR.
---

# ADR Session Review

Review the current session's conversation and determine whether any decisions made warrant a new Architecture Decision Record.

## Evaluation Criteria

A decision warrants an ADR if it meets **all three**:

1. **Non-obvious** — The rationale cannot be inferred by reading the code or git history alone. A future contributor encountering this code would reasonably ask "why?".
2. **Durable** — Reversing the decision would require deliberate effort; it is not a casual implementation detail.
3. **Real alternatives existed** — At least one credible alternative was available. The decision involved a genuine trade-off.

Decisions that do **not** warrant an ADR:
- Implementation details that follow directly from a prior ADR without introducing new trade-offs
- Decisions made purely for consistency with established patterns
- Bug fixes or corrections with no meaningful design alternatives
- Clarifications or refinements of scope that do not change the architecture
- Process/workflow decisions that do not affect the codebase structure

## Process

1. Review the full conversation history of this session.
2. Identify candidate decisions — anything where a choice was made between alternatives, a design was settled on, or a direction was deliberately taken.
3. Apply the three criteria to each candidate.
4. Present findings to the user before proceeding:
   - For each candidate: state the decision and your verdict (ADR warranted / not warranted / borderline).
   - For borderline cases, briefly explain the uncertainty and ask the user to decide.
5. For decisions approved for ADR creation, invoke the `adr new` operation from the `adr` skill.
6. After all ADRs are created, run `adr index` to update `docs/decisions/index.md`.
7. If any new ADR affects the architecture snapshot, run `adr arch` to update `docs/architecture.md`.

## Presentation Format

Present findings before taking any action:

```
## ADR Review — [brief session description]

### Warranted
- **[Decision summary]**: [one sentence on why it meets criteria]

### Not warranted
- **[Decision summary]**: [one sentence on why it does not meet criteria]

### Borderline — your call
- **[Decision summary]**: [explain the uncertainty]

Proceed with creating ADRs for the "Warranted" items? Any borderline items to include?
```

Wait for user confirmation before writing any files.
