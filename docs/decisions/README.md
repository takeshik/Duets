# Architecture Decision Records

This directory contains Architecture Decision Records (ADRs) for the Duets project. Each file documents a single architectural or design decision: the context that prompted it, the alternatives considered, and the rationale for the choice made.

See [index.md](index.md) for a quick-reference table of all ADRs with keywords and abstracts.

## Adding a New ADR

1. Copy `_template.md` and name it `N_<title>.md` where `N` is the next sequential number and `<title>` is the ADR title converted to lowercase kebab-case (spaces → `-`, special characters removed).
2. Fill in all sections of the template.
3. The title in the filename and the `# ADR-N: <Title>` heading must match.
4. Append a new row to `index.md` with Title, Keywords, and Abstract.

## File Naming

The filename is derived directly from the ADR title:

- Number prefix separated from the title by `_` (e.g. `3_use-httplistener-...`)
- Title words separated by `-` (kebab-case)
- Lowercase; special characters (`.`, `/`, `'`) removed

**Because the filename is the title, choose titles that are concise and unambiguous.** A good title names the decision, not the outcome — prefer "Use X" or "Fetch Y from Z" over vague nouns like "Asset Strategy".

## Conventions

- ADRs are append-only. When a decision is superseded, mark the old ADR as `Deprecated (superseded by ADR-N)` and write a new one; do not rewrite history.
- Each ADR should reference only earlier ADRs (no forward references).
- Keep each ADR focused on a single decision. If two choices are truly independent, write two ADRs.
