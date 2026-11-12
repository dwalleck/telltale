# 05: Time in status table

**What to build:** The report gains the time in status table: for each status, in board order, the issue count and the median business days issues spent in it during the sprint. The collector fetches Sprint field history, derive builds membership intervals and per-issue, per-sprint status durations using the existing Core behaviour, and a metric view provides the medians.

Owner decisions (2026-11-12): board order is the order of statuses in the configuration file (D19). Done-role statuses are left out of the table (D20). An issue counts toward a status only if it spent more than zero business days there (D21). The issue's current sprint IDs are stored (D22). A status nobody was in is omitted (D23).

**Blocked by:** 01

**Status:** resolved

- [x] An issue added to the sprint after it started only contributes time from when it joined
- [x] An issue removed mid-sprint only contributes time while it was a member
- [x] An issue carried over from the previous sprint contributes to each sprint only within that sprint's window
- [x] Statuses appear in board order, and a status no issue spent time in is omitted or shown with a zero count
- [x] Time in Shipped is stored in status intervals
- [x] Calendar time, business time and business days are all stored per issue, sprint and status

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as 977b9e2 (source `ticket/05-time-in-status-table` at 744dee9).

- Checks: the merged tree equals the source tree, so the implementer's full check on 744dee9 stands (record `ticket05-merge-record.json`, base e217a30): Core 47 and Pipeline 195 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 19 records (05-m1 to 05-m19), all red in the ticket branch (m1-m4, m6, m9 at 3bcbcd9; m8, m10-m18 at 4c2edcf; m5, m7, m19 at c568f6b). None rerun at merge, because the merge changed no file relative to the source. Ticket 01's record 01-m17 follows its moved line and now mutates the `to` value only; red at e34826a.
- Review: 11 findings. Fixed here: 2, 5, 6, 7, 8, 10, 11. Settled by owner decisions: 3 (D20) and 4 (D21). Deferred: 1 to ticket 06, 9 to ticket 07.
- D19: board order is the order `statusRoles` lists the statuses in the configuration file, carried by an ordered dictionary into `board_status.position`.
- D20: done-role statuses (Done as well as Shipped) are left out of the view and the table. Their time is still stored in `sprint_status_time` and `status_interval`.
- D21: an issue counts toward a status, in the count and the median, only when its business days there are more than zero. `sprint_status_time` still stores the zero-business-day row.
- D22: the issue's current sprint IDs are stored in `issue.sprint_ids` (migration `0002_issue_sprint_ids.sql`): empty for an issue in no sprint, NULL for a row stored before the column existed. Derive stops and asks for a backfill when any issue is NULL.
- D23: a status no issue spent business time in has no row.
- Note on finding 2: a Sprint change is stored only when both values are empty or digits separated by commas, but the accepted value is stored as Jira sent it, so it can hold whitespace around the commas (Jira writes `12, 13`). No other character can be stored. The current Sprint field is stored as digits and commas only.
- Ticket 06 picks up finding 1: `time_in_status_per_sprint` and the report table publish figures with no distinct-assignee check. Add the assignee counts to the view (rows come from `sprint_status_time`, joined to `issue` on `issue_id`); the report reads `median_business_days` as non-null today.
- Ticket 07 picks up finding 9: in `Derivation.Derive`, `membership.Anomalies` from `SprintMembership.Build(...)` and `timeline.Anomalies` a few lines above it are dropped inside the per-issue loop; 07 collects both.
- For later tickets: ticket 09 also adds a migration numbered 0002 (`0002_watermark.sql`) and merges second, so it renumbers; its "no derived data" check replaces the small try/catch in `SprintReport.ReadTimeInStatusAsync`. `Derivation.Derive` now takes a `BusinessCalendar`, and the configuration requires `jira.sprintFieldId` and `holidays`; ticket 03 should use the same `holidays` definition. `RawIssue.SprintIds` is the last positional parameter. The fake Jira has `FakeIssue.InSprints(...)` and `.SprintsChanged(at, ...)`.
