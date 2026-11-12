---
id: xt-m7
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  if (TextOf(role) is not { } label || !StatusRole.TryParse(label, out _))
replace: |-
  if (TextOf(role) is null)
---
# xt-m7: discover lists a role in the file that is not one of the four

Claim (cross-ticket finding 18, review finding 9): a role in the file that is text but not one of the four roles stops a rerun before anything is shown, with a message that names the status and not the role.
Expected red: `A_role_in_the_file_that_run_would_refuse_stops_a_rerun_and_is_neither_replaced_nor_copied_nor_shown("finished")` (it also ran red before the fix).
