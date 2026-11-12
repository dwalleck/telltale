# Spec: Jira flow report (tracer bullet)

Status: ready-for-agent

Terms in **bold** on first use are defined in `GLOSSARY.md`. Decisions referenced as ADR-000N are in `docs/adr/`. The workflow mapping and source-system details are in `docs/context/system-design.md`.

## Problem Statement

The project owner has to show leadership whether structured AI use changes how the team delivers, and has nothing to show yet. The only code that exists is interval and business-day math with no data behind it. There is no way to pull the team's Jira history, no store, and no report. Until one path works end to end, nobody knows whether the corporate network, Jira's changelog API, the privacy rules and the metric definitions actually fit together, and the pilot's baseline cannot be measured before the pilot starts.

## Solution

A command-line tool, Telltale, that the owner runs on the work laptop. It reads the team's Jira board, keeps a pseudonymized local copy of what the metrics need, and writes one plain markdown report per sprint showing how many issues were **delivered**, how long they took (**cycle time**, median and P85, by **point bucket**), and where the time went (**time in status**). The same run can be repeated at any time and gives the same answer for a completed sprint. Nothing in the store or the report identifies a person.

This is the first slice of the whole system: every layer (collector, raw tables, derived tables, metric views, report) exists and works for Jira, so later specs add sources and metrics rather than layers.

## User Stories

### Setting up

1. As the project owner, I want a `discover` command that reads my Jira site and writes the configuration file, so that I don't have to hunt for board, status, resolution and custom field IDs by hand.
2. As the project owner, I want `discover` to check the proxy and certificate trust first and say plainly when that is what failed, so that a TLS error on the first call doesn't look like a bug in the tool.
3. As the project owner, I want `discover` to list every status on the board with its Jira category and a proposed role (not started, work, blocked, done), so that I can correct the mapping before any metric depends on it.
4. As the project owner, I want `discover` to list Jira's resolutions and let me mark which ones mean abandoned, so that the "Won't Do" assumption is confirmed against the real system.
5. As the project owner, I want `discover` to find the Sprint and story point field IDs, so that the collector asks for the right fields.
6. As the project owner, I want the configuration file to contain no secrets, so that it can be committed and reviewed.
7. As the project owner, I want a `secrets set` command that stores my Jira email, Jira API token and the hashing salt in Windows Credential Manager, so that no secret is ever in a file or the repo.
8. As a developer of the tool, I want secrets read from environment variables when I am not on Windows, so that I can build and test on another machine.
9. As the project owner, I want the team's time zone and the company holiday list in configuration, so that a **business day** means the same thing in every number.
10. As the project owner, I want to pin the **baseline** as a list of sprint IDs in configuration, so that the git history shows the window was fixed before results existed.

### Collecting

11. As the project owner, I want a `backfill` command that fetches the board's full history once, so that the baseline sprints are available.
12. As the project owner, I want a `sync` command that fetches only what changed since the last run, so that routine runs take minutes.
13. As the project owner, I want reruns and missed runs to be harmless, so that I can run the tool by hand whenever the laptop is on.
14. As the project owner, I want an interrupted backfill to resume without duplicating data, so that an overnight run that loses the network isn't wasted.
15. As the project owner, I want status, Sprint and story point history fetched in full for every issue, so that old issues with long histories are not silently truncated (ADR-0004).
16. As a team member, I want my Jira identity replaced by a salted hash before anything is written to disk, so that the store cannot be used to look me up (ADR-0003).
17. As a team member, I want no issue summaries, descriptions or comments stored, so that the store holds no free text about my work (ADR-0002).
18. As the project owner, I want epics and sub-tasks left out, so that containers and double-counted work don't distort throughput.
19. As the project owner, I want derive (and so run) to log every changelog anomaly it finds, so that I can investigate bad history without the run stopping.
20. As the project owner, I want the tool to back off and retry when Jira rate-limits it, so that a large backfill finishes unattended.

### Deriving

