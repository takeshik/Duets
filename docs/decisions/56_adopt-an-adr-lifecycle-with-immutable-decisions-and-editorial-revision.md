# ADR-56: Adopt an ADR Lifecycle with Immutable Decisions and Editorial Revision

## Status

Proposed

- Amends: [ADR-1](1_design-documentation-strategy.md) — the append-only rule for decision records

## Context

The decision log holds fifty-five records. [ADR-1](1_design-documentation-strategy.md) made
`docs/decisions/` append-only, with old decisions superseded rather than rewritten, and the agent
skill that maintained the log added that a superseded ADR is marked
`Deprecated (superseded by ADR-N)`. Neither said what "append-only" covered. The Status line and the
decision body were treated as one text, so a supersession note could be read as a rewrite of
history, while a genuine rewrite had no rule that named it. Status text was free-form; twelve
different phrasings existed, partial and full replacement were expressed with the same words, and
several relations were recorded on one ADR only. New ADRs were written directly as `Accepted`, so no
state existed in which a record could still be corrected before it became historical.

A second gap sits alongside the first. Several accepted records had absorbed material that is not
decision content: wire formats, API inventories, pseudocode, component responsibilities, and
restatements of the current implementation. Such a record is hard to read as a decision and goes
stale against code it was never meant to track, yet its decision is still in force. The convention
named two responses to a record, supersession and deprecation, and both describe a decision that
was replaced or retired.

The convention existed in two places, ADR-1 and the agent skill, and both carried the same
ambiguity. The log is maintained by the owner and by AI agents alike.

## Decision Drivers

- Keep every accepted record a faithful account of what was known, considered, and decided at the
  time.
- Make supersession and amendment unambiguous, recorded on both sides, and machine-resolvable.
- Give a record an interval in which it can be drafted and corrected before it becomes historical.
- Give a record that has stopped working as a decision record a lawful repair that does not enter a
  decision the project never made.
- Keep that repair rare, visible, and answerable, given that no check can judge whether it preserved
  meaning.
- State the rules in one place, precisely enough to be followed without interpretation, so that
  agents and humans read the same text.
- Let the structural parts of the rules be checked mechanically without implying that meaning is.
- Do not force existing records to be rewritten to fit new rules.

## Considered Alternatives

### A: Freeze the text of an accepted record without exception

- Pro: matches the most prescriptive published guidance; nothing has to be trusted beyond the rule
  itself, and a check can decide every case.
- Con: a record that has become a specification stays one permanently, and the only sanctioned
  responses misstate the log — supersession claims a replacement that did not occur, deprecation
  retires a decision still in force.

### B: Allow an accepted body to be edited whenever it is outdated or verbose

- Pro: cheapest way to keep records readable.
- Con: erases the boundary between what was decided then and what maintainers know now, which is
  the property that makes a decision log worth reading.

### C: Close the body against falsification, with editorial revision as the one exception (chosen)

- Pro: protects what immutability exists to protect while naming the one repair that does not
  falsify anything; keeps the log accurate about what was and was not decided.
- Con: whether a revision preserved meaning is a judgment no check can make, so the rule depends on
  conditions being applied honestly and on the revision being recorded where readers will see it.

## Decision

An ADR's lifecycle is carried by its `## Status` section, which is mutable metadata distinct from
the decision body. The status vocabulary is `Proposed`, `Accepted`, `Rejected`, `Withdrawn`,
`Superseded`, and `Deprecated`; `Deprecated` is not a synonym for `Superseded`. Relations between
records are expressed as `Supersedes` / `Superseded by` for whole replacement and `Amends` /
`Amended by` with a named scope for partial change, and every relation is recorded on both records.
Updating Status or relation metadata is never a body edit and may reference later ADRs.

A new ADR starts as `Proposed` and becomes `Accepted` only after an explicit acceptance review and
the repository owner's approval. In a solo project both may land in one commit, provided the review
was performed.

What an accepted body is closed against is falsification, not editing as such: the log is never
brought to say that something other than what happened was decided, for other reasons, against
other alternatives, or at a different cost. Once a record leaves `Proposed` its body is therefore
not edited, with one exception. An editorial revision removes or relocates material that carries no
decision-bearing meaning, or repairs a typographical error or a link that no longer resolves without
changing meaning. It is permitted only when the decision and its scope are unchanged, nothing needed
to judge what was decided and why is lost, what is removed is not itself a separate architectural
decision, meaning that lived in removed detail stays in the record, the destination of removed or
relocated material is named, the record's identity and Status are untouched, the repository owner
authorised the revision in advance for the records it names, and the revision is recorded in the
record's own `## Maintenance Note` section in the same change. A removal or relocation is performed
only when a record has stopped working as a decision record — never to tidy one, and never because
a record is long.

`## Maintenance Note` is append-only metadata placed immediately after `## Status`, before
`## Context`. A body change to a record past `Proposed` with no new entry there is a violation, and
so is one that removes, adds, or reorders a section the template names; `scripts/adr-check.cs`
rejects both.

`docs/decisions/README.md` is the single normative statement of these rules and of the writing and
review guidance that accompanies them. The template, agent skills, and any checker refer to it and
do not restate it. The Status sections of existing records written in free form are migrated to the
structured form, because Status is metadata every record carries.

## Rationale

The parts of this decision form one boundary. What is decided is the rule protecting the body.
Separating Status from the body is what lets a changed decision be recorded without editing a body,
so that the rule needs no exception for supersession; the `Proposed` state is where the rule begins
to apply; and the relation vocabulary is the form the separated Status takes. Each exists to make
the body rule statable without exceptions, none would warrant a record of its own, and none was
weighed against an alternative of its own beyond the practice it replaced. The alternatives above
therefore differ in how the body is treated once Status is separate, and the consequences below are
those of the whole.

