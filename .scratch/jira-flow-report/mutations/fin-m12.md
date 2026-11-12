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
