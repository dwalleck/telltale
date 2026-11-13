---
id: xt-m9
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
          (fieldId, fromOption) => fromOption ? null : SettingRules.CustomFieldIdProblem(fieldId, example);
replace: |-
          (fieldId, fromOption) => fromOption || fieldId.Length > 0 ? null : SettingRules.CustomFieldIdProblem(fieldId, example);
---
# xt-m9: discover takes any text in the file as a field ID

Claim (cross-ticket finding 18, review findings 5 and 9): a field ID in the file that is not a custom field ID stops a rerun when no option replaces it, and is neither shown nor kept in a backup when one does.
Expected red: the sprintFieldId and storyPointFieldId cases of `A_bad_value_in_the_file_is_reported_as_the_files_and_not_as_an_option` and of `An_option_replaces_a_value_in_the_file_that_run_would_refuse_and_the_old_value_is_neither_shown_nor_kept` (they also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 4 failed of 111 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Re-pointed 2026-11-12 by fin (final fix pass): discover asks SettingRules.CustomFieldIdProblem (A6).
- Verified 2026-11-12 at 5c4f1d9: red, 4 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 4 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 4 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 4 failed of 114 (Pipeline, filter /*/*/DiscoverTests/*); restored.
