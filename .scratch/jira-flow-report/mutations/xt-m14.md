---
id: xt-m14
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Core/Workflow/StatusRole.cs
expectedMatches: 1
find: |-
  : throw new FormatException($"the role of status {pair.Key} is not one of: not started, work, blocked, done."))));
replace: |-
  : throw new FormatException($"'{pair.Value}' is not a status role."))));
---
# xt-m14: run's message about a refused status role repeats the role

Claim (cross-ticket finding 21, owner decision D43): run's message about a status role that is not one of the four names the status ID and never what was written as the role.
Expected red: `A_status_role_outside_the_four_roles_is_refused_and_the_status_is_named_not_the_role` (it also ran red before the fix).
- Verified 2026-11-12 at 6c34c20: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 53 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
