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
3. If changes span multiple independent concerns, **split into separate commits** and process each unit through Steps 2–4 in turn.

Do not bundle unrelated changes into a single commit.

## Step 2 — Pre-commit checklist

For each commit unit, follow the complete
[Before committing](../../../CONTRIBUTING.md#before-committing) checklist. That checklist is the
source of truth for the required diff, format, documentation, build, test, sample, documentation
ownership, and package-content checks; do not rely on an agent hook as a substitute.

New public APIs and behavior changes require regression coverage in the owning test project; use
[AGENTS.md](../../../AGENTS.md#testing) to select it. When behavior spans the initialized stack, run
the relevant `Duets.Sandbox` check in addition to the owning regression tests as described by the
shared workflow.

If a required action is missing, **stop and ask the user** before proceeding.

## Step 3 — Commit message

Rules:

- **English only** — commit messages are repository content.
- **Title-only commits are prohibited** unless the change is trivially obvious (e.g. a single typo fix or a single variable rename with no semantic effect). All other commits must include a body.
- The body explains **why**, not what. Before finalising, ask: "Could a reader infer this sentence from the diff alone?" If yes, cut it and write the motivation instead.
  - Bad: "Point .codex/skills to .claude/skills so both resolve to the same definitions." ← restates the diff
  - Good: "Avoids duplicating skill files; Codex and Claude Code now share one source of truth." ← states the reason
- Wrap body lines at 72 characters.
- Always append a `Co-Authored-By:` trailer with the agent's identity.
- Never stage sensitive files (`.env`, credentials, etc.).

Format:

```
<summary in imperative mood, ≤72 chars>

<body — motivation, context, trade-offs>

Co-Authored-By: <agent identity>
```

## Step 4 — Execute

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
4. Repeat Steps 2–4 for any remaining commit units identified in Step 1.
5. Report completion.
