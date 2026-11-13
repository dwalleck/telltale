---
id: xt-m1
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Core/Derive/Derivation.cs
expectedMatches: 1
find: |-
  .SelectMany(issue => timelines[issue.IssueId].Entries.Select(entry => entry.StatusId).Append(issue.StatusId))
replace: |-
  .SelectMany(issue => timelines[issue.IssueId].Intervals.Select(interval => interval.StatusId).Append(issue.StatusId))
---
# xt-m1: the role check reads the status intervals, not the timeline's entries

Claim (cross-ticket finding 1, review finding 3): the role check covers every entry of each issue's timeline, so a status with no role that was entered and left at the same instant (mid-history, or at creation) stops the run and is named.
Expected red: `A_status_with_no_role_that_an_issue_entered_and_left_at_the_same_instant_stops_the_run_and_is_named` and `A_status_with_no_role_that_an_issue_was_created_in_and_left_at_that_instant_stops_the_run_and_is_named`. The first ran red before the fix; the second was written after it.
The other direction (a later change's "from" the timeline was not in does not stop the run) ran red first: `A_status_with_no_role_that_a_later_change_only_names_as_the_status_moved_from_does_not_stop_the_run`.
- Verified 2026-11-12 at 39943d6: red, 2 failed of 51 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at bd15475: red, 2 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 2fa24b9: red, 2 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 2 failed of 53 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 2 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 2 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 2 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
