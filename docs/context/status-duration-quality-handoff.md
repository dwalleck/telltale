# Handoff: status-duration quality and review timing

Date: 2026-11-12

## Purpose and safety

The first real-use investigation exposed a distinction between correctly calculating Jira history and measuring the work that actually happened. The next task is to make duration quality visible, then decide whether independent source events can support better measurements.

This repository is public. This handoff deliberately excludes company sprint IDs, report values, issue/person identifiers, raw changelogs, and configuration contents. Do not add those to commits, fixtures, tickets, or PR descriptions. Inspect operational data only on the company machine; use synthetic fixtures for committed tests. Reports and the SQLite store remain outside the repository, conventionally under `%LOCALAPPDATA%\Telltale\work`.

## Completed change

Commit `437e710` adds **Total (business days)** beside the median in the time-in-status table.

- The total sums existing per-issue, per-sprint, per-status business durations before display rounding. Repeated visits to a status are included.
- It uses the same sprint-window and membership clipping as the median.
- Both figures use the same privacy suppression. Baseline status comparisons remain pooled medians; no baseline total was added.
- The report explains that totals are accumulated issue time, not effort or the sprint's elapsed duration.
- The definition and rationale are recorded in `GLOSSARY.md` and decision D57 in `docs/decisions.md`.
- Verification: 96 focused pipeline tests passed: 35 time-in-status, 17 suppression, 41 baseline, and three individual report/collection contract tests. The CLI also generated a report from an isolated local copy of operational data. That temporary copy was deleted; the original store and reports were not changed by the smoke run.
- SQL-derived objects are versioned by their script contents. After deploying the updated CLI, run `derive`, then regenerate the desired reports. Updating the binary does not rewrite existing Markdown files.

No duration-quality detection, exclusions, fallback calculations, GitHub collection, or Azure DevOps collection were implemented during this investigation.

## Issues encountered

### 1. Baseline suppression looked like a command failure

A baseline sprint can fail the privacy checks: fewer than five distinct sprint assignees, or fewer than three among its delivered issues. If any configured baseline sprint fails, every baseline figure is withheld. The console notice does not mean `derive` or `report` failed.

Removing a sprint from `baselineSprintIds` changes the comparison population; it does not require deleting collected data. Do not change the baseline silently or merely to obtain publishable or favorable results. Record the reason for any revision and rerun `derive`.

### 2. Report output and baseline selection were easy to misread

- `report` writes one file per invocation, selecting the most recently completed sprint unless `--sprint` is supplied. It does not generate all historical reports automatically.
- Baseline columns come from the fixed `baselineSprintIds` configuration, not automatically from sprints preceding the reported sprint.
- A configured baseline can include the reported sprint itself or later sprints. That is a fixed comparison pool, not a historical-only baseline for each report.
- Status baselines pool individual issue-sprint durations and take their median; they do not average sprint medians. An issue spanning multiple baseline sprints contributes a separate duration for each.

### 3. Time-in-status counts and durations describe different things than expected

The existing metric includes all issues with positive business time in a status while members of the sprint, not just delivered issues. Removed, carried-over, unfinished, and abandoned issues can contribute. An issue can appear in several status rows but is counted once per status per sprint.

Time is clipped to both the sprint window and membership intervals. It is not the full lifetime duration of issues delivered in that sprint. Consequently, counts can exceed delivered counts and durations can be much shorter than full-lifetime durations.

Business days count eligible local calendar days in full, not eight-hour shifts. Eight eligible hours equal one third of a business day. Weekends and configured holidays are excluded.

Cycle time is different: for delivered issues, it runs from first entry into a work status to the last entry into done, including prior sprints and intervening waits. Point buckets use the estimate at cycle start. Its sample excludes delivered issues without a measurable cycle start. P85 can simply be the maximum in a small sample.

### 4. One-decimal days hide short but nonzero durations

A positive duration under 0.05 days can display as `0.0`. Independent inspection confirmed this was happening in the reports. The display should not be interpreted as literally zero time.

Hours, mixed units, or an explicit less-than label were discussed, but no display-rule change was approved or implemented. A better unit alone will not repair unreliable source timestamps.

### 5. Brief state visits distort the meaning of Development and Code Review

The investigation replayed stored raw status and sprint-membership changes independently. The inspected counts and durations matched the derived data; no arithmetic or interval-clipping defect was found in those cases. This verifies processing of the collected history, not that every Jira event was collected or that the recorded transitions match real work.

There was evidence of the sequence:

`Committed -> Development -> Code Review`

with only seconds in Development, sometimes followed by another very brief Code Review visit. Inspection of full recorded histories showed that sprint clipping was not the main explanation for the brief visits examined.

The owner identified forgotten status updates as the explanation for near-instant review visits and suspected the same for Development. The transition patterns support catch-up bookkeeping, but cannot reveal when actual development or review began. Time previously recorded as Committed may contain both waiting and work.

A diagnostic sensitivity calculation excluding sub-minute development durations materially changed the median. It was exploratory only, not a corrected result or an adopted exclusion rule. No stored data or reporting definitions were changed as a result.

## Proposed direction, not yet an approved specification

### Detect suspicious durations before attempting repairs

Add explicit duration-quality warnings for selected work stages and rapid multi-stage transitions. Do not apply a universal cutoff to every status: brief visits may be legitimate or automated. The existing `anomaly` mechanism detects changelog inconsistencies; suspicious but internally consistent timestamps are a separate quality concern and must not silently change that category's meaning.

