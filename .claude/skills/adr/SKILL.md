---
name: adr
description: Use to create a new Architecture Decision Record, to review a Proposed one before acceptance, or to change the lifecycle of an existing one (accept, reject, withdraw, supersede, amend, deprecate). Invoke for "create an ADR", "write up this decision as an ADR", "review ADR-N for acceptance", "supersede ADR-N", "mark ADR-N accepted", "amend ADR-N", "this ADR has become a specification". Not for judging whether an ADR is needed (use adr-review).
---

# ADR Operations

This skill executes operations on `docs/decisions/`. It holds procedures only. Every rule is in
[docs/decisions/README.md](../../../docs/decisions/README.md), called "the README" below; this file
names the README section that governs each step and adds nothing to it. If this file and the README
disagree, the README is right and this file must be fixed.

## Before any operation

1. Read the README sections *How the rules apply*, *Lifecycle*, and *Body immutability*. For
   `new` and `review`, also read *What an ADR records*, *Writing an ADR*, *Acceptance review*,
   *Index*, and *File naming and numbering*.
2. Identify the operation. If the request is really "should this be an ADR?", stop and use
   `adr-review`.
3. Name the files you are going to touch before touching them, and check each change against
   *Body immutability*. A request to change what a record decided goes to `new` declaring
   `Supersedes` or `Amends`, never to `editorial-revision` (*What an editorial revision is not*).
4. Sources for the content are defined in *Sources* and *Guidance per section*.
5. Scope of side effects: only the index rows of the ADRs in the operation; only the architecture
   pages whose current state the decision changed. The index and the architecture documentation
   are never regenerated from the corpus.

## Operations

### new

1. Apply *One decision per record*. If the request bundles independently reversible decisions,
   judge each part against *What an ADR records* first; propose a split only into parts that each
   warrant a record, and stop until the owner chooses.
2. Number and filename per *File naming and numbering*. Copy `_template.md`; Status `Proposed`.
3. Write the sections per *Sections* and *Guidance per section*; a conditional section stays only
   with substance. Place detail per *Information placement*; references per *References inside a
   body*.
4. If the decision replaces or changes an earlier ADR, declare the relation in the new record's
   Status section only, per *Status and relation metadata*. The earlier ADR is not touched.
5. Append the index row per *Index*.
6. Report (see below) with the *Acceptance checklist* and stop. Acceptance is the owner's.

### review

For a named `Proposed` record, before the owner decides on its acceptance. It edits no file.

1. Read the record in full.
2. Walk the *Acceptance checklist* below. For each item give a verdict — met, not met, or for the
   owner to judge — and the passage or fact it rests on.
3. Report the verdicts and stop. Acceptance is the owner's. An item that is not met is fixed in the
   record while it is still `Proposed`, on the owner's instruction, never by this operation.

### accept

Before the operation, confirm the owner's explicit approval of this record in this session; without
it, stop. Then change the Status word to `Accepted` and nothing else in the file. In the same
operation, apply `supersede` or `amend` to each earlier ADR the record declares a relation to, update
the index rows, and decide page by page whether `docs/architecture/` changed. After the operation,
walk the *After acceptance* checklist below.

### reject, withdraw

On the owner's explicit instruction for a named `Proposed` record. Change the Status word only.
Declared relations stay in the record as part of what was proposed; they were never applied to
earlier ADRs, so nothing there is reverted (README, *Status and relation metadata*). Annotate the
index title per *Index* if searchers are likely to find the record.

### supersede

On a direct request ("supersede ADR-N") with no `Proposed` successor yet: run `new` for the
successor, declaring `Supersedes`, and stop; the steps below run when the successor is accepted.

Runs inside `accept` of the later record, never while it is `Proposed`. On the earlier ADR, per
*Transitions* and *Status and relation metadata*: Status word `Superseded`, a `Superseded by` line,
every existing relation entry kept, body untouched. Prose after the relation list is optional
lifecycle metadata: keep it where it still holds, and where the supersession makes it wrong, say so
in the report and correct it in this operation rather than leaving it to stand. Index per *Index*:
strike through the earlier title; a lifecycle note is optional.

### amend

On a direct request ("amend ADR-N") with no `Proposed` successor yet: run `new` for the successor,
declaring `Amends` with its scope, and stop; the steps below run when the successor is accepted.

Runs inside `accept` of the later record, never while it is `Proposed`. On the earlier ADR, per
*Transitions* and *Status and relation metadata*: Status word stays `Accepted`, an `Amended by`
line whose scope matches the later record's `Amends` in substance, every existing relation entry
kept, an optional note on what remains in force, body untouched. Index per *Index*.

### deprecate

On the owner's explicit instruction, and only when no replacing ADR exists (*States*,
*Transitions*). Status word `Deprecated`; prose after the relation list may explain what remains in
force (*Status and relation metadata*). Index per *Index*: strike through the title.

### editorial-revision

Only when *Editorial revision* permits it, including the owner's prior authorisation (condition 7).
If the request is to change, correct, or update what the record decided, stop and answer with `new`.

1. Read *Editorial revision* in full, including *Conditions* and *What an editorial revision is not*.
2. Fetch the record's prior text from repository history and work from it, not from memory.
3. List, before editing, every passage proposed for removal or relocation, and for each one name
   which of the record's decision content it carries, if any. A passage that carries any is kept, or
   its meaning is kept per condition 4. For a repair, list each correction instead.
4. Name the destination for relocated material per condition 5 and update that document in the same
   change. Material that is removed outright is named as such; history is the only copy.
5. Apply the edit within *Conditions* and the rest of *Editorial revision*.
6. Append one entry to `## Maintenance Note` per *Maintenance Note*, creating the section there if
   the record has none.
7. Correct the index row per *Index* where the abstract no longer describes the record.
8. Report the removal list from step 3 with the condition each item was judged against.

### index

Add or correct the row of one named ADR per *Index*. No other row changes.

## Acceptance checklist

Each item names the README section it is judged against and adds nothing to it (*Acceptance
review*).

- [ ] The record has been read in full.
- [ ] *What an ADR records* and *One decision per record*.
- [ ] *Sources*.
- [ ] Every section the record carries, against *Sections* and *Guidance per section*, including
      whether a section the template does not name belongs.
- [ ] *Information placement* and *References inside a body*.
- [ ] Declared relations, against *Status and relation metadata*; the earlier ADR untouched.
- [ ] The index row, against *Index*.
- [ ] Whether `docs/architecture/` must change has been decided.

## After acceptance

- [ ] Each declared relation is a reciprocal pair on both records, per *Status and relation
      metadata*, and the earlier record's Status word follows *Transitions*.
- [ ] The index rows of both records follow *Index*.

## Report

Every operation ends with: the files touched; the resulting `## Status` sections; whether
architecture pages were or must be updated; and the next action. `new` and `review` additionally
walk the *Acceptance checklist*, marking the items only the owner can judge, and `accept` walks
*After acceptance*.
