---
id: xt-m13
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  var fileHadNoRoles = statusRoles.Count == 0;
replace: |-
  var fileHadNoRoles = previous is null;
---
# xt-m13: only a discover with no file at all may write a file with no done role

Claim (review finding 4, owner decision D40): the discover that finds no roles in the file is the one that may write a file with no done role, whether or not a file was there.
Expected red: `A_hand_made_file_without_status_roles_is_completed_on_a_board_with_no_done_status` (it also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 111 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
