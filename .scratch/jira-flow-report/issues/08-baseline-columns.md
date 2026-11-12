# 08: Baseline columns

**What to build:** With a list of baseline sprint IDs in configuration, the cycle time table gains baseline median and P85 columns and the time in status table gains a baseline median column, each pooled over all issues in the baseline sprints. Baseline figures come from their own metric views and follow the same suppression rule. With no baseline configured, the report is unchanged.

**Blocked by:** 06

**Status:** ready-for-agent

- [ ] With baseline sprints configured, both tables show baseline columns computed over those sprints pooled together
- [ ] A baseline figure with fewer than three distinct assignees is suppressed in the view and the report
- [ ] With no baseline configured, no baseline columns appear
- [ ] The sprint being reported may itself be a baseline sprint without error
