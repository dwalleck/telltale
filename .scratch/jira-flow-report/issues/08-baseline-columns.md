# 08: Baseline columns

**What to build:** With a list of baseline sprint IDs in configuration, the cycle time table gains baseline median and P85 columns and the time in status table gains a baseline median column, each pooled over all issues in the baseline sprints. Baseline figures come from their own metric views and follow the same suppression rule. With no baseline configured, the report is unchanged.

Owner decisions (2026-11-12): a baseline row is shown only when the pooled group passes the suppression rule (pooled team of at least five, at least three distinct assignees behind the row) and no baseline sprint has that row suppressed in its own view; otherwise its figures are withheld. A baseline sprint with no issue in a row does not suppress it. The pooled team is the distinct assignees of every issue of every baseline sprint. The baseline views carry no issue count. A baseline sprint ID is checked against the store at `derive` and `report`; the loader checks digits only (D44). Every row of a sprint that fails the five-person check counts as suppressed (D45). A baseline row that exists only because such a sprint has an issue in it still appears, reading `suppressed`: it shows that some baseline sprint had an issue in that bucket or status, not which sprint, whose, or how long (D46).

**Blocked by:** 06

**Status:** ready-for-agent

- [ ] With baseline sprints configured, both tables show baseline columns computed over those sprints pooled together
- [ ] A baseline figure with fewer than three distinct assignees is suppressed in the view and the report
- [ ] With no baseline configured, no baseline columns appear
- [ ] The sprint being reported may itself be a baseline sprint without error
