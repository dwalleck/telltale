---
id: xt-m11
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Cli/Commands/PipelineSteps.cs
expectedMatches: 1
find: |-
  if (config.StatusRoles.Keys.Any(statusId => statusId.Length == 0 || !statusId.All(char.IsAsciiDigit)))
replace: |-
  if (config.StatusRoles.Keys.Any(statusId => statusId.Length == 0))
---
# xt-m11: run accepts a status ID that is not digits

Claim (review finding 8): run refuses a status ID that is not digits, with a message that names the setting and not the value.
Expected red: `A_status_id_that_is_not_digits_is_refused_and_not_shown` (it also ran red before the fix).
