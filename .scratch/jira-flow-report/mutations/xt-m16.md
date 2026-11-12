---
id: xt-m16
status: active
project: Pipeline
filter: '/*/*/StoreTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
  foreach (var (name, sql) in Scripts.InFolder("Migrations").Where(script => !applied.Contains(script.Name)))
replace: |-
  foreach (var (name, sql) in Scripts.InFolder("Migrations").Where(script => !applied.Contains(script.Name) && applied.All(done => string.CompareOrdinal(done, script.Name) < 0)))
---
# xt-m16: a migration numbered below one already applied is skipped

Claim: migrations are applied by name, each one that is not recorded, so a script numbered below one a store already applied still runs when it arrives (0004 of ticket 06 after 0005 of the cross-ticket fixes), and a gap in the numbers is accepted.
The test `A_migration_numbered_below_one_already_applied_is_still_applied_and_a_gap_in_the_numbers_is_accepted` passed when it was written: it records what the runner already did.
- Verified 2026-11-12 at 3b6c271: red, 1 failed of 7 (Pipeline, filter /*/*/StoreTests/*); restored.
- Verified 2026-11-12 at 1e8622b: red, 1 failed of 22 (Pipeline, filter /*/*/StoreTests/*); restored.
- Verified 2026-11-12 at 4af92da: red, 1 failed of 22 (Pipeline, filter /*/*/StoreTests/*); restored.
- Verified 2026-11-12 at 2fa24b9: red, 1 failed of 22 (Pipeline, filter /*/*/StoreTests/*); restored.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 25 (Pipeline, filter /*/*/StoreTests/*); restored.