21. As the project owner, I want a `derive` command that rebuilds all derived data from the raw tables, so that changing a definition means rerunning, never migrating (ADR-0002).
22. As the project owner, I want cycle time to start when development work first starts and end at the last entry into Done, so that it matches the settled definition.
23. As the project owner, I want an issue that sat in Blocked before development started to have no cycle running, so that waiting to start is not counted as working.
24. As the project owner, I want time in Blocked after the cycle started to count toward cycle time, so that the number reflects how long delivery really took.
25. As the project owner, I want a move from Done to Shipped to leave the cycle end unchanged, so that a release schedule the team doesn't control stays out of the metric.
26. As the project owner, I want a reopened issue's cycle time to run to its final Done, and the **reopen** to be recorded, so that a later guardrail can count it.
27. As the project owner, I want abandoned issues excluded from delivered counts and cycle time, so that cancelled work doesn't look like throughput.
28. As the project owner, I want each issue's point bucket taken from its estimate at the moment its cycle started, so that a re-estimate after the fact can't move it (ADR-0006).
29. As the project owner, I want issues with no estimate at cycle start grouped as unestimated, so that they are visible and not dropped.
30. As the project owner, I want each delivered issue counted in the sprint whose **sprint window** contains its final Done transition, so that throughput and cycle time agree on which sprint a delivery belongs to.
31. As the project owner, I want an issue delivered between sprints to be counted and reported as such, so that it isn't lost.
32. As the project owner, I want every duration available in business days and calendar days, so that our numbers can be compared with other teams' (ADR-0001).

### Reporting

33. As the project owner, I want a `report` command that writes one markdown file for a sprint, so that I have something to read and paste before Confluence publishing exists.
34. As the project owner, I want `report` to default to the most recently completed sprint and accept a sprint ID, so that I can produce the baseline sprints' reports too.
35. As the project owner, I want the report to show the delivered issue count, so that I can see throughput.
36. As the project owner, I want median and P85 cycle time by point bucket, with the number of issues in each bucket, so that I can judge how much weight a figure can bear.
37. As the project owner, I want the median time in each status in board order, so that I can see where issues wait.
38. As the project owner, I want a baseline column once the baseline sprint list is configured, so that each sprint is read against the team's own history.
39. As a team member, I want any figure with fewer than three distinct assignees behind it shown as suppressed, so that a small bucket can't reveal one person's work.
40. As a team member, I want the report to contain no issue keys and no names, so that it can be shared without exposing anyone.
41. As a leadership reader, I want a short method note in the report (business days, median and P85, suppression rule, as-of time), so that I can trust what the figures mean.
42. As the project owner, I want the report footer to say how many issues had changelog anomalies and how many were delivered between sprints, so that I can judge data quality.
43. As the project owner, I want a report on a completed sprint to be identical each time it is generated from the same data, so that a published number doesn't drift.
44. As the project owner, I want a `run` command that does sync, derive and report in one go, so that the per-sprint routine is one command.

### Querying and trust

45. As the project owner, I want to run ad hoc SQL against the metric views, so that I can answer follow-up questions without new code.
46. As a team member, I want the suppression rule enforced inside the metric views, so that ad hoc SQL against them cannot produce a figure the report would have suppressed.
47. As the project owner, I want the database to be disposable and rebuildable from Jira, so that a corrupt or lost file costs one backfill.

## Implementation Decisions

### Platform and language

- The solution targets .NET 11 and C# 15 (both generally available as of November 2026). The existing Core project and its tests already target .NET 11.
- Use C# 15 and .NET 11 features where they make the code clearer, not for their own sake. Union types are the main one. Where a value is one of a fixed set of shapes, model it as a union and consume it with an exhaustive `switch` expression that has no discard arm, so adding a case is a compile error at every use. Candidates in this spec:
  - a history change: status change, sprint change or point change;
  - an issue's outcome: delivered with a cycle, delivered without a cycle, abandoned, or not delivered;
  - a reported figure: a value or suppressed;
  - a status role: not started, work, blocked or done;
  - the result of a Jira call: data, rate-limited with a retry delay, or failed.
- Known limits of compiler-generated unions to design around: value-type cases are boxed, and a union gets no equality, cloning or deconstruction of its own. Prefer record classes as case types, and compare the case values rather than the union when equality matters. Closed hierarchies (the `closed` modifier) are the alternative where cases need shared members.
- Other features worth using where they fit: collection expression arguments, extension members, the new LINQ joins, and the newer process-running helpers if a command shells out. System.Text.Json's union support is not relied on for configuration or stored data without first checking how it serializes.
- Core stays dependency-free; unions are a language feature and add no package.

### Modules

- **Core** (exists; extended). Stays dependency-free. Gains: cycle calculation for one issue from its **status intervals** and a status-role map; point-at-time reconstruction from story point history; median and P85; identity hashing. Existing interval, calendar, timeline, membership and time-in-status behaviour is preserved as documented in the system design.
- **Collectors** (new), Jira only in this spec. One deep module whose interface is "bring the store up to date with Jira" in backfill or incremental mode. It hides paging, the bulk changelog endpoint, deduplication, timestamp parsing, retry and hashing.
- **Storage** (new). SQLite behind a repository interface so Postgres can replace it later (ADR-0002). Owns migrations, upserts keyed by Jira's own IDs, watermarks, the derived rebuild's writes, and the metric views.
- **Reporting** (new). Reads only from metric views and renders markdown. No Confluence, no charts.
- **CLI** (new). The composition root and the only laptop-specific project. Commands: `discover`, `secrets set`, `backfill`, `sync`, `derive`, `report`, `run`. Built on System.CommandLine.

