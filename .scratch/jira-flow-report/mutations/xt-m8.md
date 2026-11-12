---
id: xt-m8
status: active
project: Pipeline
filter: '/*/*/DiscoverTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  if (jira["projectKeys"] is { } projectKeys && !(projectKeys is JsonArray keys && keys.All(key => JiraProjectKey.IsValid(TextOf(key)))))
replace: |-
  if (jira["projectKeys"] is { } projectKeys && !(projectKeys is JsonArray keys && keys.All(key => TextOf(key) is not null)))
---
# xt-m8: discover shows project keys of the file that are not project keys

Claim (cross-ticket finding 18, review finding 9): project keys in the file that run would refuse stop a rerun before anything is shown.
Expected red: `Project_keys_in_the_file_that_run_would_refuse_stop_a_rerun_and_are_never_shown(["FAKE", "synthetic-pasted-token"])` (it also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 111 (Pipeline, filter /*/*/DiscoverTests/*); restored.
