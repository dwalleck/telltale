# 07: Method note and data-quality footer

**What to build:** The report explains itself and its data quality. A short method note states that durations are business days in the team's time zone with calendar days alongside, that figures are medians and P85s, what the suppression rule is, and the as-of time. The footer states how many issues had changelog anomalies and how many were delivered between sprints. The sync logs each anomaly, and anomalies are stored in derived data. An issue whose final Done transition falls outside every sprint window is delivered between sprints: it is in no sprint's figures and is counted in the footer.

Owner decision (2026-11-12): every report prints the as-of time and derive stores it (D33). Anomalies are logged by `derive` and `run`, the steps that build each issue's history; `sync` alone logs nothing (D37). The method note says that the group-size checks count Jira accounts, so a bot or a person's second account counts as a person (D42). In a sprint that fails the five-person check, each table shows a single "suppressed" line with no per-bucket or per-status rows, in the metric views and the report, so a reader cannot see which buckets or statuses are non-empty; this ticket builds it (D45).

**Blocked by:** 03, 05

**Status:** ready-for-agent

- [ ] The report contains the method note and the as-of time used for open intervals
- [ ] An issue with inconsistent status history still gets a timeline, is logged by the sync, and is counted in the footer
- [ ] An issue delivered between two sprints is in neither sprint's delivered count and is counted in the footer
- [ ] The report contains no issue keys and no names
