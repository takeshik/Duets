---
name: adr-review
description: Read-only assessment of whether a decision made in the current session warrants a new Architecture Decision Record or a lifecycle update to an existing ADR. Invoke for "review for ADRs", "do we need an ADR for this", "end of session ADR check", or when the user asks whether a design choice should be recorded. Creates and edits nothing; approved items are handed to the adr skill.
---

# ADR Need Review

This skill decides nothing on its own and writes no files. It applies the criteria in
[docs/decisions/README.md](../../../docs/decisions/README.md), section *What an ADR records*
(including *One decision per record*), to the decisions of the current session and presents
verdicts for the owner to confirm.

## Process

1. Read *What an ADR records* and skim [docs/decisions/index.md](../../../docs/decisions/index.md)
   so that existing decisions are known.
2. Collect candidates from the session: every place where a direction was deliberately taken, a
   design was settled, or an option was chosen or rejected. Tooling, process, and governance
   decisions are candidates like any other; the README's criteria decide, not the subject.
3. For each candidate, answer the README's criteria. Then determine the operation that expresses
   the outcome:
   - a new record (`new`), declaring `Supersedes` (target) or `Amends` (target and scope) when it
     changes an existing decision;
   - a lifecycle transition of an existing record: `deprecate` (no longer applied, no replacement),
     `reject` or `withdraw` (a `Proposed` record the session decided against);
   - readiness for `accept`: a `Proposed` record whose acceptance review the session completed
     (the `adr` skill's `review` operation).
     This skill never recommends or performs acceptance itself; it reports readiness for the
     owner's decision.

   A record that has become hard to read, or that has absorbed material belonging elsewhere, is not
   a decision and is not a candidate here. Never propose supersession or deprecation as the remedy
   for it: that would enter a decision the session did not make (README, *Editorial revision*).
   Note the observation for the owner and stop; the `editorial-revision` operation runs only on the
   owner's explicit instruction.
4. Assign one verdict per candidate:
   - **ADR warranted** — a new record; state the decision boundary, or the split if there are
     several. Each part of a split must itself meet *What an ADR records*; a part that does not
     stays with the decision it serves (*One decision per record*).
   - **Existing ADR lifecycle update required** — the ADR and the transition, with the scope where
     the transition is an amendment.
   - **ADR not warranted** — with the criterion that fails.
   - **Borderline, owner decides** — what makes it uncertain.
5. Present the findings in the format below and stop. Do not create files, do not draft text, and
   do not call the `adr` skill until the owner has confirmed which items proceed.
6. After confirmation, hand each approved item to the `adr` skill with the operation named. Do not
   ask `adr` to regenerate the index or the architecture documentation.

## Presentation format

```text
## ADR Review — <session in one line>

### ADR warranted
- <decision>: <criteria met>; <one decision boundary, or proposed split>

### Existing ADR lifecycle update required
- <decision>: new record supersedes ADR-N | new record amends ADR-N — <scope> | deprecate ADR-N | reject / withdraw ADR-N
- ADR-N: acceptance review complete — ready for the owner's acceptance decision

### Not warranted
- <decision>: <criterion that fails>

### Borderline — owner decides
- <decision>: <what is uncertain>

Which items should proceed?
```
