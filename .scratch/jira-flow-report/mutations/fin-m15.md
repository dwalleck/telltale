---
id: fin-m15
status: active
project: Pipeline
filter: '/*/*/PrivacyTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
              rebuildFile |= RebuildFileAfter.Contains(name);
replace: |-
              rebuildFile |= name.Length == 0;
---
# fin-m15: the file is not rebuilt after the issue-key migration

Claim (review finding 6): what an earlier version deleted is gone from the file after the migration. The test ran red first; this pins which line it depends on.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
- Verified 2026-11-13 at c7de38f: red, 1 failed of 7 (Pipeline, filter /*/*/PrivacyTests/*); restored.
