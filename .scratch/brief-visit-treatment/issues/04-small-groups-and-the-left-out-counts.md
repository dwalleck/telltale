# 04: Small groups and the left-out counts

**What to build:** A reader can judge how much a row changed: under `exclude` for time in status, the footer says, per listed status, how many issues had no other time in the status and so are not in the row's count, and `brief_visits_per_sprint` gives the same number to `query`. A team member can rely on leaving issues out never making a figure rest on one or two people: every adjusted figure and every count of issues left out is withheld by the same rule as the rest of the report, in the report and through `query`, and no combination of queries over the metric views within one derive gets round it.

Tickets 01 to 03 put the rules in the views. This ticket adds the left-out number, tests every rule from every side, and fixes what it finds.

Four things are accepted and recorded, not closed: the delivered count less the sum of the bucket counts bounds the number of delivered issues with no cycle time (D94); a figure under `count` can be set beside the same figure under `exclude` by deriving twice (D95); the cycle count of ticket 01 can be combined with shown brief visit counts to give a withheld one or a bound on it (D97); and a listed status with a footer line and no table row shows that its left-out number is at least one (D98). The probe pins each as shown.

Each ticket corrects the sentence of the method note that it makes untrue: here, what the note says about when a count reads `suppressed` and about a listed status with no row. The wording under `count` does not change.

Spec: `../spec.md` (owner decisions D93, D94, D95; "The metric views"; "The report", footer, per listed status, and marks; "Testing Decisions", suppression; user stories 24 and 30 to 36).

**Blocked by:** 02, 03

**Status:** ready-for-agent

- [ ] `brief_visits_per_sprint` has one row per sprint and listed status that had a row before anything was left out, under either treatment
- [ ] The view gains the number of issues left out of the row: null under `count`; under `exclude`, withheld when fewer distinct assignees than a figure needs are behind the issues it counts, and when it is zero
- [ ] Under `exclude`, the footer's existing line for a listed status goes on to say that those visits are left out of the row, and how many of the issues are not in the row's count; a withheld number reads `suppressed`
- [ ] A status with every issue left out has no row in the time in status table and still has a footer line that explains it
- [ ] A row that had three assignees behind it and has two after leaving out has its median and total withheld
- [ ] A point bucket in the same position has its figures withheld, counted over the issues that still have a cycle time
- [ ] A baseline figure is withheld when any baseline sprint's adjusted row is withheld
- [ ] A left-out count with two assignees behind it is withheld; a left-out count of zero reads the same as a withheld one
- [ ] The cycle count of ticket 01 is withheld when the delivered count is withheld
- [ ] The marks on the tables are present with nothing left out, so that a mark cannot tell a zero from a withheld count
- [ ] A probe over the metric views finds no query, within one derive, that tells a zero from a withheld count or recovers a withheld count, other than what D94, D97 and D98 record; the probe pins each of those as shown
- [ ] The brief visit count follows D99 under `exclude`: withheld by the recorded row's check and its own
- [ ] The method note's sentences on when a count reads `suppressed`, and on a listed status with no row, are true under `exclude`; under `count` their wording is unchanged
- [ ] Each case is asserted in the report and by `query`
- [ ] With the configured numbers at zero, every count is shown, and a count of nothing is `0`
- [ ] The derived data holds issue, sprint and status IDs, durations and flags, and nothing else
- [ ] The glossary's "Minimum group size" entry has a sentence covering the left-out count, and the owner decisions list has D95 and D98
- [ ] A leak found here is reported with the broad rule that would close it, as a decision needed, before a narrow fix is made
- [ ] The existing privacy checks still pass
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
