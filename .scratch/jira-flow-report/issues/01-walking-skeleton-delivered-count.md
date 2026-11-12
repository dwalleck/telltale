# 01: Walking skeleton: delivered count per sprint

**What to build:** One `run` command syncs from Jira into a local SQLite store, derives each issue's status intervals and delivery, and writes a markdown report for a sprint showing its name, dates and delivered issue count. This is the first path through every layer (collector, raw tables, derived tables, metric views, report, CLI), so it also establishes the configuration file, the secret-store interface with environment-variable and in-memory implementations, identity hashing at import, and the pipeline test harness that every later ticket extends: a stub HTTP handler serving synthetic Jira responses, a temporary SQLite file, a fixed clock.

In this slice an issue is delivered when it is in a done-role status at the as-of time; its delivery moment is its last transition from a non-done-role status into a done-role status; and it belongs to the sprint whose sprint window contains that moment. Status roles come from the configuration file. Paging, incremental sync, abandoned resolutions and issue-type filtering come in later tickets. See the spec's Schema, Metric rules and Testing Decisions sections.

**Blocked by:** None (can start immediately)

**Status:** resolved

- [x] `run` against the fake Jira produces a markdown report for a sprint with its name, dates and delivered count
- [x] An issue whose final Done transition falls in a later sprint than the one it started in is counted in the later sprint
- [x] A move from Done to Shipped does not change which sprint the issue is delivered in
- [x] An issue that was Done, reopened and is not Done at the as-of time is not delivered
- [x] Reporting and the test's ad hoc SQL read the delivered count from a metric view, not from raw or derived tables
- [x] Assignees are stored only as HMAC-SHA256 hashes of the Jira account ID, using a salt from the secret store
- [x] A test scans the database file and the report and finds no account ID, display name, email or issue summary from the fixtures
- [x] Statuses are matched by ID throughout; renaming a status in the fixtures changes no figure
- [x] The configuration file contains no secrets
- [x] The existing 24 Core tests still pass, and new tests use TUnit

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as d0c9deb (source `ticket/01-walking-skeleton` at de131a6).

- Checks: the merged tree equals the source tree, so the implementer's full check on de131a6 stands: Core 47 and Pipeline 97 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass.
- Mutations: 33 records (01-m1 to 01-m33), all red in the ticket branch. None rerun at merge, because the merge changed no file relative to de131a6.
- Review: findings 1-40. Fixed here: 1-9, 12-21, 23-26, 28-31, 33, 34, 36-40. Left to their owners: 11 and the views-only part of 12 (ticket 06), 22 and 35 (ticket 12), paging in 8 and 24 (ticket 10), 10 and 27 (ticket 14).
- D2: the project keys come from `jira.projectKeys`; the collector does not call the board's project endpoint.
- D3: the sprint name is stored and shown in the report; the sprint goal is never stored.
- D7: a closed sprint with no completion time has a window ending at its planned end. A closed sprint with neither time stops the run with a message naming the sprint; that case has no owner decision yet.
- D8: an issue in a done-role status whose changelog ends outside done is not delivered, even with an earlier move into done.
- D9: an issue created in a done-role status and never moved into done is not delivered.
- For later tickets: the derived table `sprint_window` holds each started sprint's window, and views join it.
