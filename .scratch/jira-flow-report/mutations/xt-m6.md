---
id: xt-m6
status: active
project: Pipeline
filter: '/*/*/CredentialManagerSecretStoreTests/*'
file: src/Telltale.Cli/Commands/SecretsCommand.cs
expectedMatches: 1
find: |-
          catch (StoredSecretUnusableException)
replace: |-
          catch (UserFacingException)
---
# xt-m6: a failed read of the stored salt is treated as an unusable salt

Claim (cross-ticket finding 2, review finding 7): only a stored salt that was read and is not usable text may be overwritten; a read that failed is reported as that, and nothing is stored.
Expected red: `A_failure_to_read_the_stored_salt_is_reported_as_that_and_nothing_is_stored` (it also ran red before the fix).
- Verified 2026-11-12 at 73ad3e6: red, 1 failed of 16 (Pipeline, filter /*/*/CredentialManagerSecretStoreTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 16 (Pipeline, filter /*/*/CredentialManagerSecretStoreTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 16 (Pipeline, filter /*/*/CredentialManagerSecretStoreTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 16 (Pipeline, filter /*/*/CredentialManagerSecretStoreTests/*); restored.
