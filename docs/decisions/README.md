# Architecture Decision Records

This directory is the decision log of the Duets project. Each Architecture Decision Record (ADR)
captures one architecturally significant decision: the forces that applied when it was made, what
was decided, why, and what followed. ADRs are historical records; the current state of the system
is described by the [architecture documentation](../architecture/).

This file is the single normative statement of how ADRs are written, reviewed, accepted, and
changed. The template, the agent skills, and any checker refer to this file; they do not restate
its rules.

Related files:

- [index.md](index.md) — navigation table (title, keywords, abstract) for every ADR.
- [_template.md](_template.md) — the shape of a new ADR; it carries no rules of its own.

## How the rules apply

A rule is applied to a change: a base state and the state after it. A check before a commit takes
the last commit as the base; a check of a push takes the branch as it was before the push, and a
check of a pull request its target; a question about the log as it stands takes the current state
as both. In a change, a
record is *open* when it does not exist in the base or is `Proposed` there, and *closed* otherwise.

Each rule in this file is of one of four kinds, and its kind alone decides what it reaches:

| Kind | Reaches | Rules of this kind |
|---|---|---|
| Writing | open records | [What an ADR records](#what-an-adr-records) and [One decision per record](#one-decision-per-record); the states a new record may start in ([Transitions](#transitions)); [Writing an ADR](#writing-an-adr), except that no section name appears twice; the title-derived filename and the naming of titles ([File naming and numbering](#file-naming-and-numbering)); that an open record carries no Maintenance Note ([Maintenance Note](#maintenance-note)); [Acceptance review](#acceptance-review) |
| Change | records that the change modifies | the allowed status changes ([Transitions](#transitions)); how relation entries are established and kept ([Status and relation metadata](#status-and-relation-metadata)); [Body immutability](#body-immutability), with the editorial revision and the append-only Maintenance Note, which concern bodies that have left `Proposed`; for text an editorial revision adds, the rules on later ADRs and commit hashes in [References inside a body](#references-inside-a-body) |
| Index | index rows that the change adds or modifies | [Index](#index), except what the standing kind holds for the index |
| Standing | every record and every index row, always | the status vocabulary, the shape of the Status section and its relation entries, and their reciprocity ([Status and relation metadata](#status-and-relation-metadata)); `main` carries no `Proposed` record ([Transitions](#transitions)); the placement and shape of a Maintenance Note; that no section name appears twice; that links resolve and stay within the supported subset ([References inside a body](#references-inside-a-body)); numbering without gaps or reuse, and the heading matching number and filename ([File naming and numbering](#file-naming-and-numbering)); one index row per record, in ascending ADR order, with a non-empty abstract and a title that repeats the record's title and is struck through exactly when [Index](#index) says |

Writing rules govern how a record is written, and a record is written only while it is open. Once
it is closed, its body cannot change to meet a writing rule adopted later, so a closed record is
never judged against one: it was held to the writing rules in force while it was open, and the log
needs no list of exceptions for records that predate a rule. A standing rule governs what every
record carries at all times; when one changes, what it governs is brought into line — metadata
directly, body text only through an editorial revision.

## What an ADR records

An ADR records a single decision that is significant for the project: it shapes structure, quality
attributes, dependencies, public interfaces or contracts, construction techniques, or durable
governance, and a future maintainer could not recover the *why* from the code alone.

An ADR is warranted when a decision will be hard to reverse without deliberate thought and when the
reasoning is not obvious from the result. The existence of real alternatives is strong evidence but
not a precondition: a significant constraint adopted without credible alternatives still deserves a
record, provided the record does not invent alternatives to fill the form.

An ADR is not warranted for implementation detail that follows directly from an earlier ADR, for
ordinary bug fixes, or for choices made purely for consistency with established patterns. A change to
an existing decision is expressed through the lifecycle metadata of the existing ADR plus a new ADR,
not by editing the existing body.

### One decision per record

An ADR has one decision boundary. Split the record when any of the following holds:

- one part could later be reversed while the other part stays in force;
- the parts have different alternatives, drivers, or consequences;
- the parts could be decided by different owners or at different times.

Do not judge the number of decisions by counting markers such as `(chosen)`.

Split only into parts that each warrant a record of their own under
[What an ADR records](#what-an-adr-records). A part that would not — one that follows directly from
the decision it sits in, or a policy too easily reversed to be architecturally significant — is not
split out: it stays with the decision it serves or is left to the governing documentation.

## Lifecycle

### States

| Status | Meaning |
|---|---|
| `Proposed` | Under consideration. The body may be edited freely; once the record leaves this state it changes only through an editorial revision. |
| `Accepted` | Adopted and currently in force. |
| `Rejected` | Considered and deliberately not adopted. |
| `Withdrawn` | Withdrawn before a decision was reached. |
| `Superseded` | Replaced in full by a later ADR. |
| `Deprecated` | No longer applied or recommended, with no direct replacement ADR. |

`Deprecated` is not a synonym for `Superseded`. A record replaced by a later ADR is `Superseded`.

### Status and relation metadata

The `## Status` section is lifecycle metadata. It has this shape:

```markdown
## Status

Accepted

- Amends: [ADR-23](23_ci-and-package-publishing.md) — package topology and signing scope
- Amended by: [ADR-54](54_independent-snapshot-versioning-for-nuget-packages.md) — snapshot packing

Optional prose explaining what remains in force.
```

- The first paragraph is exactly one status word from the table above.
- Relations follow as a bullet list, one relation per bullet, using exactly these labels:
  - `Supersedes` / `Superseded by` — the whole decision is replaced. The entry is the label, a
    colon, and the link to the other record, as in the example above.
  - `Amends` / `Amended by` — only the named scope changes; the rest of the older decision stays in
    force. The entry adds ` — <scope>` after the link, as in the example above; the scope is
    required.
- A relation exists once the later ADR is `Accepted`, and from then on it is recorded on both ADRs
  as a reciprocal pair: `Supersedes` on the later record and `Superseded by` on the earlier one, or
  `Amends` and `Amended by`, each entry naming the other record. For an amendment the two entries
  describe the same scope in substance; identical wording is not required. A supersession has no
  scope: the whole decision is replaced. While the later ADR is `Proposed`, only that record
  carries the relation, as a declaration of what its acceptance will establish; the earlier ADR is
  not touched. A `Rejected` or `Withdrawn` record keeps its declaration as part of what was
  proposed, and the earlier ADR is never updated for it.
- A `Proposed` record may change its declarations freely. Once a record has left `Proposed`, every
  relation entry it carries is kept: entries are not removed, retargeted, relabeled, or given a
  different scope, and no entry is added afterwards except the incoming `Superseded by` or
  `Amended by` written when a later record is accepted. A relation is established only when the
  later record is accepted, and only against an earlier record that is `Accepted` (for an
  amendment) or `Accepted` or `Deprecated` (for a supersession) at that moment. An entry written in
  error is not repaired: like the body, it is part of what the record says, and a relation recorded
  wrongly is answered by a later record that states the relation correctly.
- Prose after the list may explain what remains in force. It is metadata, not decision body.

Status and decision-relation metadata are lifecycle information separate from the immutability of
the accepted body. Updating them when a decision becomes accepted, rejected, withdrawn, superseded,
deprecated, or amended is permitted, and it is required whenever such a relation arises. A reference
from Status or relation metadata to a later ADR does not violate the body's forward-reference rule.

### Transitions

```text
Proposed -> Accepted | Rejected | Withdrawn
Accepted -> Superseded | Deprecated
Deprecated -> Superseded
Accepted + "Amended by" -> Accepted
```

- A new ADR is created as `Proposed`. A record that is new in a change is `Proposed`, `Accepted`,
  `Rejected`, or `Withdrawn`; it never appears first in a state that presumes an accepted life.
- `Accepted` requires an explicit acceptance review (see [Acceptance review](#acceptance-review))
  and the repository owner's approval. In a solo project the proposal and the acceptance may land
  in the same commit, but the review must have been performed and the approval must be explicit.
- `Proposed` exists because the rule protecting the body needs a boundary: it is the interval in
  which a record is drafted and corrected without condition. It does not make a review happen, and a
  record that passes through it within one commit is not thereby less reviewed than one that lingers
  there.
- `main` carries no `Proposed` record. A proposal is settled before it reaches `main` and lands
  there in the state it settled in, so the published log holds outcomes rather than drafts.
  Drafting and review happen in commits that have not reached `main`;
  `scripts/adr-check.cs --no-proposed` rejects a `Proposed` record.
- Superseding or amending an earlier ADR never edits that ADR's body. The earlier ADR's Status
  metadata and index row are updated in the same operation that accepts the later ADR, so a
  proposal that is later rejected or withdrawn leaves no trace on the decision it would have
  replaced.
- A `Deprecated` record becomes `Superseded` when a replacing ADR is accepted later.

## Body immutability

The decision-bearing body is everything from `## Context` to the end of the file. What it is closed
against is not editing as such but falsification: the log must never be brought to say that
something other than what happened was decided, for other reasons, against other alternatives, or at
a different cost. That is the property
[Nygard's rule](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions) about
keeping a reversed decision on record protects, and it is the only reason the body is closed at all.

Once a record leaves `Proposed` its body is therefore not edited. A decision that no longer holds is
superseded or amended by a new ADR; a record whose reasoning turned out to be wrong keeps its
reasoning. The single exception is an editorial revision, defined below, and it is exceptional in
fact as well as in name.

Everything else in and around a record is metadata with its own rules:

| Part | Nature |
|---|---|
| `## Status` section | Mutable lifecycle metadata (see above). |
| `## Maintenance Note` section | Append-only record of editorial revisions (see below). |
| `index.md` row | Mutable navigation metadata; may be corrected whenever it is inaccurate. |
| `docs/architecture/` | Mutable current-state documentation. |
| ADR number, filename, and title | Stable once accepted. They are the record's identifier and the scope it announces. |
| Any other text in the record, outside the title line, the body, the `## Status` section, and the `## Maintenance Note` section | Historical text, as immutable as the body. |

Bodies of `Rejected`, `Withdrawn`, `Superseded`, and `Deprecated` records are historical in the same
way.

### Editorial revision

An editorial revision is the only permitted edit to a body that has left `Proposed`, and it is one
of two things:

- removing or relocating material that carries no decision-bearing meaning, performed when a
  record has stopped working as a decision record: not to tidy one, not to shorten one, and never
  because a record is long (see [Length](#length));
- repairing a typographical error, or a link that no longer resolves, where the repair changes no
  meaning.

Neither is ordinary maintenance, and no other edit is an editorial revision.

Supersession is not the instrument for either. A record whose decision still holds is not
superseded: marking it so would say that a later ADR replaced a decision nothing replaced, which is
the exact falsification this section exists to prevent. Nygard and
[Fowler](https://martinfowler.com/bliki/ArchitectureDecisionRecord.html) condition their rules on
the decision being changed or reversed, and
[Zimmermann's](https://ozimmer.ch/practices/2023/04/03/ADRCreation.html) remedy for a record that
has absorbed detailed design is to move that design to a document meant for it; his article does not
go into ADR maintenance, so applying that remedy to an accepted record is this project's own step.
That reasoning is what permits the exception.

It is a deliberate departure all the same.
[AWS Prescriptive Guidance](https://docs.aws.amazon.com/prescriptive-guidance/latest/architectural-decision-records/adr-process.html)
and the
[Microsoft Azure Well-Architected Framework](https://learn.microsoft.com/en-us/azure/well-architected/architect-role/architecture-decision-record)
treat an accepted record as immutable without qualification. Fowler, on the same page that
conditions supersession on a changed decision, also writes that an accepted ADR should never be
reopened or changed. The immutability advice in
[joelparkerhenderson's collection](https://github.com/joelparkerhenderson/architecture-decision-record)
is not to alter existing information, although the same collection reports that in practice
mutability has worked better for its authors' teams. This project accepts the departure and answers
for it with the conditions below and with a permanent note in every record a revision touches.

#### Conditions

An editorial revision is permitted only when all of the following hold. They are judged by reading
the record against its prior text, never by a checker.

1. The decision, its scope, and its force are unchanged.
2. Nothing a reader needs in order to judge what was decided and why is lost: the forces that
   applied at the time, the constraints and drivers, the alternatives and the reasons they were
   rejected, and the consequences — the negative and the neutral ones as much as the positive.
3. What is removed is not itself a separate architectural decision. Module boundaries, dependency
   directions, public interfaces, persistence formats, security boundaries, ownership, lifecycle,
   and concurrency boundaries are decisions even where they read as implementation detail.
4. Where detail carried meaning, the meaning stays in the record although the detail goes.
5. For removed or relocated material, the destination is named: an owning document updated in the
   same change, or removal whose prior text stays recoverable from repository history.
6. Identity is not falsified. Number, filename, title, and Status word are unchanged, and no record
   is moved to `Superseded`, `Deprecated`, or `Rejected` for a decision that did not change.
7. The repository owner authorises the revision in advance, for the records it names.
8. The revision is recorded in the record's own `## Maintenance Note` section in the same change.

#### What an editorial revision is not

None of the following is an editorial revision. Each changes what a reader understands was decided,
and each is expressed as a new ADR that supersedes or amends the old one.

- Changing what the decision says, how far it reaches, or how strongly it binds.
- Adding a reason that did not apply at the time, or presenting a fact learned later as though it
  had been known.
- Adding an alternative that was not actually considered, or removing one that was.
- Removing a negative consequence, an accepted trade-off, or a precondition that did not hold.
- Rationalising the contemporaneous context with hindsight.
- Rewriting an older record so that it reads as though it had chosen what the project does now.
- Correcting a conclusion that turned out to be wrong.

The test is one question: after this edit, would a future reader understand something different
about what was decided, why, or what it cost? If the answer is yes, it is not an editorial revision.

An editorial revision keeps every section named in [Sections](#sections) that the record carries,
in its order, and adds none of them: removing one would drop what it recorded, and adding one would
write what was not written. A section not named there may be removed when it carries nothing the
decision needs.

### Maintenance Note

`## Maintenance Note` is append-only metadata. It sits immediately after `## Status` and before
`## Context`, and a record carries one only after its first editorial revision; an open record
therefore carries none. Entries are added,
never removed, reworded, or reordered, and the section is not part of the body.

Each entry is a bullet stating what was removed or relocated, where it went, what was retained,
and who authorised the revision. Entries stand in the order they were added. When a revision
happened is a question for repository history, and an entry does not restate it, for the same
reason a body does not embed a commit hash. A typographical or link repair may be a single line
naming what was corrected.

A change to the body of a record that has left `Proposed` with no new entry here is a rule
violation, and `scripts/adr-check.cs` rejects it.

## Writing an ADR

### Sources

A record is written from the decision as it was made and the reasons stated for it. The
implementation and tests may be read to verify facts and current contracts, never to supply a
rationale that was not stated.

### Sections

Core sections, always present, in this order: `Status`, `Context`, `Decision`, `Rationale`,
`Consequences`.

Conditional sections, included only when they carry substance and deleted otherwise:
`Decision Drivers` (after Context), `Considered Alternatives` (after Decision Drivers, before
Decision), `Confirmation` and `Revisit Conditions` (after Consequences, in that order).

Nothing but the title line precedes `## Status`, and `## Context` follows it directly. A record may
add a section the decision needs, placed anywhere after `Context`; the acceptance review judges
whether it belongs. No section name appears twice.

### Guidance per section

**Context** describes the technological, project-local, and organizational forces at decision time in
value-neutral terms. It does not argue for the decision, does not describe facts learned from the
current implementation as if they were known then, does not mention ADRs that did not exist at the
time, and does not guess at history that is not known; say "unknown" or verify contemporaneous
evidence.

**Decision Drivers** lists what mattered: criteria, constraints, priorities.

**Considered Alternatives** lists only options that were seriously considered, compared at the same
level of abstraction and decision scope as the chosen one. Omit the section rather than build straw
men. A claim that no alternative existed must itself be supportable.

**Decision** states the choice and its scope in full sentences in the active voice. It describes the
boundary that binds future implementation, not an inventory of the implementation. A conditional
conclusion keeps its conditions in the same sentence or paragraph.

**Rationale** explains why the choice beats the alternatives against the drivers and trade-offs. It
does not restate the decision, does not convert "it works well now" into a historical reason, and
keeps the uncertainty and constraints that applied at the time.

**Consequences** records the significant positive, negative, and neutral results honestly. There is
no required count of negatives, but a record with a real trade-off and no stated negative is not
accepted. Non-obvious results in areas such as breaking changes, security boundaries,
compatibility, ownership, lifecycle, and concurrency are part of the decision and are never dropped
as verbosity.

**Confirmation** names how compliance with the decision is or will be verified.

**Revisit Conditions** names what would trigger reconsideration.

### Length

One to two pages is the guideline, following Nygard. There is no limit, hard or soft, and length
alone is never a defect. A record well beyond the guideline is only a prompt to ask whether it holds
two decisions or has become a specification; a record that needs its length to preserve the context,
conditions, and trade-offs of one decision keeps it. Neither a review nor a check fails on length.

### Information placement

| Information | In the ADR | Owning location |
|---|---|---|
| The decision, contemporaneous context, rationale, significant consequences | Yes | ADR |
| Non-obvious security, compatibility, ownership, lifecycle, concurrency boundaries the decision depends on | Yes | ADR; current detail may also live in owning docs |
| Alternatives that were actually considered | Yes | ADR |
| Complete wire formats, field or operation inventories | No | protocol/reference documentation |
| API signatures, type and member listings | No | package README, XML documentation, API reference |
| Pseudocode, step-by-step algorithms | No | implementation and tests; architecture docs if needed |
| Current module layout and data flow | No | `docs/architecture/` |
| Configuration value inventories | No | configuration reference |
| Numbers or limits that *are* the decision | Yes | ADR, with a pointer to where the current value lives |
| Investigation logs, full discussions, full comparison data | Summary only | issue, RFC, review artifact |
| Commit hashes and other volatile references | No | a stable PR, tag, or issue |

"It lives in another document" does not prove the ADR does not need it. A condition required to
understand the decision stays in the ADR even when the owning document repeats it.

### References inside a body

- A body references only earlier ADRs. Later ADRs are referenced from Status metadata.
- Link ADRs by relative file path so links stay valid within the repository; an absolute path is
  an error.
- Links use the Markdown subset stated in [CONTRIBUTING.md](../../CONTRIBUTING.md#build-format-and-test):
  inline links with a single-token destination and no title, reference definitions, reference
  usages, and footnotes. Anything that starts like a link but falls outside that subset is an
  error, never ignored.
- Do not embed commit hashes; history rewrites invalidate them.

## Acceptance review

A record becomes `Accepted` only after a reviewer has read it in full and found that it meets the
writing rules — [What an ADR records](#what-an-adr-records),
[One decision per record](#one-decision-per-record), [Sources](#sources), every section it carries
against [Sections](#sections) and [Guidance per section](#guidance-per-section) (including whether
a section the template does not name belongs), [Information placement](#information-placement), and
[References inside a body](#references-inside-a-body) — that its declared relations follow
[Status and relation metadata](#status-and-relation-metadata) with the earlier ADR untouched, that
its index row follows [Index](#index), and that the need to update `docs/architecture/` has been
decided; and after the repository owner has approved it. None of this is judged by a checker. The
acceptance applies the declared relations in the same change, per [Transitions](#transitions).

## Index

`index.md` is mutable navigation metadata. It holds one row per record in ascending ADR order.
Existing rows are corrected when inaccurate; the table is never regenerated wholesale.

- **Title** repeats the ADR title; a short italic note may summarize lifecycle state. The title is
  struck through if and only if the record is `Superseded` or `Deprecated`.
- **Keywords** are the terms a human or agent would search for. There is no minimum count.
- **Abstract** is one sentence answering what was decided and why. Mechanism listings without a
  reason do not pass review.

## File naming and numbering

- Files are named `N_<title>.md`: the sequential number, an underscore, then the title in lowercase
  kebab-case: ASCII letters and digits in groups separated by single hyphens, no leading, trailing,
  or doubled hyphen, special characters (`.`, `/`, `'`) removed.
- `N` is the highest existing number plus one, never zero-padded, never reused. Records are never
  deleted, so the numbers 1 to the highest form a sequence without gaps.
- The heading `# ADR-N: <Title>` must match the number and the filename.
- Titles name the decision ("Use X", "Separate Y from Z"), not a vague topic.

## Mechanical checks and semantic review

`scripts/adr-check.cs` verifies the structural part of these rules (see
[CONTRIBUTING.md](../../CONTRIBUTING.md#build-format-and-test)), each against what its kind reaches:
numbering, filename and heading agreement, status vocabulary, relation resolution on both sides,
Maintenance Note placement, links, and index rows on every record; section layout, the title-derived
filename, references to later ADRs, and the absence of a Maintenance Note on open records; and,
given a base revision, the change rules on closed records — whether a body changed without a new
`## Maintenance Note` entry, whether it kept the sections the template names, whether it gained a
reference to a later ADR, and whether relation entries were kept. Without a base revision the check
can see only the current state, so it treats only `Proposed` records as open.

Semantic rules — whether the decision is single, the alternatives real, the context
contemporaneous, the rationale sufficient, the consequences honest, the abstract informative, or an
editorial revision meaning-preserving — are verified only by reading. The check can tell that a
body changed and that a note was written; it cannot tell whether the note is true. No check, build,
test, or formatter result is evidence of semantic correctness.
