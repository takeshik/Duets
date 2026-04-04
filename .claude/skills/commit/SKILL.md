---
name: commit
description: Use when committing changes to the repository. Handles commit granularity assessment, code style, pre-commit checklist, commit message authoring, and execution. Invoke when the user says "commit", "commit the changes", "create a commit", or similar.
---

# Commit

This skill handles the full commit workflow: granularity assessment, pre-commit checks, message authoring, and execution. Stop and report to the user if any step fails — do not attempt to work around failures.

## Step 1 — Assess commit granularity

Before staging anything, review all pending changes:

1. Run `git status` and `git diff` to understand the full scope of changes.
2. Group changes into logical units. Each commit should represent one coherent change (one fix, one feature, one refactoring, etc.).
3. If changes span multiple independent concerns, **split into separate commits** and process each unit through Steps 2–5 in turn.

Do not bundle unrelated changes into a single commit.

## Step 2 — Code style

For each commit unit that includes source file changes, run:

```bash
dotnet jb cleanupcode Duets.slnx --include="<changed files>"
```

If this fails, **stop and ask the user** before proceeding.

## Step 3 — Pre-commit checklist

Verify each item that applies to the changes in this commit unit:

| Condition | Required action |
|-----------|-----------------|
| Any source change | `dotnet test` passes with no failures |
| New public API or behavior change | Test added or updated in `tests/Duets.Tests/` |
| New feature visible to script authors | `samples/` updated or new sample added |
| New user-facing feature or API added, or existing one changed | Review `README.md` and update if necessary; do not add content that does not pull its weight |
| Design decision made (new component, technology choice, API shape, trade-off) | ADR written in `docs/decisions/` |
| ADR added or updated | Row added/updated in `docs/decisions/index.md` |
| Architecture change (new layer, dependency, or data flow) | `docs/architecture.md` updated |

If a required action is missing, **stop and ask the user** before proceeding.

## Step 4 — Commit message

Rules:

- **English only** — commit messages are repository content.
- **Title-only commits are prohibited** unless the change is trivially obvious (e.g. a single typo fix or a single variable rename with no semantic effect). All other commits must include a body.
- The body explains **why**, not what. Do not restate what the diff already shows.
  - Bad: "Point .codex/skills to .claude/skills so both resolve to the same definitions."
  - Good: "Avoids duplicating skill files; Codex and Claude Code now share one source of truth."
- Wrap body lines at 72 characters.
- Always append a `Co-Authored-By:` trailer with the agent's identity.
- Never stage sensitive files (`.env`, credentials, etc.).

Format:

```
<summary in imperative mood, ≤72 chars>

<body — motivation, context, trade-offs>

Co-Authored-By: <agent identity>
```

## Step 5 — Execute

1. Stage files for this commit unit **explicitly by name** — do not use `git add -A` or `git add .`.
2. Create the commit using a heredoc to pass the message — **never use `\n` escape sequences inside a `-m` string**, as they will appear literally in the commit log:
   ```bash
   git commit -m "$(cat <<'EOF'
   <summary>

   <body>

   Co-Authored-By: <agent identity>
   EOF
   )"
   ```
3. If the commit hook fails, **stop and ask the user**. Never bypass hooks (`--no-verify`).
4. Repeat Steps 2–5 for any remaining commit units identified in Step 1.
5. Report completion.
