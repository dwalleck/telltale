---
id: xt-m11
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Core/Configuration/TelltaleSettings.cs
expectedMatches: 1
find: |-
              if (!statusRoles.Keys.All(SettingRules.IsJiraId))
replace: |-
              if (!statusRoles.Keys.All(statusId => statusId.Length > 0))
---
# xt-m11: run accepts a status ID that is not digits

Claim (review finding 8): run refuses a status ID that is not digits, with a message that names the setting and not the value.
Expected red: `A_status_id_that_is_not_digits_is_refused_and_not_shown` (it also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 1e8622b: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at bd15475: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 4af92da: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 2fa24b9: red, 1 failed of 52 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Re-pointed 2026-11-12 by fin (final fix pass): the configuration rules moved to TelltaleSettings.From (A1).
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 53 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 1268959: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
