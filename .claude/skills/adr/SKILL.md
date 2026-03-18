---
name: adr
description: Use when creating, updating, or managing Architecture Decision Records (ADRs) in this project. Invoke for operations like "create a new ADR", "update the ADR index", or "regenerate architecture.md". Also triggered when the user asks to write up a design decision or document a choice made during a session.
---

# ADR Management

This skill manages Architecture Decision Records in `docs/decisions/`.

## Index Format

`docs/decisions/index.md` is a derived document updated incrementally as ADRs are added. It serves as a quick-reference entry point so that relevant ADRs can be identified without reading full documents.

Structure:

```markdown
# ADR Index

| # | Title | Keywords | Abstract |
|---|-------|----------|----------|
| [ADR-N](N_title.md) | Human-readable title | keyword1, keyword2, ... | One-sentence summary of the decision and key rationale. |
```

The Abstract cell should be self-contained and fit on one line without line breaks — it answers "what was decided and why at a glance", not "how". Do not duplicate the full rationale.

## Operations

### `adr new <title>`

1. Determine the next ADR number N (highest existing number + 1, do not zero-pad).
2. Convert title to kebab-case filename: `N_<kebab-title>.md`. Remove special characters (`.`, `/`, `'`); replace spaces with `-`; lowercase.
3. Copy `docs/decisions/_template.md`, fill in all sections. Status: `Accepted`.
4. Append a new row to the table in `docs/decisions/index.md`. If `index.md` does not exist, create it with the header row first, then append. Do not modify existing rows.
5. If the decision meaningfully changes the current architecture snapshot, update `docs/architecture.md` accordingly. Add an ADR link wherever relevant.

### `adr index`

Incremental update — do not rewrite existing rows.

1. Read the existing `docs/decisions/index.md` and collect the ADR numbers already present in the table.
2. Read all `.md` files in `docs/decisions/` (excluding `_template.md`, `README.md`, `index.md`).
3. For each ADR whose number is **not** already in the index, generate a new row (Title, Keywords, Abstract) and append it in ascending numeric order.
4. If `index.md` does not exist, create it from scratch with all ADRs.

To update an existing row (e.g. after a status change to Deprecated), explicitly ask to update that specific ADR's entry.

### `adr index --rebuild`

Full rebuild — read all ADR files and regenerate `docs/decisions/index.md` entirely. Use only when the index is known to be stale or corrupt.

### `adr arch`

Read `docs/decisions/index.md` and all ADR files. Regenerate `docs/architecture.md` to reflect the current architectural state, preserving existing structure and tone. Every substantive claim should link to the ADR that supports it.

## File Naming Convention

`N_title-in-kebab-case.md`:
- N is the sequential ADR number, no zero-padding
- Title is lowercase kebab-case derived from the ADR title
- Special characters (`.`, `/`, `'`) are removed

## ADR Content Guidelines

- Keep each ADR focused on a single decision. If two choices are truly independent, write two ADRs.
- "Considered Alternatives" must include at least one real alternative with honest pros/cons.
- "Rationale" explains *why* the chosen option beats the alternatives; it does not merely restate the decision.
- ADRs are append-only. To supersede, mark the old ADR `Deprecated (superseded by ADR-N)` and write a new one.
- All content must be in English regardless of the conversation language.
