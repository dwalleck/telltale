# Spec: Leaving brief visits out of time in status and cycle time

Status: ready-for-agent

Every reading is confirmed by the owner (2026-11-17). Nothing is waiting.

Terms in **bold** on first use are defined in `GLOSSARY.md`, except **treatment**, which this spec introduces and the implementation adds to the glossary. Read `.scratch/brief-status-visits/spec.md` first: this spec builds on it and does not repeat it. Background is in `docs/context/status-duration-quality-handoff.md`.

This repository is public. Every fixture is synthetic. No real sprint ID, status ID, report value or configuration content goes into a commit, a test or a ticket.

## Problem Statement

The tool counts **brief visits** and changes nothing because of them. That showed the size of the problem. It did not make the figures usable: a status whose issues were clicked through in seconds still has a median near zero, and the owner cannot report that median as the time work spends there.

**Cycle time** has the same fault from the same habit. A cycle starts at the first move into a work status. When that move is a catch-up click made after the work was done, the cycle is too short by an unknown amount. Cycle time by point bucket is a **headline metric**.

There are three things to do with a brief visit: count it and leave the figures alone, leave it out of the figures, or replace it with a length taken from another source. The owner wants to choose between them without a code change, and to choose separately for time in status and for cycle time.

## Solution

One new optional setting, `briefVisitTreatment`, says what is done with brief visits. It has two parts, one for **time in status** and one for cycle time. Each is `count` (what the tool does today) or `exclude`.

- With `exclude` for time in status, the time of every brief visit is left out. An issue with no other time in the status leaves that status's row.
- With `exclude` for cycle time, a delivered issue whose cycle starts with a brief visit has no cycle time. It is still delivered.

The report shows the adjusted figure only. It says that figures were adjusted in a line under its heading, marks the tables affected, and says in the footer how many issues were left out and why.

The third choice, replacing a brief visit with a length from another source, is not built. It needs pull request or git events, which the tool does not collect yet. The code is shaped so that it can be added as a third value.

## User Stories

### Configuring

1. As the project owner, I want one setting that says what is done with brief visits, so that I can change the treatment without a code change.
2. As the project owner, I want to set the treatment for time in status and for cycle time separately, so that I can leave brief visits out of one and keep them in the other.
3. As the project owner, I want the setting to be optional and to mean `count` when absent, so that a configuration file written before this feature gives the reports it gave before.
4. As the project owner, I want one part to be optional when I write the other, so that I only write the part I am changing.
5. As the project owner, I want the file refused when a treatment is not `count` or `exclude`, so that a mistyped value is caught at load.
6. As the project owner, I want the file refused when a part is `exclude` and no status is listed in `briefVisitThresholds`, so that I am not left believing figures were adjusted when nothing could be.
7. As the project owner, I want the file refused when cycle time is `exclude` and no listed status has the work role, for the same reason.
8. As the project owner, I want `report` to say that `derive` is needed after I change the treatment, so that a report is never written from data derived under another treatment.
9. As the project owner, I want `discover` to leave the setting alone when it rewrites the file.
10. As the project owner, I want the README to document the setting and what each value does to each figure.

### Time in status

11. As the project owner, I want the time of a brief visit left out of the issue's time in that status, so that a few seconds do not stand for the work.
12. As the project owner, I want an issue whose only time in a status was brief visits left out of that status's row, so that the count and the median describe issues with a believable stay.
13. As the project owner, I want an issue with a brief visit and a longer stay in the same status to stay in the row with its longer stay, so that real time is not thrown away.
14. As the project owner, I want the total and the baseline median adjusted in the same way as the median, so that the row's figures agree with each other and with the baseline.
15. As the project owner, I want a status I did not list to be untouched, so that only the statuses I chose are adjusted.
16. As the project owner, I want the same treatment applied to every sprint, the baseline sprints included, so that a comparison is never between figures measured two ways.

### Cycle time

17. As the project owner, I want a delivered issue whose cycle starts with a brief visit to have no cycle time, so that a cycle known to be too short does not pull the median and the P85 down.
18. As the project owner, I want that issue to stay in the delivered count and in every figure that is not a duration, so that leaving out its cycle time does not hide that it was delivered.
19. As the project owner, I want a brief visit later in the cycle to change nothing, because a cycle is measured from its start to delivery and not as a sum of stays.
20. As the project owner, I want the baseline cycle times adjusted in the same way.
21. As the project owner, I want to know how many delivered issues had a cycle starting with a brief visit whichever treatment is in force, so that I can see the size of the problem before I choose to exclude.

