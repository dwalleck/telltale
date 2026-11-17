# 01: The footer counts issues re-estimated after their cycle started

**What to build:** A reader who checks a report against the Jira board can see why a point bucket's issues differ from the board's. The tool puts a delivered issue in the **point bucket** of its estimate at the start of its cycle, so that a later re-estimate does not move it (ADR-0006). Jira shows the estimate the issue has now. The data quality footer says how many delivered issues have an estimate now that differs from the one they are bucketed by, and `query` returns the same number. No existing figure changes, and ADR-0006 does not change.

The number is the same under either brief visit treatment: an issue left without a cycle time keeps the bucket of its cycle-start estimate (D102).

A new count is defined here, so the record changes in the same commit: the glossary and the owner decisions list.

Background: owner request of 2026-11-17, during the brief visit treatment run, after the review of that spec's ticket 03 (`.scratch/brief-visit-treatment/issues/03-exclude-leaves-brief-start-cycles-out-of-cycle-time.md`, its Comments on D102).

**Blocked by:** None in this folder. Build it after `spec/brief-visit-treatment` is merged, because both change the footer and the method note.

**Status:** needs-info

Waiting on the owner to confirm the readings below. They are the author's and bind only once confirmed.

## Readings not yet confirmed

1. **Compared with the estimate as of the last sync,** not the estimate at delivery. The purpose is to explain a difference from what the board shows, and the board shows the estimate now. A change made after delivery therefore counts.
2. **One number for the whole store,** beside the footer's two existing whole-store counts, not one per sprint. A per-sprint number would nearly always be one or two issues, which the minimum group size withholds under its default numbers, so the line would nearly always read `suppressed`.
3. **Any difference counts once:** raised, lowered, estimated only after the start, or the estimate removed. The number is not split by direction, so that a direction cannot rest on one or two people.
4. **A delivered issue that never entered a work status** is bucketed by its estimate at delivery (D35) and is compared with its estimate now in the same way.
5. **Withheld as the other whole-store counts are:** when fewer distinct assignees than a figure needs are behind the issues it counts, and when it is zero, so that a zero reads as a withheld count does. With the configured number at zero it is shown, a count of nothing as `0`.
6. **Only delivered issues are counted,** because only delivered issues have a point bucket.

## Acceptance criteria

- [ ] In a synthetic store with at least three assignees behind the counted issues, a delivered issue re-estimated between its cycle start and its delivery, and one re-estimated after delivery, are both counted, and the footer has one line with the number
- [ ] An issue whose estimate never changed, and an issue re-estimated only before its cycle started, are not counted
- [ ] An issue estimated only after its cycle started, and one whose estimate was removed, are each counted once
- [ ] A delivered issue that never entered a work status is counted when its estimate now differs from its estimate at delivery
- [ ] An issue that is not delivered is never counted
- [ ] The number is the same under `count` and `exclude` for cycle time, with an issue whose cycle starts with a brief visit among those re-estimated (D102)
- [ ] `query` returns the number and a suppressed flag from the metric view that holds the footer's other whole-store counts
- [ ] The number is withheld with fewer distinct assignees behind it than a figure needs, and when it is zero; an unassigned issue counts toward nobody
- [ ] The view reads the minimum group size's numbers from the one place every other metric view reads them
- [ ] The derived data gains nothing about a person
- [ ] No existing figure, bucket or view row changes; the reports differ from before only by the new footer line and the method note's sentence about it
- [ ] The method note says what the line counts and why the tool buckets by the estimate at the start of the cycle
- [ ] The README's "What a report shows" and its `query` section name the new number
- [ ] The glossary's "Point bucket" entry says a report's buckets can differ from the board's and where the number is; "Minimum group size" covers the count; the owner decisions list records the confirmed readings
- [ ] The commit message gives the reason for the new count
- [ ] `derive` rebuilds it with the rest; two derives of the same raw data and settings give the same number
- [ ] Every existing test passes, apart from assertions on the footer and on the view's columns that gain the new number; the commit message lists each changed assertion
- [ ] Tests use TUnit; expected multi-line strings are normalized to LF; all fixtures are synthetic
