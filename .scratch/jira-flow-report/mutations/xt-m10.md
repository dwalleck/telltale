---
id: xt-m10
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  if (statusRoles.Any(role => !IsStatusId(role.Key)))
replace: |-
  if (statusRoles.Any(role => role.Key.Length == 0))
---
# xt-m10: discover shows a status ID of the file that is not digits

Claim (review finding 8): a status ID in the file that is not digits stops a rerun before anything is shown or copied.
Expected red: `A_status_id_in_the_file_that_is_not_digits_stops_a_rerun_and_is_never_shown` (it also ran red before the fix).
