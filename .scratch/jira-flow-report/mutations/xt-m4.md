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
