---
id: fin-m12
status: active
project: Pipeline
filter: '/*/*/ConfigurationAndSecretsTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 1
find: |-
          _saltAtHand = new Lazy<string?>(() => secrets?.HashingSalt ?? (environment.Secrets.Get(SecretName.HashingSalt) is { } salt && !string.IsNullOrWhiteSpace(salt) ? salt.Trim() : null));
replace: |-
          _saltAtHand = new Lazy<string?>(() => environment.Secrets.Get(SecretName.HashingSalt) is { } salt && !string.IsNullOrWhiteSpace(salt) ? salt.Trim() : null);
---
# fin-m12: derive and report read the salt from the secret store again

Claim (A3): a run reads each secret once. The test passed when written.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 53 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 55 (Pipeline, filter /*/*/ConfigurationAndSecretsTests/*); restored.
