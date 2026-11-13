# Spec: Configurable and optional small-group suppression

Status: ready-for-agent

Terms in **bold** on first use are defined in `GLOSSARY.md`. Decisions referenced as ADR-000N are in `docs/adr/`; decisions referenced as D-numbers are in `docs/decisions.md`.

This repository is public. Every fixture is synthetic. No real sprint ID, report value or configuration content goes into a commit, a test or a ticket.

## Problem Statement

Telltale withholds every figure that rests on too few people: the **minimum group size** rule of ADR-0003, built into the metric views and refined by owner decisions D5 to D56. On the owner's team of six this withholds a large share of each report: a sprint with fewer than five assignees shows nothing, a row with fewer than three assignees behind it shows no median, and one short baseline sprint withholds the whole **baseline**.

The owner states that the reasons for the rule no longer apply to their team, and that the team cannot afford to have figures withheld. The rule cannot be turned off today: its two numbers are fixed in the code.

Other teams that use the tool may still need the rule, so it must stay available and stay the default.

## Solution

One optional setting in the configuration file holds the rule's two numbers: the smallest team, and the smallest number of assignees behind one figure. When the setting is absent they are five and three and the rule applies exactly as it does today. An owner may lower or raise either number. Setting both to zero turns suppression off: every figure the rule withheld is shown, in the report and in `query`. A report written under numbers lower than the defaults says so plainly.

In this spec, "suppression off" means both numbers are zero.

Only the minimum group size rule becomes optional. Person fields are still hashed before storage, names, emails and issue keys are still not stored, and the tool still has no view or report grouped by person.

## User Stories

### Configuring

1. As the project owner, I want to turn small-group suppression off in the configuration file, so that my reports show every figure.
2. As someone setting the tool up for another team, I want the rule to apply with five and three when the setting is absent, so that a team is protected unless its owner chooses otherwise.
3. As someone setting the tool up, I want `discover` to write the setting with its default numbers in a new configuration file, so that I can see the rule exists and where to change it.
3a. As the owner of a deployment, I want to set the smallest team and the smallest number of assignees behind a figure separately, so that the rule fits the size of my team.
3b. As the owner of a deployment, I want to turn off one check and keep the other, by setting one number to zero.
3c. As the owner of a larger team, I want to raise either number above its default, so that the rule can be stricter than the default.
3d. As the owner of a deployment, I want the file refused when the assignees behind a figure exceed the team size, so that a pair of numbers that would withhold every figure is caught at load.
4. As the project owner, I want an existing configuration file without the setting to load and behave as before, so that upgrading the tool changes nothing until I edit the file.
5. As the project owner, I want `discover` to leave the setting as I set it on a rerun, so that rewriting the file never turns suppression back on or off.
6. As the project owner, I want the file refused when either number is negative, fractional, quoted or missing, with a message that names the file and the setting, so that a typo does not silently leave suppression in the wrong state.
7. As the project owner, I want `report` to say that `derive` is needed after I change the setting, so that no report mixes the two states.
8. As the project owner, I want changing the setting not to need a `backfill`, so that I do not refetch Jira to change how figures are shown.
9. As the project owner, I want the README to document the setting and what turning it off shows, so that the choice is made knowingly.

### Reports with suppression off

10. As the project owner, I want a sprint with fewer than five assignees to show its delivered count and both tables in full, so that a small sprint is reported like any other.
11. As the project owner, I want a sprint's delivered count shown whatever the number of assignees among its delivered issues, so that the headline figure is always present.
12. As the project owner, I want every point bucket row to show its median and P85, so that a bucket one or two people worked on is not blank.
13. As the project owner, I want every time in status row to show its median and total, for the same reason.
14. As the project owner, I want every baseline figure shown whatever the size of each baseline sprint, so that one small sprint no longer withholds the whole baseline.
15. As the project owner, I want no baseline sprint named as the cause of a withheld baseline, on the console or in the report, because nothing is withheld.
16. As the project owner, I want the data quality footer's counts shown, including a count of zero as `0`, so that "none" is told apart from "withheld".
17. As the project owner, I want issues with no assignee to contribute to shown figures, so that an unassigned issue does not blank a row.
18. As the project owner, I want the word `suppressed` to appear nowhere in a report's tables or footer when suppression is off, so that I can check the setting took effect.
19. As the project owner, I want figures that have no data (no issues in a bucket, a sprint with no window) to behave as they do today, so that turning suppression off changes only what the rule withheld.
20. As the project owner, I want reports already written to be left alone, so that the earlier record is kept; I regenerate the ones I want.