### Configuration and secrets

- One non-secret configuration file holds: Jira site URL, board ID, Sprint and story point field IDs, the status-role map by status ID, abandoned resolution IDs, team time zone (IANA ID), holiday dates, baseline sprint IDs, and the backfill start date.
- The status-role map gives each status ID one role: not started (New, Approved, Committed), work (Development, Review, QE), blocked (Blocked), or done (Done, Shipped). `discover` proposes roles from Jira's status categories; the owner corrects them.
- Secrets sit behind one secret-store interface with two implementations: Windows Credential Manager, and environment variables for development. Secrets are the Jira account email, the Jira API token and the hashing salt. `secrets set` can generate the salt.
- HTTP uses the system proxy and the operating system's certificate store, which is where the corporate root certificate is trusted.

### Jira collection

- Issues are found by JQL on the board's project(s), `updated` since the watermark, overlapping by a few minutes; backfill uses the configured start date. Use Jira Cloud's current token-paged JQL search endpoint; confirm the endpoint on the work laptop, since the older search endpoint has been retired.
- History comes from the bulk changelog endpoint, filtered to status, Sprint and story point fields, paged until the token is null, deduplicated by history ID. Never `expand=changelog` (ADR-0004).
- Sprints come from the Agile API for the configured board.
- Statuses, resolutions and fields come from their listing endpoints during `discover`, and statuses are refreshed on sync.
- The watermark advances only after a batch is fully written, so an interrupted run resumes safely.
- Rate limiting (HTTP 429) is honoured using the server's retry-after value, with bounded retries.
- Person fields are hashed with HMAC-SHA256 over the Jira account ID using the salt, before storage. No name-to-hash table exists.

### Schema

Raw tables (written only by the collector; selected fields, never original payloads):

- **Issue**: Jira issue ID, key, issue type ID, created time, current status ID, current resolution ID, current story points, current assignee hash, updated time.
- **Change**: history ID, issue ID, time, field (status, sprint or points), from value, to value. Status values are status IDs; sprint values are sprint ID lists; point values are numbers.
- **Sprint**: sprint ID, board ID, state, start, planned end, completed time.
- **Status**: status ID, name, Jira category.
- **Watermark**: one row per collector.

Derived tables (dropped and rebuilt by `derive`):

- **Status interval** and **membership interval**, per issue.
- **Issue cycle**: issue ID, cycle start, cycle end, calendar days, business days, point bucket at start, delivered flag, delivery sprint ID (null if between sprints), reopen count.
- **Sprint status time**: issue ID, sprint ID, status ID, calendar time, business time, business days.
- **Anomaly**: issue ID and message.
- **Run**: the as-of time used for open intervals.

Metric views (the only thing Reporting and ad hoc SQL should read):

- Delivered count per sprint.
- Cycle time per sprint and point bucket: issue count, median, P85, in business and calendar days.
- Time in status per sprint and status: issue count and median. Done-role statuses are left out, and an issue counts only if it spent more than zero business days in the status (owner decisions D20, D21, 2026-11-12).
- Baseline equivalents of the two above, pooled over the configured baseline sprints.
- Each view row carries its distinct-assignee count check inside the view: when the sprint has fewer than five distinct assignees, or the row has fewer than three, the figures are null and a suppressed flag is set. Delivered count per sprint is subject to the five-person team check only.

### Metric rules

- Cycle start: the first entry into a work-role status. Entries into blocked never start a cycle.
- Cycle end: the last transition from a non-done-role status into a done-role status, and only if the issue is in a done-role status at the as-of time.
- Delivered: has a cycle end and its resolution is not in the abandoned list. An issue that reached Done without ever entering a work status is delivered and counts toward throughput but has no cycle time.
- Reopen: any transition from a done-role status to a non-done-role status.
- Point bucket: the story point value in effect at cycle start, reconstructed from point history; the current value if there is no history; unestimated if none.
- Delivery sprint: the sprint whose window (start to completion) contains the cycle end.
- Time in status per sprint: existing Core behaviour, over issues that were members of the sprint.
- Owner decisions of 2026-11-12 (D12-D32) refine these rules at their edges; they are listed in the tickets they changed (02, 03, 05, 09, 11, 12).
- Median: the middle value, or the mean of the two middle values. P85: nearest rank (the smallest value with at least 85% of values at or below it). Percentiles are computed in SQL with window functions.
- Assignee for the suppression rule: the issue's assignee at the time of the sync. Unassigned issues count toward nobody.
- All per-issue durations are computed in C# during `derive`; the views only aggregate.

