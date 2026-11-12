# 01: Walking skeleton: delivered count per sprint

**What to build:** One `run` command syncs from Jira into a local SQLite store, derives each issue's status intervals and delivery, and writes a markdown report for a sprint showing its name, dates and delivered issue count. This is the first path through every layer (collector, raw tables, derived tables, metric views, report, CLI), so it also establishes the configuration file, the secret-store interface with environment-variable and in-memory implementations, identity hashing at import, and the pipeline test harness that every later ticket extends: a stub HTTP handler serving synthetic Jira responses, a temporary SQLite file, a fixed clock.

In this slice an issue is delivered when it is in a done-role status at the as-of time; its delivery moment is its last transition from a non-done-role status into a done-role status; and it belongs to the sprint whose sprint window contains that moment. Status roles come from the configuration file. Paging, incremental sync, abandoned resolutions and issue-type filtering come in later tickets. See the spec's Schema, Metric rules and Testing Decisions sections.

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] `run` against the fake Jira produces a markdown report for a sprint with its name, dates and delivered count
- [ ] An issue whose final Done transition falls in a later sprint than the one it started in is counted in the later sprint
- [ ] A move from Done to Shipped does not change which sprint the issue is delivered in
- [ ] An issue that was Done, reopened and is not Done at the as-of time is not delivered
- [ ] Reporting and the test's ad hoc SQL read the delivered count from a metric view, not from raw or derived tables
- [ ] Assignees are stored only as HMAC-SHA256 hashes of the Jira account ID, using a salt from the secret store
- [ ] A test scans the database file and the report and finds no account ID, display name, email or issue summary from the fixtures
- [ ] Statuses are matched by ID throughout; renaming a status in the fixtures changes no figure
- [ ] The configuration file contains no secrets
- [ ] The existing 24 Core tests still pass, and new tests use TUnit
