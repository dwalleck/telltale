---
id: xt-m1
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Core/Derive/Derivation.cs
expectedMatches: 1
find: |-
  .SelectMany(issue => statusChanges[issue.IssueId].SelectMany(change => new[] { change.FromStatusId, change.ToStatusId }).Append(issue.StatusId))
replace: |-
  .SelectMany(issue => statusChanges[issue.IssueId].SelectMany(change => new[] { change.ToStatusId }).Append(issue.StatusId))
---
# xt-m1: the role check reads only the status a change moves to

Claim (cross-ticket finding 1): a status with no role that a change names only as the status moved from still stops the run.
The test `A_status_with_no_role_that_a_change_only_names_as_the_status_moved_from_stops_the_run_and_is_named` was written after the fix, so it could not run red first.
- Verified 2026-11-12 at da6ced9: red, 1 failed of 47 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
