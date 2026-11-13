# 03: Small groups and the count

**What to build:** A team member can rely on the brief visit count never saying more about a small group than the rest of the report does. The count is withheld in every case the spec lists, in the report and through `query`, and no combination of queries over the metric views gets round it. Ticket 01 put the rule in the view; this ticket tests it from every side and fixes what it finds.

When the `minimumGroupSize` setting exists (the configurable suppression spec, which is expected to be built first), the count follows the configured numbers, and with both at zero every count is shown, a count of nothing as `0`.

Spec: `../spec.md` (owner decision D62; the confirmed reading on zero; user stories 27 to 33; "The metric view"; "Testing Decisions"). The other spec: `../../optional-suppression/spec.md` ("The brief status visits spec").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] With two distinct assignees behind the counted issues, the line reads as withheld and the view's count is null with its flag set
- [ ] When the status's time in status row has its median withheld, the count is withheld
- [ ] A sprint that fails the team check has no brief visit lines in its footer and no rows in the view
- [ ] A count of zero reads the same as a withheld count, in the report and in the view
- [ ] A listed status with no time in status row in the sprint has no line and no row
- [ ] Issues with no assignee count toward nobody
- [ ] A sprint whose delivered count is withheld (D49) still shows a brief visit count that passes its own checks, as its time in status counts stay (D55)
- [ ] A probe over the metric views finds no query that tells a zero count from a withheld one
- [ ] The probe finds no query that recovers a withheld count by subtracting shown figures, including the row's issue count and the counts of other statuses and sprints
- [ ] Each case is asserted in the report and by `query`
- [ ] The existing privacy checks on the delivered count still pass with brief visits present
- [ ] If the `minimumGroupSize` setting exists: the count is withheld and shown according to the configured numbers; with both zero every sprint has its rows, every count is shown, and a count of nothing is `0`
- [ ] If the `minimumGroupSize` setting does not exist yet, the ticket says so in its comments, so that the suppression work knows to cover the brief visits view
- [ ] A leak found here is reported with the broad rule that would close it, as a decision needed, before a narrow fix is made
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
