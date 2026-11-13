---
id: xt-m5
status: active
project: Pipeline
filter: '/*/*/SprintStateTests/*'
file: src/Telltale.Reporting/SprintReport.cs
expectedMatches: 1
find: |-
  var dates = sprint.Ended is not null ? $"{start} to {LocalDate(sprint.Ended, teamTimeZone)}"
replace: |-
  var dates = (sprint.Ended ?? sprint.PlannedEnd) is { } end ? $"{start} to {LocalDate(end, teamTimeZone)}"
---
# xt-m5: a running sprint's title gives its planned end as its end

Claim (cross-ticket finding 7, review finding 6): the title of a sprint that is not completed says it is still running and names the planned end as a plan; only the sprint window's end is shown as the end.
Expected red: `An_issue_delivered_in_the_active_sprint_counts_in_it` and `An_active_sprint_past_its_planned_end_still_takes_deliveries_and_its_title_says_it_is_still_running` (both ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 2 failed of 13 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at 1e8622b: red, 2 failed of 13 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at baeaf03: red, 2 failed of 13 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at 2fa24b9: red, 2 failed of 13 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at b966865: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 2 failed of 15 (Pipeline, filter /*/*/SprintStateTests/*); restored.