Separating Status from the body resolves the ambiguity that produced the original confusion:
supersession becomes a metadata update by definition, so the rule protecting the body no longer has
to be weakened to allow it. Requiring both sides of a relation removes the one-sided links the log
had accumulated and gives a mechanical check something it can verify. Distinguishing amendment from
supersession records the truth that most later decisions change part of an earlier one; the old
"partially superseded" prose carried that meaning but in no consistent form.

The `Proposed` state is what makes the rule protecting the body workable: it is the interval in
which a record is drafted and corrected without condition, so that freezing has a boundary. It is
not a review station. In a solo project a proposal and its acceptance may land in the same commit,
and the state does not by itself make the review happen; saying that it does would credit a status
word with work only a reader does.

Alternative B was rejected because a log whose records can be brought into line with present
knowledge stops being evidence of anything.

Alternative A was rejected because it mistakes the text for the thing being protected. Nygard's
rule[^1] is that a reversed decision stays on record, so that it remains possible to know what was
once decided; Fowler's[^2] is that a changed decision is superseded rather than modified. Both are
written about a decision that changed. Removing a wire format from a record whose decision still
stands changes nothing a reader relies on, while the responses A permits do: supersession would
enter a replacement that never happened, and deprecation would retire a decision the project still
follows. Zimmermann's[^3] remedy for a record that has become a documentation master is to move the
detailed design to a document meant for it, which is the same edit; his article states that it does
not go into ADR maintenance, so applying the remedy to an accepted record is this project's step,
not his. Choosing A would have meant either leaving such records as they are or falsifying the log
to repair them.

This is nonetheless a departure, and the record should say so plainly. AWS Prescriptive Guidance[^4]
and the Microsoft Azure Well-Architected Framework[^5] state without qualification that an accepted
record is immutable and that changes require a new record. Fowler, on the same page that conditions
supersession on a changed decision, also writes that an accepted ADR should never be reopened or
changed. The immutability advice in joelparkerhenderson's collection[^6] is not to alter existing
information, although the same collection reports that in practice mutability has worked better for
its authors' teams. None of the sources cited here sanctions removal of this kind. The reasoning
above is why the project departs, and the conditions and the note are the price it pays for
departing.

The exception is deliberately narrow. Its conditions are not checkable, so the controls that remain
are that it must be rare, that it is authorised before it happens rather than reported after, and
that it leaves a permanent entry in the record it touched. Length is excluded for the same reason:
a long record invites the question of whether it holds two decisions, but length alone is not a
defect and must not become a standing licence to edit.

[^1]: Michael Nygard, [Documenting Architecture Decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions),
    15 November 2011.
[^2]: Martin Fowler, [Architecture Decision Record](https://martinfowler.com/bliki/ArchitectureDecisionRecord.html).
[^3]: Olaf Zimmermann,
    [How to create Architectural Decision Records (ADRs) — and how not to](https://ozimmer.ch/practices/2023/04/03/ADRCreation.html),
    3 April 2023.
[^4]: AWS Prescriptive Guidance, [Architectural decision record process](https://docs.aws.amazon.com/prescriptive-guidance/latest/architectural-decision-records/adr-process.html).
[^5]: Microsoft Azure Well-Architected Framework,
    [Maintain an architecture decision record (ADR)](https://learn.microsoft.com/en-us/azure/well-architected/architect-role/architecture-decision-record).
[^6]: [Architecture decision record (ADR)](https://github.com/joelparkerhenderson/architecture-decision-record),
    sections "Suggestions for writing good ADRs" and "Teamwork advice for ADRs".

## Consequences

- Positive: a supersession or amendment can be recorded without touching historical text, and the
  relation can be followed in either direction.
- Positive: the rules exist in one file; skills and checkers that drift from it are wrong by
  definition.
- Positive: a record that has become a specification has a repair that does not enter a decision
  nobody made, and the repair is visible in the record afterwards.
- Negative: whether a revision preserved meaning cannot be checked, only reviewed. The rule is as
  good as the honesty of the person applying it, and a revision that quietly drops an accepted
  trade-off will pass every automated gate.
- Negative: the project now differs from the guidance most likely to be cited against it, so each
  revision has to carry its own justification rather than resting on common practice.
- Negative: typographical errors and links that no longer resolve are repairable, but every such
  repair is an editorial revision, authorised in advance and noted in the record, so trivial fixes
  are not free.
- Negative: two-sided relation metadata is written by hand on both records; the check finds a
  one-sided update but does not write the missing side, and a one-sided update is a rule violation
  rather than a stylistic lapse.
- Negative: a check run without a base revision sees only the current state and holds only
  `Proposed` records to the writing rules, so a record that arrives already settled is checked
  against them only where a base is given.
- Neutral: because a closed body cannot change to meet a rule, rules on how a record is written
  reach a record only while it is open — new in a change or still `Proposed`. A closed record is
  never judged against a writing rule adopted after it closed, so earlier records need neither
  rewriting nor a list of exceptions.
- Neutral: the Status sections of earlier records written in free form are migrated to the
  structured form once; their bodies are unchanged by it.

## Confirmation

`scripts/adr-check.cs` verifies the structural part of this decision: the status vocabulary, the
reciprocity of relation entries, and the placement and shape of a `## Maintenance Note` on every
record; the section layout, title-derived filename, and references to later records of open
records; and, against a base revision, whether a body that has left `Proposed` changed without a
new entry there, kept the sections the template names, or gained a reference to a later record. Its
output states that it decides structure only.

Whether an editorial revision preserved what a record decided is confirmed by reading the result
against the prior text before the revision is committed. No check can establish it, and the
governance says so rather than implying that a passing check is evidence.
