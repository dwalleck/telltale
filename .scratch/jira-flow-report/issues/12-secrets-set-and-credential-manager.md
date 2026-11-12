# 12: `secrets set` and Windows Credential Manager

**What to build:** `secrets set` stores the Jira account email, the Jira API token and the hashing salt in Windows Credential Manager, behind the secret-store interface from ticket 01, and can generate the salt. On Windows the tool reads secrets from Credential Manager; elsewhere it uses environment variables. Secret values are never echoed, logged or written to a file. Carried over from the ticket 01 review: until this ticket lands, every platform reads secrets from environment variables, which `CLAUDE.md` does not allow in production on Windows. The Credential Manager implementation cannot be exercised off Windows, so its final check is by hand in ticket 13.

Owner decisions (2026-11-12): replacing a stored hashing salt needs `--replace` (D24). A secret value is typed without echo or piped with `--stdin`, never a command-line argument (D25). A typed control character refuses the value (D32).

**Blocked by:** 01

**Status:** resolved

- [x] `secrets set` prompts for or accepts each secret without echoing it, and can generate a random salt
- [x] On Windows the Credential Manager store is selected and environment variables are never read for secrets; on other systems the environment-variable store is
- [x] A missing secret produces a message naming which one and how to set it
- [x] No secret value appears in logs, the configuration file or the database
- [x] The solution still builds and all tests pass on a non-Windows machine

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as bcfe787 (source `ticket/12-secrets-set` at 97c8af6).

- Checks: the merged tree differs from the source tree (the integration tip had three docs-only commits the branch lacked), so the full check ran on the merge (tag `merge-12`): Core 47 and Pipeline 173 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass.
- Mutations: 30 records (12-m1 to 12-m30), all red in the ticket branch at e4d1358. None rerun at merge, because the merge changed no file a record edits.
- Review: findings 1-13, all fixed here. This also closes findings 22 and 35 of the ticket 01 review (the store is chosen by platform; on Windows no environment variable is read for a secret).
- D24: replacing a stored hashing salt needs `--replace`. Generating a salt while the database holds hashed people and the salt is missing also needs `--replace`; typing the original salt back does not.
- D25: a value is typed without echo or piped with `--stdin`, never a command-line argument. Input that is not a console is refused without `--stdin`.
- D32: a control character refuses the value, typed or piped.
- Waits for the hand check in ticket 13 (Windows only, not run here): `WindowsCredentialVault` against the real `advapi32` (read, write, a missing entry as error 1168, zeroing and freeing); `SystemConsoleInput` (that keys waiting after Enter show the rest of a multi-line paste, and whether a paste ending in CRLF gives one Enter or two); whether corporate policy answers error 1312, and so whether a DPAPI store is needed.
- For later tickets: a new `ISecretStore` must implement `HowToSet`; use `SecretInfo.Of(name)` in place of `EnvironmentSecretStore.VariableName`; the harness has `pipeline.Console` and `pipeline.SecretStore`.