### Reading the report

22. As a reader, I want a line under the report's heading when any figure was adjusted, so that I know before I read a number.
23. As a reader, I want each adjusted table marked, so that I can tell which figures the adjustment touched.
24. As a reader, I want the footer to say, per listed status, how many issues were left out of the row, so that I can judge how much the row changed.
25. As a reader, I want the footer to say how many delivered issues have no cycle time because their cycle started with a brief visit.
26. As a reader, I want the method note to say why brief visits were left out, so that I do not take it for trimming of inconvenient results.
27. As a reader, I want the method note to say what leaving them out does not do, so that I do not take the adjusted figure for a corrected one.
28. As a reader, I want the method note to say the rule was added after results had been seen, and when.
29. As the project owner, I want a report under `count` for both parts to differ from today's only by the one new cycle time line in the footer.

### Privacy

30. As a team member, I want an adjusted median and total withheld unless enough distinct assignees are behind the issues that remain in the row, so that leaving issues out cannot make a figure rest on one or two people.
31. As a team member, I want an adjusted cycle time figure withheld by the same rule, counted over the issues that still have a cycle time.
32. As a team member, I want a baseline figure withheld when any baseline sprint's adjusted row is withheld, so that pooling cannot give back a withheld figure.
33. As a team member, I want each count of issues left out withheld unless enough distinct assignees are behind the issues it counts, and a count of zero shown the same way as a withheld one.
34. As a team member, I want the marks on the tables not to depend on whether anything was left out, so that a mark cannot tell a zero from a withheld count.
35. As a team member, I want the `query` command to obey the same rules.
36. As a team member, I want the derived data to gain nothing about a person: issue, sprint and status IDs, durations and flags only.

### Operating

37. As the project owner, I want the raw history untouched by any treatment, so that I can change my mind and derive again.
38. As the project owner, I want two derives of the same raw data and settings to give the same figures.
39. As the project owner, I want the console output of `derive` and `run` unchanged.

## Implementation Decisions

### Owner decisions (2026-11-16)

These continue the numbering of the owner decisions list. The implementation adds them to its "Data quality" section and cites them by number.

- **D83.** What is done with brief visits is one optional configuration setting, `briefVisitTreatment`, with two parts: `timeInStatus` and `cycleTime`. Each is `count` or `exclude`. An absent setting or an absent part means `count`. A treatment applies to every listed status and to every sprint, the baseline sprints included.
- **D84.** Under `exclude` for time in status, the time of every brief visit is left out of the issue's time in that status. An issue left with no business time in the status is not in the status's row. The row's count, median and total, and the baseline median, are of what remains.
- **D85.** Under `exclude` for cycle time, a delivered issue whose cycle starts with a brief visit has no cycle time. It is still delivered: the delivered count, seat value and the reopen count do not change.
- **D86.** The report shows the adjusted figure and not the recorded one beside it. It carries a line under its heading, a mark on each adjusted table, and footer lines that give how many issues were left out.
- **D87.** The number of delivered issues whose cycle starts with a brief visit is shown in the footer under either treatment.
- **D88.** Replacing a brief visit with a length from another source is not built. It is a third treatment for later, when pull request or git events are collected. Time in an earlier status is never relabelled as time in a later one.
- **D89.** This is a change to how two metrics are measured, made on 2026-11-16 after results had been seen. The glossary commit and the method note say so.

### Readings confirmed by the owner (2026-11-17)

Each narrows something the decisions' words do not settle. The owner confirmed the first six as written and decided the seventh, and they continue the numbering as D90 to D96. An implementer who finds one does not fit the code treats it as a decision needed.