### Report

One markdown file per sprint, named by sprint ID: title with sprint name and dates; delivered count; cycle time table (rows are point buckets; columns are issue count, median and P85 in business days, median and P85 in calendar days, and baseline median and P85 when configured); time in status table (rows are statuses in board order; columns are issue count, median business days, baseline median when configured); method note; footer with as-of time, anomaly count and delivered-between-sprints count. Suppressed cells say "suppressed" rather than showing a blank or zero.

## Testing Decisions

A good test here states a rule from this spec or the glossary in terms of Jira history going in and a report figure or view row coming out. It does not assert on table layouts, SQL text, HTTP call order or class structure, so the internals can change freely.

Two seams, agreed with the owner:

1. **Pipeline seam (new, primary).** Tests run the real sync, derive and report commands against a fake Jira (a stub HTTP handler serving synthetic responses built from the documented response shapes), a temporary SQLite file, a fixed clock and an in-memory secret store. Assertions are on the markdown report and on direct SQL against the metric views. This seam covers the collector, storage, derive, views and reporting together. It must include:
   - paging and duplicate history entries from the bulk changelog endpoint;
   - an incremental sync after a backfill, and a rerun that changes nothing;
   - each cycle rule: Blocked before start, Blocked after start, skipped Development, Done to Shipped, reopen then Done again, abandoned resolution, straight to Done;
   - re-estimate after cycle start leaving the bucket unchanged;
   - delivery inside a sprint, in a later sprint than the one it started in, and between sprints;
   - suppression: a bucket with two assignees is suppressed in the report and in a direct query of the view; a sprint with four assignees suppresses everything;
   - no account ID, display name, email or issue summary appears anywhere in the database file or the report;
   - a rate-limited response followed by success;
   - business vs. calendar days across a weekend and a configured holiday.
2. **Core seam (existing).** Pure-function tests, following the existing tests for timelines, membership, time in status and the business calendar (including their shared time helper). Extended for cycle calculation, point-at-time reconstruction, median and P85 edge cases (one value, even counts, ties), and hashing (same input and salt give the same hash; different salts differ).

All tests use TUnit, not xUnit. The 24 existing Core tests have been ported and are the pattern to follow: awaited assertions, and ordered collection comparisons stated explicitly. TUnit runs on Microsoft.Testing.Platform, which `global.json` selects for `dotnet test`.

No separate test suites for the collector or storage layers; they are covered through the pipeline seam. `discover` and the Credential Manager implementation touch live systems and Windows, so they are verified by hand on the work laptop, with `discover`'s parsing of status, resolution and field listings covered through the stub handler.

All fixtures are synthetic. No real Jira data is used in tests or committed to the repo.

## Out of Scope

- GitHub review metrics, the git collector, agent sessions and the session exporter, each a later spec.
- **Guardrail metrics**: reopen rate, revert rate, rework, linked bugs. Reopens are recorded in derived data here but not reported.
- Structured vs. ad hoc comparison, the volunteer cohort views and the case study views. This report is whole-team only.
- Confluence publishing, charts and the `publish` command.
- Forecasting: service level expectation, aging work in progress, Monte Carlo.
- Worklogs and hours per point.
- The replay benchmark.
- Task Scheduler setup and the AWS host.
- Analysis of time in Shipped. It is kept in status intervals and not reported.
- Assignee history and attribution of Blocked time to a stage.

## Further Notes

- Development happens off the work laptop against synthetic fixtures. The first real run is `discover` on the work laptop; expect it to surface differences from the documented Jira shapes, which should be fixed by adding a synthetic fixture that reproduces them.
- Three assumptions to confirm at that first run: abandoned work is Done with a "Won't Do" resolution; Blocked, Development, Review and QE have the roles given above; the board is a single Scrum board whose sprints don't overlap.
- The Core tests pass under TUnit on .NET 11 on the development machine (24 passing); the work laptop run is still to come.
- The work laptop needs the .NET 11 SDK. It is a managed machine, so confirm the SDK can be installed before relying on it. .NET 11 is a standard-term release, so plan the move to .NET 12 when the tool goes to AWS.
- Owner tasks outside this spec: the volunteers' consent to cohort reporting, the baseline sprint list, and the date the success criteria go to leadership.
- The handoff's next step after this spec is `/to-tickets`.
