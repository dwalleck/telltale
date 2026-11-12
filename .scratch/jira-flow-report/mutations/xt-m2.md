---
id: xt-m2
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  var hasDoneRole = await RequireRunWouldAcceptAsync(text, path, mayLackDoneRole: fileHadNoRoles, cancellationToken);
replace: |-
  var hasDoneRole = await RequireRunWouldAcceptAsync(text, path, mayLackDoneRole: true, cancellationToken);
---
# xt-m2: a rerun writes a file with no done role too

Claim (cross-ticket finding 20, owner decision D40): only the first discover writes a file in which no status has the done role; a rerun over such a file is still refused, and the file is neither rewritten nor copied.
The test `A_rerun_over_a_file_with_no_done_status_is_still_refused_and_the_file_is_neither_rewritten_nor_copied` passed when it was written, because it guards behaviour the change had to keep.
- Verified 2026-11-12 at 0c2c2c7: red, 1 failed of 107 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Re-pointed 2026-11-12 by xt (review of the cross-ticket fixes): the exemption is keyed on the file having had no roles (review finding 4); the mutation still exempts every discover.
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 111 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