1. **D90. Which stay starts a cycle.** A cycle starts with a brief visit when the stay that begins at the cycle's start is a brief visit under its status's threshold. The cycle's start is what it is today: the first entry into a work-role status, or the issue's creation when it was created in one. A stay bounded by a changelog anomaly is never a brief visit (D75, D81), so such a cycle is kept.
2. **D91. A mis-click is treated as a catch-up.** An issue moved into a work status and straight back out, with its real start days later, also has a cycle that starts with a brief visit, and also has no cycle time under `exclude`. Moving its start to the next work status entry would be more exact and is left out of this spec. The owner confirmed this after seeing that the shape is rare in the store.
3. **D92. One treatment for all listed statuses.** There is no per-status treatment. A status the owner does not want adjusted is one the owner does not list, which also removes its count.
4. **D93. The count of brief visits is measured against the recorded row.** Under `exclude`, the existing count (D61) is of issues that had business time in the status during the sprint before anything was left out. Otherwise the issues left out would vanish from the count that explains them.
5. **D94. A cycle time count that can be bounded.** Under `exclude`, the delivered count minus the sum of the bucket counts is the number of delivered issues with no cycle time: those that never entered a work status plus those left out. When no issue of the first kind exists, that gives the second number even when the footer withholds it. This is accepted and recorded, as D78 is. Today the same subtraction already gives the first number.
6. **D95. Two derives can be compared.** A figure under `count` and the same figure under `exclude` can be set side by side by deriving twice, which says something about the issues left out even when their count is withheld. This is accepted and recorded, as D79 is: whoever edits the configuration file is trusted with it.
7. **D96. Which treatment the evaluation is judged under** is the owner's to choose and is written in `docs/context/pilot-plan.md`, with its date and reason. This spec does not choose it. The owner chose on 2026-11-17, before the setting was built: `exclude` for both parts. The pilot plan's "Brief visits" section is the record.

### Decisions made at the start of the implementation run (2026-11-17)

The exploration before ticket 01 found places where this spec did not fit the code. The owner decided each one. Where one differs from a sentence elsewhere in this spec, the decision stands.

- **D97.** The number of delivered issues whose cycle starts with a brief visit can be combined with shown brief visit counts to say something about a withheld one: the issues it counts are often among those the per-status counts count, and the subtraction then gives the withheld count or a bound on it. This is accepted and recorded, as D78 and D94 are. The number is not withheld for it. The probe that proves a zero cannot be told from a withheld count pins this as shown.
- **D98.** (Widened on 2026-11-17, while ticket 04 was built and again in its review.) Under `exclude`, shown figures can give a withheld number of a listed status, or a bound on it. A footer line with no row in the time in status table shows that the status's left-out number and its brief visit count are each at least one, even when they read `suppressed`. A shown brief visit count larger than the row's issue count shows that the left-out number is at least their difference, and a brief visit count shown beside a row that reads `suppressed` shows that it is at least one. A withheld left-out number is also less than a shown brief visit count, so with one issue in the row it is the number itself. This is accepted and recorded, as D78, D94 and D97 are. The line stays, so that a status does not leave the report unexplained, and no count is withheld for it.
- **D99.** Under `exclude`, the brief visit count of D62 is withheld when the recorded row's median would be withheld, not the adjusted row's: the count is measured against the recorded row (D93). The count's own check of the assignees behind it still applies. A count can therefore be shown beside a row that reads `suppressed`.
- **D100.** The footer's cycle time line comes directly after the brief visit lines.
- **D101.** Under `exclude` for cycle time, the note under the cycle time table is printed whatever the table holds, also when it is the single word `suppressed` or says no issue has a cycle time.
- **D102.** (Decided on 2026-11-17 in the review of ticket 03.) Under `exclude` for cycle time, a delivered issue whose cycle starts with a brief visit keeps the point bucket it has under `count`: its estimate at the start of its cycle (ADR-0006), not at delivery. It has no cycle time, but it did enter a work status, so its start is what it was (D90), and the seat value inputs do not change with the treatment (D85). The sentence "stored as delivered with no cycle time, as an issue that never entered a work status is" is read as being about the cycle time only.
- **D103.** (Decided on 2026-11-17, from the cross-ticket review of tickets 01 to 03.) A metric view gives the treatment the derived data was built under, one value for time in status and one for cycle time, so that a figure read through `query` can be told from the same figure under the other treatment.
- **D106.** (Decided on 2026-11-17, in the review of ticket 04.) The brief visit count of a status less its left-out number is the number of issues that had a brief visit and are still in the row, and it can rest on fewer assignees than a figure needs. The tool neither defines nor withholds that number. This is accepted and recorded, as D78 is.

Two changes of ticket scope were decided with them and are in the tickets: a refusal is built in the ticket that first accepts the value it refuses, and each ticket corrects the sentence of the method note that it makes untrue.

### Configuration

