# Spec: Brief status visits in the data quality footer

Status: ready-for-agent

Terms in **bold** on first use are defined in `GLOSSARY.md`, except **brief visit**, which this spec introduces and the implementation adds to the glossary. Background is in `docs/context/status-duration-quality-handoff.md`. Read it first.

This repository is public. Every fixture is synthetic. No real sprint ID, status ID, report value or configuration content goes into a commit, a test or a ticket.

## Problem Statement

The first run against the real Jira showed issues that passed through a work status in seconds. The owner's explanation is that people forgot to move the issue when the work started, then clicked it through several statuses at once to catch up. The tool computes the recorded history correctly, but for those issues the recorded history is not what happened.

Such a visit still counts toward **time in status**: the issue is in the status's count and its few seconds pull the median toward zero. The owner cannot tell from a report how many of a status's issues are affected, so cannot judge how far to trust that row, or whether the problem is the same size in the **baseline** sprints as in the current one.

## Solution

The owner lists, in the configuration file, the statuses to check and the shortest believable stay in each. Each sprint report then says, in its data quality footer, how many of the issues behind each listed status's time in status row had a visit shorter than that.

Nothing else changes. No duration is excluded, replaced or relabelled, and every existing figure stays as it is. This is a measurement of the problem's size, made before any decision about repairing it.

## User Stories

### Configuring

1. As the project owner, I want to list statuses and a threshold for each in the configuration file, so that I choose which statuses are checked without the tool assuming what each status means.
2. As the project owner, I want each status to carry its own threshold, so that a status where a short stay is normal can have a lower threshold than one where it is not.
3. As the project owner, I want the setting to be optional, so that a configuration file written before this feature still loads and its reports are unchanged.
4. As the project owner, I want a status I did not list to be left unchecked, so that an automated or legitimately short status is not reported as a problem.
5. As the project owner, I want the file refused when the setting names a status that is not in `statusRoles`, so that a mistyped status ID is caught at load and not by a missing footer line.
6. As the project owner, I want the file refused when the setting names a status with the done role, so that I am not left waiting for a line that can never appear.
7. As the project owner, I want the file refused when a threshold is zero, negative or not a number, with a message that names the file and the setting, so that a bad value is caught at load.
8. As the project owner, I want `report` to say that `derive` is needed after I change a threshold, so that a report is never written from data derived under the old thresholds.
9. As the project owner, I want `discover` to leave the setting alone when it rewrites the file, so that a rerun does not lose my thresholds.
10. As the project owner, I want the README to document the setting's format, so that I can write it without reading the code.

### Reading the report

11. As the project owner, I want one footer line per listed status giving the number of issues with a brief visit, so that I can see which statuses are affected.
12. As the project owner, I want the line to name the status and state its threshold, so that the count can be read without opening the configuration file.
13. As the project owner, I want the lines in board order, so that they read in the same order as the time in status table.
14. As the project owner, I want the count to be of issues that are in that status's time in status row, so that I can compare it directly with the row's issue count.
15. As the project owner, I want an issue with several brief visits to one status counted once for that status, so that the count is never larger than the row's issue count.
16. As the project owner, I want an issue with brief visits to two listed statuses counted once under each, so that each line stands on its own.
17. As the project owner, I want a stay cut short only by the sprint's start or end not to count, so that the count reflects how long the issue really stayed and not where the sprint boundary fell.
18. As the project owner, I want a stay cut short only by the issue joining or leaving the sprint not to count, for the same reason.
19. As the project owner, I want an issue that is still in the status not to count for that stay, so that work that has just started is not reported as a brief visit.
20. As the project owner, I want a stay with the same start and end time to count as brief, so that an instant pass-through is included.
21. As the project owner, I want a stay exactly as long as the threshold not to count, so that the rule has one clear boundary.
22. As the project owner, I want the threshold measured in clock time, so that a stay across a weekend or a holiday is not mistaken for a short one.
23. As the project owner, I want to see a baseline sprint's counts by running `report --sprint` on it, so that I can compare the size of the problem then and now.
24. As the project owner, I want the method note to say what a brief visit is and that no figure was changed because of it, so that a reader does not assume the medians were corrected.
25. As the project owner, I want the method note to say the rule was added after the first real run, so that nobody reads it as a criterion fixed before results existed.
26. As the project owner, I want a report with no setting to have no brief visit lines and no extra method note text, so that existing reports stay identical.

### Privacy