Keep raw history and recorded measurements intact. Distinguish recorded, suspicious, independently sourced/proxy, and unavailable measurements rather than silently replacing short values with longer ones.

Decisions still needed before implementation:

1. Which stages qualify, and how to identify them without hardcoding company status IDs or assuming every work-role status means Development?
2. Thresholds and boundaries: seconds versus business time, individual visit versus accumulated per-item duration, and treatment of repeated visits, zero-duration transitions, weekends, and legitimate sprint-clipped fragments.
3. Whether warnings merely annotate existing figures or exclude contributions from separately labeled quality-qualified figures.
4. How to show entrant counts, eligible sample counts, suspicious/missing counts, and incomplete totals without disclosing small groups.
5. Whether a fallback is available, its source and precedence, its label, and what happens when sources disagree or are incomplete.

A sub-minute threshold was an investigative example, not a settled metric rule. Because the data has already been inspected, document any subsequent rule as an explicit revision, with its reason and limitations; do not describe it as a criterion fixed before results existed. Apply an adopted rule consistently to historical and future data and baselines.

### Privacy must follow the contributors to each figure

The current time-in-status view uses one population for issue counts, assignee counts, medians, and totals. Keeping all entrants in the count while filtering duration samples requires separating those populations.

- The sprint's five-person gate still applies.
- A duration figure must have at least three distinct assignees among the actual eligible duration contributors, not merely among all entrants.
- A filtered median or total must be withheld when that contributor gate fails.
- Baseline suppression must follow the eligible per-sprint populations, so pooling cannot recover a withheld figure.
- Quality counts and breakdowns also require privacy treatment; they are not automatically safe because they describe anomalies.

Do not weaken ADR-0003 or ADR-0005 to make the new diagnostics easier to display.

### Prefer PR events for PR review measurements

The owner suggested GitHub/Azure DevOps as a more accurate source for review timing. Candidate measures discussed:

- Ready for review to first human review: initial review wait.
- Ready for review to required approvals satisfied: review turnaround.
- Required approvals satisfied to merge: post-approval delay.

These are elapsed-time measures, not hands-on effort. Draft intervals, automation, requested changes, new commits, renewed approvals, missing history, and provider-specific event availability must be resolved before implementation. The discussion did not verify provider API capabilities or choose an integration.

Report per PR initially rather than silently summing overlapping PR durations into an issue-level number. Decide issue linkage and aggregation explicitly. Git activity can also provide development proxies, but a first commit is not necessarily the start of development. Do not automatically relabel Committed time as Development.

Keep Jira's metric labeled as time recorded in a workflow status. Do not mix Jira intervals and PR-based proxies into a single median without an explicit, justified definition and visible provenance.

### A requested comparison table needs a separate definition

The owner showed another team's averages for Development, Code Review, QE, and their sum, and asked about median per work item instead of per-story-point averages.

The data can support a similar median table, but cohort and window must be explicit. A suggested alternative was items delivered in a sprint using their full status histories, rather than the current membership-clipped table. This was not implemented or approved as a replacement.

For a combined Development + Review + QE row, first sum those durations per item, then take the median across a consistently defined population. Never add status medians. Specify absent stages and unreliable contributions. Such a sum differs from first-work-to-Done cycle time and still includes waiting within those stages.

## Entry points for the next agent

- Definitions and constraints: `GLOSSARY.md`, `docs/decisions.md`, `docs/adr/0003-pseudonymized-aggregates-only.md`, `docs/adr/0005-agent-sessions-carry-no-identity.md`.
- Raw-to-derived orchestration: `src/Telltale.Core/Derive/Derivation.cs`.
- History and clipping: `src/Telltale.Core/Intervals/StatusTimeline.cs`, `SprintMembership.cs`, `TimeInStatus.cs`.
- Existing anomaly classification: `src/Telltale.Core/Derive/ChangelogAnomalies.cs`.
- Business-time measurement: `src/Telltale.Core/Calendar/BusinessCalendar.cs`.
- SQL populations and privacy: `src/Telltale.Storage.Sqlite/Sql/Views/time_in_status_per_sprint.sql`, `sprint_team.sql`, `baseline_time_in_status.sql`, `baseline_sprint.sql`.
- Duration samples and statistics: `src/Telltale.Storage.Sqlite/Sql/Derived/duration_sample.sql`, `duration_figures.sql`.
- Report rendering: `src/Telltale.Reporting/SprintReport.cs`; console notices: `src/Telltale.Pipeline/PipelineSteps.cs`.
- Existing test seams: `tests/Telltale.Pipeline.Tests/TimeInStatusTableTests.cs`, `SuppressionTests.cs`, `BaselineTests.cs`, `MethodNoteAndFooterTests.cs`, plus core interval tests.

## Recommended next work

1. Agree on a small, explicit duration-quality specification with the owner. Start with flags and quality indicators rather than fabricated replacement durations.
2. Use synthetic examples for rapid catch-up transitions, legitimate brief visits, repeated visits, clipping, weekends/holidays, and contributor counts falling below privacy thresholds after filtering.
3. Preserve recorded data, implement the approved reporting behavior end to end, and verify baseline suppression as well as per-sprint figures.
4. Record and commit any metric-definition change with its reason. Update the report's method note and distinguish diagnostic hypotheses from measured facts.
5. Investigate the selected PR provider's available history before defining or implementing fallbacks.

This handoff is an investigation summary, not an approved anomaly/fallback specification. Do not infer authorization to exclude durations, reinterpret Committed time, or revise the evaluation's success criteria.