### Saying so

21. As a reader of a report, I want a line near the top when either number was below its default, giving the numbers in force, so that I know a figure may rest on fewer people than the default allows even if the report was forwarded without context.
22. As a reader of a report, I want the method note to state the numbers in force, and to say no figure was withheld for group size when both are zero, so that the note describes how this report was made.
23. As a reader of a report written with the default numbers, I want it to read exactly as it does today, so that nothing changes for teams that keep the rule.
23a. As a reader of a report written with numbers at or above the defaults, I want no line at the top, because the rule is at least as strict as the default.
24. As the project owner, I want `query --help` to say that the views withhold small-group figures only when suppression is on, so that the help text stays true.

### `query`

25. As the project owner, I want `query` to show the same figures the report shows when suppression is off, so that ad hoc questions get full answers.
26. As the project owner, I want each view to keep its columns, including `suppressed`, so that a saved query runs unchanged in either state.
27. As a team that keeps suppression on, I want `query` to withhold exactly what it withholds today, so that the guard is as strong as before.

### What does not change

28. As a team member, I want my Jira identity still hashed with the secret salt before storage, whatever the setting, so that the store cannot be used to look me up.
29. As a team member, I want names, emails, issue keys and free text still not stored, whatever the setting.
30. As a team member, I want the tool still to have no view or report grouped by person, whatever the setting, so that turning suppression off does not create a per-person report.
31. As a team that keeps suppression on, I want every existing suppression rule (D5 to D56) unchanged, so that the option costs us nothing.

### The record

32. As a future reader of the repository, I want an ADR that says suppression became optional, when, and why, so that ADR-0003 is not silently contradicted.
33. As a future reader, I want the glossary, the README, `CLAUDE.md` and the decisions list to describe the rule as a default that can be turned off, so that the documents agree with the code.
34. As a future reader, I want the commit that changes the glossary to give the reason and to say the change was made after results had been seen, so that the dated record stays honest.

## Implementation Decisions

### Proposed owner decisions

The owner asked for suppression to be optional and kept for others. The points below are this spec's recommended answers to what that leaves open. They continue the numbering in `docs/decisions.md` (D58 to D63 belong to the brief status visits spec) and are recorded there, in the "Suppression" section, once the owner has confirmed them. An implementer who finds one does not fit treats it as a decision needed.

- **D64.** The minimum group size is one optional configuration setting, `minimumGroupSize`, with two whole numbers: `team` and `assigneesBehindAFigure`. Absent means five and three. (Owner, 2026-11-12: the thresholds are configurable.)
- **D65.** A number of zero turns that check off. With both zero, no figure or count is withheld for the size of the group behind it. This covers every rule in the "Suppression" section of the decisions list and the data quality footer's counts.
- **D66.** Every existing rule applies with the configured numbers in place of five and three. The rules themselves (what is withheld when a check fails) do not change. The file is refused when `assigneesBehindAFigure` is greater than `team` and `team` is not zero.
- **D67.** Every report's method note states the numbers in force. A report written with either number below its default also says so in a line under its heading.
- **D68.** Hashing of person fields, the fields that are never stored, and the absence of any per-person view do not depend on the setting.
- **D69.** `discover` writes the setting with five and three into a new configuration file and never changes it afterwards.

### Scope of the setting

- The setting governs the minimum group size rule wherever it is applied: the team check per sprint, the three-assignee check per figure, the delivered-count check (D49, D54), the baseline rules (D44, D50, D56), and the footer's counts.
- It does not create anything. No new view, column or report section appears when it is off. The consented exceptions of ADR-0003 (the two case studies, the volunteer cohort) are not built yet and are not touched here; a later spec that adds a result grouped by people decides how this setting applies to it.
- ADR-0005 is not affected.

### Configuration

- `minimumGroupSize` sits at the top level of the configuration file. Example, turning suppression off:

  ```jsonc
  // Withhold figures that rest on too few people (GLOSSARY: minimum group size).
  // team: the fewest assignees a sprint needs for anything to be shown.
  // assigneesBehindAFigure: the fewest assignees behind one figure.
  // Optional; absent means 5 and 3. Zero turns a check off.
  "minimumGroupSize": {
    "team": 0,
    "assigneesBehindAFigure": 0
  }
  ```

