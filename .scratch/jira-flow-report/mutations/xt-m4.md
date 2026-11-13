---
id: xt-m4
status: active
project: Pipeline
filter: '/*/*/TimeInStatusTableTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Migrations/0005_sprint_change_form.sql
expectedMatches: 1
find: |-
  WHERE field = 'sprint';
replace: |-
  WHERE field = 'sprint' AND 0;
---
# xt-m4: Sprint changes stored before they were normalised keep the form Jira wrote

Claim (review finding 2): the migration rewrites stored Sprint change values in the stored form of sprint IDs.
Expected red: `Sprint_changes_stored_as_jira_wrote_them_are_rewritten_in_the_stored_form_by_the_next_command` (it also ran red before the migration existed).
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 26 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 28 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
