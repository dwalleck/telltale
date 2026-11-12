---
id: xt-m15
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Core/Configuration/JiraProjectKey.cs
expectedMatches: 1
find: |-
  throw new FormatException("every entry must be a Jira project key (an upper-case letter followed by upper-case letters, digits or underscores), and one is not.");
replace: |-
  throw new FormatException($"'{keys.First(key => !IsValid(key))}' is not a Jira project key.");
---
# xt-m15: run's message about a refused project key repeats the key

Claim (cross-ticket finding 21, owner decision D43): run's message about a project key it refuses names the setting and never the key.
Expected red: every case of `A_project_key_that_is_not_in_jiras_key_form_is_refused_before_any_search` (they also ran red before the fix).
- Verified 2026-11-12 at 6c34c20: red, 7 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 7 failed of 53 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