- When the setting is present, both numbers are required. Refused at load, with a message naming the file and the setting: a missing number; a negative or fractional number; a quoted number; `assigneesBehindAFigure` greater than `team` when `team` is not zero.
- A number of one is allowed and is not the same as zero: with `assigneesBehindAFigure` at one, a figure with only unassigned issues behind it, and a footer count of zero, are still withheld.
- The setting is a derive setting. It is part of what `report` compares between the derived data and the current file, so changing it makes `report` refuse and ask for `derive`. It is not a collection setting.
- `discover` writes it with five and three when it creates the file, and keeps whatever the file says on a rerun, including its absence.

### The views

- The rule's two numbers are already read from one place by every metric view (`minimum_group_size`). The setting acts there: `derive` builds that one row from the configured numbers, and no view states a number of its own. With both at zero every existing condition passes, so no view needs a second code path. If a view turns out to withhold something by another route when a minimum is zero, or to compare against a literal five or three, that is fixed in the view, and a test names the case.
- With other numbers, each view behaves as it does today with those numbers in place of five and three.
- With suppression off:
  - `sprint_team` reports no sprint as suppressed, for the team or for the delivered count;
  - `delivered_per_sprint`, `cycle_time_per_sprint_and_bucket` and `time_in_status_per_sprint` have their ordinary rows for every sprint, with every figure filled in, and never the single "suppressed" row;
  - `baseline_sprint` reports no baseline sprint as suppressed, and the two baseline views show every pooled figure;
  - `data_quality` shows both counts, a count of nothing as `0`.
- Every view keeps its columns. `suppressed` and its like are `0` in every row when suppression is off.
- The views remain the only place the rule is applied. `report` and `query` gain no knowledge of the setting beyond the wording below.
- With suppression on, every view returns exactly what it returns today.

### The report and the console

- With suppression off, the report has one line directly under its heading, along the lines of: "Small-group suppression is off: a figure in this report may rest on the work of one or two people." With either number below its default but not both zero, the line gives the numbers in force instead. With both numbers at or above the defaults there is no line. Exact wording is the implementer's, within the report's style, and is fixed by tests.
- The method note states the numbers in force wherever it states five and three today. With the default numbers its text is unchanged.
- With suppression off, the method note's minimum group size paragraph, including the sentences about bots counting as people (D42) and about a late status's count approximating a withheld count (D55), is replaced by a statement that no figure was withheld for group size and that person fields are still hashed.
- With suppression off, the console and the report never name a baseline sprint as withholding the baseline (D50, D56), because none does.
- With the default numbers, the report and the console are byte for byte what they are today, apart from the as-of time.
- `query --help` says the metric views withhold small-group figures according to `minimumGroupSize`, and keeps its sentence that opening the database file elsewhere bypasses the views.

### The brief status visits spec

The two specs are independent and either can be built first. Whichever is built second covers the other: with suppression off, `brief_visits_per_sprint` has rows for every sprint, shows every count, and shows a count of zero as `0`.

### Documents changed with the code

- **A new ADR** (the next free number) that amends ADR-0003: the minimum group size's numbers are defaults that the owner of a deployment may change or set to zero; pseudonymization at import and the absence of per-person reports are unchanged. It records the date, the alternatives considered (an on/off switch with fixed numbers; removing the rule), and the owner's reason as given under Further Notes. ADR-0003 gains a first line pointing to it and is otherwise left as written.
- **`CLAUDE.md`**: the non-negotiable was reworded on 2026-11-12, ahead of the code, at the owner's direction: it was written as if for a research study. The implementation checks that the wording matches what is built and removes the "being made configurable" phrasing once it is.
- **`GLOSSARY.md`**: "Minimum group size" says five and three are the defaults and that a deployment's configuration may set other numbers, zero turning a check off. The definition is otherwise unchanged. The commit gives the reason and says the change was made on 2026-11-12, after the first run on real data.
- **`docs/decisions.md`**: D64 to D69, and a sentence at the head of the "Suppression" section that those decisions describe the rule with its default numbers, and apply with whatever numbers are configured.
- **`README.md`**: the "Privacy rules" section says small groups are withheld by default, how to change the numbers or turn the rule off, and what a report then shows; the setting goes in the configuration example and the "Setting formats" table; `derive` is listed as needed after changing it.
- **`docs/context/handoff.md`**: one dated line pointing at this spec.

## Testing Decisions

A good test states a rule in terms of Jira history and configuration going in and a report line or view row coming out. It does not assert on SQL text, table layouts or class structure.

Both seams exist. No new seam is needed. Nearly all of this is the pipeline seam.