- `briefVisitTreatment` sits at the top level of the configuration file beside `briefVisitThresholds`:

  ```jsonc
  // What is done with brief visits. Optional. Each part is "count" or "exclude".
  "briefVisitTreatment": {
    "timeInStatus": "exclude",
    "cycleTime": "count"
  }
  ```

- Refused at load, with a message that names the file and the setting: the setting written as anything but an object; a part other than `timeInStatus` and `cycleTime`; a value other than `count` or `exclude`; either part `exclude` with no status listed in `briefVisitThresholds`; `cycleTime` as `exclude` with no listed status that has the work role.
- It is a derive setting, as the thresholds are. `derive` records it with the run, `report` compares it with the current file and refuses when they differ, and changing it never needs a `backfill`.
- `discover` never writes or removes it. On a rerun it is checked where the thresholds are checked (D73), and nothing is written if it is refused.

### Deriving

- The treatment is applied in one place, between finding the issue's stays and measuring its time in status. It is a pure function in the core beside the brief visit rule: stays, thresholds and treatment in, the status intervals to measure out. It can be tested without a store.
- The treatment is a closed set of cases, written as a union type. A later third treatment is a new case and a new input to that function; no caller downstream of it changes.
- Under `exclude` for time in status, the span of each brief visit to a listed status is removed from the issue's status intervals before they are clipped to the sprint window and to membership. A stay of no length has no time to remove.
- Under `exclude` for cycle time, the issue is stored as delivered with no cycle time, as an issue that never entered a work status is.
- The derived data keeps what the footer needs to count, under either treatment: which issues were in a status's row before anything was left out, and which delivered issues have a cycle that starts with a brief visit. The layout is the implementer's. It holds IDs, durations and flags and nothing else.
- Brief visits are still not changelog anomalies. The anomaly table, the anomaly count and the console output do not change.

### The metric views

- `time_in_status_per_sprint`, `baseline_time_in_status`, `cycle_time_per_sprint_and_bucket` and `baseline_cycle_time_per_bucket` keep their columns and their rules. They read the adjusted data, so each existing check counts the assignees of the issues that remain: the row's median and total, the bucket's figures, and the baseline's rule that one withheld sprint row withholds the pooled figure.
- A status whose every issue was left out has no row for that sprint, as a status nobody spent time in has none. A baseline sprint with no row for a status does not withhold it, as today.
- The team of a sprint does not change: it is counted from every issue that was a member or was delivered, whatever was left out.
- `brief_visits_per_sprint` has one row per sprint and listed status that had a row before anything was left out, under either treatment. It gains the number of issues left out of the row. That number is null under `count`. Under `exclude` it is withheld when fewer distinct assignees than a figure needs are behind the issues it counts, or when it is zero, as the existing count is.
- A new metric view gives, per sprint, the number of delivered issues whose cycle starts with a brief visit, and a suppressed flag. It has a row only when a listed status has the work role. The number is withheld when the sprint's delivered count is withheld, when too few assignees are behind the issues it counts, or when it is zero.
- Both are among the views `query` may read.

### The report

Exact wording is the implementer's, within the report's existing style, and is fixed by tests.

- **Under the heading.** A report written with either part as `exclude` says so in one line, as a report with a lowered minimum group size does (D67). It names which of the two metrics were adjusted.
- **Marks.** With `exclude` for time in status, the row of every listed status carries a mark that points to the footer. With `exclude` for cycle time, the cycle time table carries one note under it. Both are there whether or not anything was left out in this sprint.
- **Footer, per listed status.** The existing line stays. Under `exclude` it goes on to say that those visits are left out of the row, and how many of the issues had no other time in the status and so are not in the row's count. A withheld number reads `suppressed`.
- **Footer, cycle time.** One line: how many delivered issues had a cycle starting with a brief visit, and whether they are in the cycle time figures (`count`) or have no cycle time (`exclude`).
- **Method note.** One paragraph, printed when at least one status is listed, in place of today's sentence that nothing is excluded. It states:
  - the treatment in force for each of the two metrics;
  - what was left out, in the words of D84 and D85;
  - why: a stay shorter than the threshold is too short to be the work, and its usual cause is an issue moved through several statuses at once after the work was done, so its recorded length says when the board was updated and not how long the work took;
  - what leaving out does not do: the time the work really took is still recorded under the earlier status, the figures describe the issues that were moved on time, which may not be typical, and no length was estimated or replaced;
  - that the rule was added on 2026-11-16, after results had been seen;
  - that a zero and a withheld count both read `suppressed`.