27. As a team member, I want a brief visit count withheld unless at least three distinct assignees are behind the counted issues, so that the line cannot point at one or two people's habits.
28. As a team member, I want the count withheld whenever the status's time in status row has its median withheld, so that the footer never says more about a small group than the table does.
29. As a team member, I want no brief visit lines at all for a sprint that fails the five-person check, so that the footer does not show which statuses a small team's work sat in.
30. As a team member, I want a count of zero shown the same way as a withheld count, so that the line cannot be used to tell "nobody" from "one or two people".
31. As a team member, I want a listed status with no time in status row in the sprint to have no line, as it has no row in the table.
32. As a team member, I want the `query` command to obey the same rules, so that the count cannot be read around the report.
33. As a team member, I want the stored brief visit data to hold nothing but issue ID, sprint ID and status ID, so that the store gains no new information about a person.

### Operating

34. As the project owner, I want `derive` to rebuild the brief visit data with everything else, so that there is no extra command.
35. As the project owner, I want the console output of `derive` and `run` unchanged by this feature, so that brief visits are not confused with changelog anomalies.
36. As the project owner, I want two derives of the same raw data and settings to give the same counts, so that a report stays reproducible.

## Implementation Decisions

### Owner decisions (2026-11-12)

These continue the numbering of the owner decisions list. The implementation adds them there, in a new "Data quality" section, and cites them by number.

- **D58.** The statuses to check and their thresholds are one optional configuration setting, `briefVisitThresholds`: a dictionary of status ID to threshold. A status not listed is not checked.
- **D59.** A threshold is elapsed clock time in minutes, written as a number. It is not business time.
- **D60.** A visit is one unbroken stay in a status, measured over the issue's whole history and not clipped to a sprint window or to sprint membership. A stay the issue is still in is not a visit for this rule. A visit is brief when it is shorter than the status's threshold.
- **D61.** The report shows, per sprint and per listed status, the number of issues in that status's time in status row that have at least one brief visit. It is shown in the data quality footer. No existing figure changes.
- **D62.** The count is withheld when the row's median is withheld, and also when fewer than three distinct assignees are behind the counted issues.
- **D63.** There is no baseline count. A baseline sprint's counts are seen by reporting on that sprint.

### Readings this spec adds to the decisions

Each is a narrowing the decisions' words do not settle. They are put to the owner with the spec; an implementer who finds one does not fit treats it as a decision needed, not as theirs to settle.

- **Which visits count toward a sprint.** An issue is counted for a sprint and a status when it is in that status's time in status row for the sprint and one of its brief visits to that status overlaps the time the row counts (inside the sprint window, while a member). A brief visit made in an earlier sprint does not flag the issue in a later one.
- **Zero.** A count of zero is withheld like a count with too few assignees behind it, as the existing data quality counts are.
- **Fractions.** A threshold may be a fraction of a minute (`0.5`). It must be greater than zero.
- **Done statuses.** A done-role status cannot be listed, because done statuses have no time in status row.

### Configuration

- `briefVisitThresholds` sits at the top level of the configuration file beside `statusRoles`. Example, with synthetic IDs:

  ```jsonc
  // Status ID to the shortest believable stay, in minutes. Optional.
  "briefVisitThresholds": {
    "10001": 5,
    "10005": 2
  }
  ```

- Refused at load, with a message that names the file and the setting and never quotes a status ID (as D43 for status roles): a key that is not a status in `statusRoles`; a key whose role is done; a value that is not a number greater than zero. A quoted number is refused by the JSON reader as other wrong types are.
- An absent setting and an empty dictionary mean the same: nothing is checked.
- The setting is a derive setting. It is part of what `report` compares between the derived data and the current file, so a changed threshold makes `report` refuse and ask for `derive`, as a changed holiday does. It is not a collection setting: changing it never needs a `backfill`.
- `discover` never writes or removes the setting. A rerun keeps it as it keeps other owner-edited settings.

### Deriving

- Brief visits are found from the status intervals the derive already builds for each issue: an interval with an end, in a listed status, whose length in clock time is less than the threshold. Length is the difference between the two recorded instants.
- A new derived table records, per issue, sprint and status, that the issue has a brief visit counted for that sprint (the overlap rule above). It holds the three IDs and nothing else: no duration, no timestamp, no count of visits.
- The core rule (is this interval brief, does it overlap the counted time) is a pure function beside the existing interval code, so it can be tested without a store.
- Brief visits are not changelog anomalies. Nothing is written to the anomaly table, the anomaly count in the footer does not change, and `derive` and `run` log nothing new.
- The derived table is dropped and rebuilt by `derive` with the others.

### The metric view

- A new metric view, `brief_visits_per_sprint`, gives one row per sprint and listed status that has a time in status row: sprint ID, status ID, status name, board position, the threshold in minutes, the count, and a suppressed flag.
- Privacy is in the view, as for every other figure, so `report` and `query` read the same thing:
  - A sprint that fails the team check has no rows in this view.
  - The count is null and suppressed is 1 when the status's time in status row is suppressed, when fewer than three distinct assignees are among the counted issues, or when the count is zero. An unassigned issue counts toward nobody.
  - The delivered-count check (D49) is not read, as the time in status view does not read it (D55): the count is of issues that spent time in a status, delivered or not, and is never larger than the row's issue count, which is already shown.
