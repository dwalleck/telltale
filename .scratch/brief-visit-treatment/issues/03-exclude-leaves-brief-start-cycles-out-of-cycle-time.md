# 03: `exclude` leaves brief-start cycles out of cycle time

**What to build:** The owner writes `cycleTime` as `exclude` under `briefVisitTreatment`. After `derive`, a delivered issue whose cycle starts with a brief visit has no cycle time, so a cycle known to be too short does not pull the median and the P85 down (D85). The issue is still delivered: it stays in the delivered count and in every figure that is not a duration. The baseline cycle times are adjusted in the same way. The report names cycle time in the line under its heading, carries one note under the cycle time table, and the footer line of ticket 01 says the issues have no cycle time.

The two parts are set separately: each of the four combinations of `count` and `exclude` gives its own figures.

This changes how a headline metric is measured, so the record changes in the same commit: the glossary and the owner decisions list.

Decided at the start of the run (2026-11-17, spec "Decisions made at the start of the implementation run"):

- D101: under `exclude` for cycle time, the note under the cycle time table is printed whatever the table holds.
- A refusal is built in the ticket that first accepts the value it refuses. This ticket refuses `cycleTime` as `exclude` with no status listed, and with no listed status that has the work role.
- Each ticket corrects the sentence of the method note that it makes untrue. The wording under `count` does not change; ticket 06 writes the full paragraph.

Spec: `../spec.md` (owner decisions D83, D85, D86, D90, D91, D94; "Configuration"; "Deriving"; "The metric views"; "The report"; user stories 2, 17 to 20, 22, 23 and 25).

**Blocked by:** 01, 02

**Status:** ready-for-agent

- [ ] A configuration file with `cycleTime` as `count` or `exclude` loads, alone or beside `timeInStatus`; an absent part means `count`
- [ ] Under `exclude`, a delivered issue whose cycle starts with a brief visit is stored as delivered with no cycle time, as an issue that never entered a work status is
- [ ] Its delivered count, seat value inputs and reopen count are unchanged
- [ ] A brief visit later in the cycle changes nothing; a cycle whose first stay is bounded by a changelog anomaly is kept
- [ ] An issue created in a work status and moved on within the threshold has no cycle time
- [ ] A move into a work status and straight back out, with a real start later, has no cycle time (D91)
- [ ] The bucket's issue count, median and P85, in business and in calendar days, are of the cycles that remain, and the baseline cycle time figures are adjusted in the same way
- [ ] A catch-up sequence is asserted under each of the four combinations of the two parts: the time in status row, the cycle time bucket and the footer
- [ ] The line under the heading names which of the two metrics were adjusted
- [ ] `cycleTime` as `exclude` with no status listed in `briefVisitThresholds`, or with no listed status that has the work role, is refused, with a message that names the file and the setting
- [ ] Under `exclude` for cycle time, the cycle time table carries one note under it, whether or not anything was left out in the sprint, also when the table is the single word `suppressed` or says no issue has a cycle time (D101)
- [ ] Any sentence of the method note that `exclude` for cycle time makes untrue is corrected; under `count` its wording is unchanged
- [ ] The footer's cycle time line says the issues are in the cycle time figures under `count` and have no cycle time under `exclude`
- [ ] The number in that line is the same under either treatment
- [ ] After the part changes, `report` and `query` refuse and say to run `derive`
- [ ] The raw tables are identical before and after a derive under `exclude`
- [ ] With both parts as `count`, the report and every view's results are identical to those of ticket 01
- [ ] The glossary has a sentence in "Cycle time" saying what `exclude` leaves out; the owner decisions list has D85, D91, D94 and D101
- [ ] The commit message gives the reason and says the change was made on 2026-11-16, after results had been seen
- [ ] Every existing test passes unchanged
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