- With both parts as `count`, the report is what it is today plus the cycle time footer line and the method note's sentence about it. With no status listed it is byte for byte what it is today, apart from the as-of time.

### Documents changed with the code

- The glossary: a "Treatment" entry; the last sentence of "Brief visit"; a sentence each in "Time in status" and "Cycle time" saying what `exclude` leaves out; a sentence in "Minimum group size" covering the two new counts. The commit message gives the reason and says the change was made after results had been seen.
- The owner decisions list: D83 to D89, and the confirmed readings as further decisions.
- The README: the setting in the example and in the "Setting formats" table, the new lines in "What a report shows", the new view and column in the `query` section, `derive` listed as needed after a treatment change, and the note on comparing two derives beside the existing one (D79).
- The project handoff document: one dated line pointing at this spec.

## Testing Decisions

A good test states a rule from this spec in terms of Jira history and configuration going in and a report line or view row coming out. It does not assert on table layouts, SQL text or class structure.

The two seams of the brief status visits spec are the seams to use. No new seam is needed.

1. **Pipeline seam (primary).** The real `derive`, `report`, `run` and `query` commands against the fake Jira, a temporary SQLite file and a fixed clock. It must include:
   - a catch-up sequence under each of the four combinations of the two parts, with the time in status row, the cycle time bucket and the footer asserted in each;
   - an issue whose only time in a listed status is a brief visit: out of the row, and counted as left out;
   - an issue with a brief visit and a longer stay in the same status: in the row with the longer stay, and not counted as left out;
   - a status with every issue left out: no row, and a footer line that still explains it;
   - a brief visit to a status that is not listed: nothing changes;
   - a cycle that starts with a brief visit, one with a brief visit only later, one whose first stay is bounded by a changelog anomaly, and one created in a work status and moved on within the threshold;
   - a move into a work status and straight back out, with a real start later;
   - the delivered count, seat value inputs and reopen count unchanged for an issue with no cycle time;
   - the baseline: a pooled median and a pooled cycle time figure adjusted, and a baseline withheld because one baseline sprint's adjusted row is;
   - suppression: a row that had three assignees and has two after leaving out; a bucket in the same position; a left-out count with two assignees behind it; a left-out count of zero; the cycle count when the delivered count is withheld; each asserted in the report and by `query`;
   - the marks present with nothing left out;
   - a search for a way round within one derive: no query over the metric views tells a zero from a withheld count, or recovers a withheld count, other than the subtraction recorded in reading 5;
   - both parts as `count`: the report equals today's plus the cycle time line; no status listed: the report equals today's;
   - a changed treatment: `report` refuses until `derive` runs;
   - each refused configuration, with a message that names the file and the setting;
   - a `discover` rerun that keeps the setting;
   - the anomaly count and the console output unchanged;
   - the raw tables identical before and after a derive under `exclude`.
2. **Core seam.** Pure-function tests for the treatment function: a brief stay removed, a brief stay beside a long one, a stay of no length, an open stay, an anomaly-bounded stay, a status not listed, `count` returning the intervals unchanged; and for whether a cycle starts with a brief visit.

All tests use TUnit. All fixtures are synthetic. Expected multi-line strings are normalized to LF.

## Out of Scope

- Replacing or estimating a duration from another source (the third treatment), and collecting pull request or git events.
- Showing the recorded figure beside the adjusted one.
- A treatment per status.
- Moving a cycle's start past a brief visit to the next work status entry.
- Excluding on anything other than a brief visit: no rule for rapid multi-status sequences, and no change to how changelog anomalies are handled.
- A baseline count of issues left out.
- Changing how days are displayed.
- Choosing which treatment the evaluation's success criteria are judged under.

## Further Notes

- Leaving issues out makes samples smaller. A point bucket's P85 can be its largest value in a small sample, and more rows and buckets will read `suppressed` than under `count`. Both follow from the rules and are not faults.
- If the catch-up habit is equally common in the baseline and the evaluation sprints, the understated cycles roughly cancel in a comparison under `count`. If structured AI use changes when people move issues, they do not. The cycle time footer line under `count`, read for a baseline sprint and a current one, shows which is the case.
- The time left out of a status under `exclude` is not added anywhere. The status the issue sat in while the work was done keeps that time, so its figures stay as long as they were.
- The thresholds and the treatment the owner chooses are company configuration and stay on the work laptop. The values in this spec and in tests are invented.
