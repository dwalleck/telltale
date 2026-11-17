# 01: The footer counts cycles that start with a brief visit

**What to build:** With statuses listed under `briefVisitThresholds` and no other setting, each sprint report's data quality footer says how many of the sprint's delivered issues had a cycle that starts with a brief visit, and `query` returns the same number from a new metric view. The owner sees the size of the problem in cycle time before choosing to exclude anything. No existing figure changes. This is the tracer bullet for cycle time: one sprint, through the derive, a derived flag, the view and the report. Ticket 03 acts on the flag.

A cycle starts with a brief visit when the stay that begins at the cycle's start is a brief visit under its status's threshold (D90). The cycle's start is what it is today. A stay bounded by a changelog anomaly is never a brief visit, so such a cycle does not count.

The view applies the whole withholding rule from this ticket on, so that an unguarded count never exists. Ticket 04 probes it.

A new count is defined here, so the record changes in the same commit: the glossary and the owner decisions list.

Spec: `../spec.md` (owner decisions D87 and D90; "Deriving"; "The metric views"; "The report", footer, cycle time; user stories 21, 25, 29, 33, 35, 36, 38 and 39).

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] In a synthetic sprint that passes every check, with at least three assignees behind the counted issues, a catch-up sequence gives one footer line with the number of delivered issues whose cycle starts with a brief visit
- [ ] The line says the issues are in the cycle time figures, which is the treatment in force until ticket 03
- [ ] Whether a cycle starts with a brief visit is a pure function beside the brief visit rule, with its own tests: a brief first stay, a long first stay, a brief visit only later in the cycle, a first stay bounded by a changelog anomaly, an issue created in a work status and moved on within the threshold, a first stay in a work status that is not listed
- [ ] A move into a work status and straight back out, with a real start later, counts (D91)
- [ ] A delivered issue that never entered a work status does not count
- [ ] `query` against the new metric view returns one row per sprint, with the number and a suppressed flag, and the view is among those `query` may read
- [ ] The view has a row only when a listed status has the work role
- [ ] The number is withheld when the sprint's delivered count is withheld, when fewer distinct assignees than a figure needs are behind the issues it counts, and when it is zero
- [ ] The view reads the minimum group size's numbers from the one place every other metric view reads them
- [ ] The derived data gains a flag per issue and nothing about a person: no duration, no timestamp
- [ ] `derive` rebuilds the flag with the rest; two derives of the same raw data and settings give the same numbers
- [ ] With no status listed, the report and every existing view's results are identical to today's, apart from the as-of time
- [ ] With statuses listed, the report differs from today's only by the new footer line
- [ ] The anomaly count and the console output of `derive` and `run` are unchanged
- [ ] The glossary's "Minimum group size" entry has a sentence covering this count, and the owner decisions list has D87 and D90
- [ ] The commit message gives the reason for the new count
- [ ] Every existing test passes, apart from lists of metric views that gain the new view
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