1. **Pipeline seam (primary).** The real `derive`, `report`, `run`, `query` and `discover` commands against the fake Jira, a temporary SQLite file and a fixed clock. Prior art: `SuppressionTests`, `BaselineTests`, `TimeInStatusTableTests`, `MethodNoteAndFooterTests`, `QueryCommandTests`, and the configuration refusal tests. It must include:
   - **Each existing suppression scenario, repeated with suppression off,** asserting the figure that was withheld is now shown with its correct value, in the report and by `query`: a sprint of four assignees; a bucket with two assignees; a status row with two assignees; a delivered count with two deliverers (D49) and its cycle time table (D54); a baseline sprint under five (D50); a baseline sprint with a withheld delivered count (D56); a pooled baseline row with a suppressed member row (D44); footer counts with too few assignees and with zero; a row made only of unassigned issues.
   - **Other numbers:** a team minimum of four shows a sprint of four and withholds a sprint of three; two assignees behind a figure shows a row with two and withholds a row with one; one check at zero and the other at its default; numbers above the defaults withholding what the defaults show; the baseline rules (D44, D50, D56) and the delivered-count rules (D49, D54) each exercised at a non-default number, so that no view is left comparing against a literal five or three.
   - **One is not zero:** with one assignee behind a figure, a row of unassigned issues and a footer count of zero are withheld.
   - **The default:** no setting, and the setting written as five and three, each give a report and query results identical to today's for every one of those scenarios. The existing suppression tests stay as they are and stay green.
   - **No `suppressed` anywhere** in a report written with suppression off, over a store built to trip every rule at once.
   - **The disclosure line and method note:** the line is present when both numbers are zero, present with the numbers when either is below its default, and absent at or above the defaults; the method note states the numbers in force.
   - **Changing the setting** in either direction: `report` refuses until `derive` runs, and no `backfill` is needed.
   - **Refused values:** a missing number, a negative, a fraction, a quoted number, `null`, a boolean, and assignees behind a figure greater than a non-zero team; the message names the file and the setting.
   - **`discover`:** a new file has the setting as five and three; a rerun keeps changed numbers, keeps the defaults, and keeps the setting absent.
   - **What does not change:** the existing test that no account ID, display name, email or issue summary appears in the database file or the report passes with suppression off; the list of metric views is the same under any numbers, and none has a person column.
   - **The existing privacy probe with the default numbers:** the checks that no query recovers, bounds or tells zero from non-zero for a withheld delivered count still pass.
   - Expected multi-line strings normalized to LF, as the Windows run established.
2. **Core seam.** Only if the setting adds a pure rule to the settings code (loading, the default, the comparison of derive settings); tested beside the existing settings tests.

All tests use TUnit. All fixtures are synthetic.

Mutation records, where the build run uses them: rerun only the records a change affects.

## Out of Scope

- Different numbers for different figures, sprints or views: there is one pair for the whole deployment.
- A separate on/off switch beside the numbers.
- Removing the rule or any of its code paths.
- Any view, column or report grouped by person, by cohort, by seniority or by plan tier.
- The case study and volunteer cohort views.
- Any change to hashing, the salt, or which Jira fields are collected and stored.
- Rewriting reports already on disk.
- A command-line flag that overrides the setting for one run.

## Further Notes

- **The owner's reason (2026-11-12), for the ADR.** The ADR states it in these terms:
  - A fixed rule can leave a team without a usable baseline: one small sprint withholds every baseline figure, and a small team may have no sprint that passes.
  - A rule that is strict and cannot be configured does not fit every team that might use the tool, so the numbers belong in configuration, with the strict values as the default.
  - The Jira metrics are already visible to the team and the leaders involved. The tool aggregates data that anyone with access to the Jira board can already see; it adds no new information about a person.
  - The metrics are there to show progress, not to blame or shame anyone.
- **What that reason covers.** It is about figures derived from Jira, which is the only source built. ADR-0003 was written with the volunteers' AI usage in mind, which is not visible on a Jira board. The ADR says the reasoning applies to Jira-derived figures; a later spec that adds agent sessions, GitHub data or any result grouped by people decides how the setting applies there. ADR-0005 is unchanged.
- **The team knows.** The owner confirmed on 2026-11-12 that the team asked for this tool and is aware of what it reports. Nobody needs to be told separately that the numbers are changing.
- **What turning it off exposes.** Person fields stay hashed, but a figure for a bucket or a status that one or two people worked on in a sprint is, in effect, a figure about them to anyone who knows who worked on what. The disclosure line exists so that a reader of a forwarded report knows this.
- **The dated record.** The success criteria and metric definitions are unchanged. What changes is which figures are shown, after results have been seen; the glossary commit and the ADR say so.
- **The database file** is unaffected: it has always held every row, and opening it elsewhere has always bypassed the views.
