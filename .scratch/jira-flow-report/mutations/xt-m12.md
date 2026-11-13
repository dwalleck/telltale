---
id: xt-m12
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  keepPrevious: previous is not null && replacedUnshown.Count == 0,
replace: |-
  keepPrevious: previous is not null,
---
# xt-m12: the old file is kept as a backup though it held a value run would refuse

Claim (review finding 5): when an option replaces a value in the file that run would refuse, no backup of the old file is made.
Expected red: every case of `An_option_replaces_a_value_in_the_file_that_run_would_refuse_and_the_old_value_is_neither_shown_nor_kept` and `A_new_site_address_replaces_one_in_the_file_that_carries_a_password_and_the_old_file_is_not_kept` (they also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 7 failed of 111 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 7 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 7 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 7 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 7 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 7 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
