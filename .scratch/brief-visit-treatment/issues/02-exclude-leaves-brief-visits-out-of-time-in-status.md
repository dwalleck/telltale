# 02: `exclude` leaves brief visits out of time in status

**What to build:** The owner writes `briefVisitTreatment` with `timeInStatus` as `exclude` in the configuration file. After `derive`, the time of every brief visit to a listed status is left out of each issue's time in that status. An issue with no other business time in the status leaves that status's row, so the row's count, median and total, and the baseline median, are of what remains (D84). The report says so in one line under its heading and marks the row of every listed status, so that an adjusted figure is never shown unannounced. This is the tracer bullet for the setting: configuration, the derive settings, the treatment function, the derived data, the views and the report.

Only the `timeInStatus` part is accepted in this ticket. Ticket 03 adds `cycleTime`, so that the tool never accepts an `exclude` it does not act on.

The treatment is a closed set of cases written as a union type, and it is applied in one place: a pure function in the core beside the brief visit rule, between finding an issue's stays and measuring its time in status. A later third treatment is a new case and a new input to that function.

This changes how a metric is measured, so the record changes in the same commit: the glossary and the owner decisions list.

Decided at the start of the run (2026-11-17, spec "Decisions made at the start of the implementation run"):

- D99: under `exclude`, the existing brief visit count is withheld by the recorded row's check, not the adjusted row's.
- A refusal is built in the ticket that first accepts the value it refuses. This ticket refuses `timeInStatus` as `exclude` with no status listed in `briefVisitThresholds`, and refuses any part it does not know, `cycleTime` included until ticket 03. Ticket 05 keeps the other refusals.
- Each ticket corrects the sentence of the method note that it makes untrue. Here that is the sentence saying no duration was excluded or corrected. The wording under `count` does not change; ticket 06 writes the full paragraph.

Spec: `../spec.md` (owner decisions D83, D84, D86, D89, D92, D93, D99; "Configuration"; "Deriving"; "The metric views"; "The report", under the heading and marks; user stories 1, 3, 4, 8, 11 to 16, 22, 23, 37, 38 and 39).

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] A configuration file with `briefVisitTreatment` holding `timeInStatus` as `count` or `exclude` loads; a file without the setting, or without the part, loads and means `count`
- [ ] Under `exclude`, an issue whose only time in a listed status is a brief visit is not in that status's row for the sprint
- [ ] Under `exclude`, an issue with a brief visit and a longer stay in the same status stays in the row, with the brief visit's time left out
- [ ] The row's issue count, median and total are of what remains, and the baseline median is adjusted in the same way
- [ ] A status whose every issue was left out has no row for that sprint; a baseline sprint with no row for a status does not withhold it
- [ ] A status that is not listed is untouched, and so is a stay bounded by a changelog anomaly
- [ ] The treatment applies to every sprint, the baseline sprints included
- [ ] The span of a brief visit is removed before the intervals are clipped to the sprint window and to membership; a stay of no length has no time to remove
- [ ] The treatment function has its own tests: a brief stay removed, a brief stay beside a long one, a stay of no length, an open stay, an anomaly-bounded stay, a status not listed, and `count` returning the intervals unchanged
- [ ] The team of a sprint does not change, whatever was left out
- [ ] `timeInStatus` as `exclude` with no status listed in `briefVisitThresholds` is refused, with a message that names the file and the setting
- [ ] A part other than `timeInStatus` is refused, `cycleTime` included, and the message repeats nothing written in the file
- [ ] The existing brief visit count is still of the issues in the row before anything was left out (D93), and `brief_visits_per_sprint` still has a row for a listed status whose every issue was left out
- [ ] Under `exclude`, the brief visit count is withheld by the recorded row's check and its own, so it can be shown beside an adjusted row that reads `suppressed` (D99)
- [ ] Under `count`, `brief_visits_per_sprint` returns what it returns today, to the byte
- [ ] Under `exclude`, the method note no longer says that no duration was excluded or corrected; under `count` its wording is unchanged
- [ ] A report written under `exclude` has one line under its heading that names time in status, and a mark on the row of every listed status whether or not anything was left out in the sprint
- [ ] After the treatment changes, `report` and `query` refuse and say to run `derive`; no `backfill` is needed
- [ ] The raw tables are identical before and after a derive under `exclude`
- [ ] Two derives of the same raw data and settings give the same figures
- [ ] With the part as `count`, or the setting absent, the report and every view's results are identical to those of ticket 01
- [ ] The anomaly count and the console output of `derive` and `run` are unchanged
- [ ] The glossary has a "Treatment" entry and a sentence in "Time in status" saying what `exclude` leaves out; the owner decisions list has D83, D84, D86, D89, D92, D93 and D99
- [ ] The commit message gives the reason and says the change was made on 2026-11-16, after results had been seen
- [ ] Every existing test passes unchanged
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