- The view is added to the views `query` may read and to the list in the README.

### The report

- The data quality footer gains one line per row of the view, in board order, after the existing lines. A shown count reads along the lines of "Development: 4 issues had a visit shorter than 5 minutes." A withheld one reads "Development: issues with a visit shorter than 5 minutes: suppressed." Exact wording is the implementer's, within the report's existing style, and is fixed by tests.
- The method note gains a short paragraph, printed only when at least one status is listed: what a brief visit is (D60), that it is measured in clock time, that it is counted and nothing is excluded or corrected, that a zero and a withheld count both read `suppressed`, and that the rule was added on 2026-11-12 after the first run on real data.
- With no status listed the report is byte for byte what it is today, apart from the as-of time.

### Documents changed with the code

- The glossary: a "Brief visit" entry (the definition only, no implementation detail) and one sentence in "Minimum group size" covering the count. The commit message gives the reason and says the rule was set after results had been seen.
- The owner decisions list: D58 to D63.
- The README: the setting in the example and in the "Setting formats" table, the footer line in "What a report shows", the new view in the `query` section, and `derive` listed as needed after a threshold change.
- The project handoff document: one dated line pointing at this spec.

## Testing Decisions

A good test states a rule from this spec in terms of Jira history and configuration going in and a report line or view row coming out. It does not assert on table layouts, SQL text or class structure.

Both seams exist already, and the owner confirmed on 2026-11-12 that they are the seams to use. No new seam is needed.

1. **Pipeline seam (primary).** The real `derive`, `report`, `run` and `query` commands against the fake Jira, a temporary SQLite file and a fixed clock. Prior art: the existing pipeline tests for the time in status table, suppression, the method note and footer, the `query` command, and refused configuration files. It must include:
   - a catch-up sequence (not started, then two work statuses seconds apart) giving a count under each listed status;
   - a stay just under, exactly at and just over the threshold;
   - a zero-length stay;
   - an issue with two brief visits to one status counted once;
   - an issue still in the status, not counted;
   - a long stay cut to seconds by the sprint window, and one cut by membership, not counted;
   - a brief visit entirely over a weekend, so the issue has no business time in the status from it: it counts only if the issue is in the row through other time, and the overlap rule decides;
   - a brief visit in an earlier sprint and a long stay in the reported sprint, not counted for the reported sprint;
   - a status that is not listed, with a brief visit, giving no line;
   - different thresholds for two statuses;
   - suppression: two assignees behind the counted issues; a row whose median is suppressed; a sprint of four assignees giving no lines; a count of zero; each asserted in the report and by `query` against the view;
   - a search for a way round: no query over the metric views tells a zero count from a withheld one, or recovers a withheld count by subtraction from shown figures;
   - no setting and an empty dictionary: the report equals the report from before this feature;
   - a changed threshold: `report` refuses until `derive` runs;
   - each refused configuration, with a message that names the file and setting and quotes no status ID;
   - a `discover` rerun that keeps the setting;
   - the anomaly count and the console output unchanged when brief visits exist;
   - expected multi-line strings normalized to LF, as the Windows run established.
2. **Core seam.** Pure-function tests for the brief-interval rule and the overlap rule, following the existing interval tests: boundaries, open intervals, zero length, daylight-saving changes inside a stay (clock time is the difference of instants, so a shift does not change the length).

All tests use TUnit. All fixtures are synthetic.

Mutation records, where the build run uses them: rerun only the records a change affects.

## Out of Scope

- Excluding brief visits from any median, total or count, and any "quality-qualified" second figure.
- Replacing or estimating a duration from another source, and reading Committed time as Development time.
- A baseline brief visit count or column.
- Detecting rapid multi-status sequences as one event; each status is checked alone.
- Changing how days are displayed (the `0.0` for a short positive duration).
- Cycle time: whether a late first move into a work status understates it is not measured here.
- Review timing from GitHub or Azure DevOps pull request events.
- The other team's comparison table of per-stage medians.
- Any change to the evaluation's success criteria.

## Further Notes

- The status-duration handoff cites a commit hash for the total business days change that is not in the repository; the change is on `main` under another hash.
- The thresholds the owner chooses are company configuration and stay on the work laptop. The values in this spec and in tests are invented.
- Choosing thresholds after looking at the data is a revision made with knowledge of results. The method note and the glossary commit say so; the counts are a description of data quality and are not a success criterion.
- If the counts turn out large, the next decision (annotate only, or exclude with a separately labelled figure and its own contributor check) is a new spec. The privacy rules for that are set out in the handoff under "Privacy must follow the contributors to each figure".
