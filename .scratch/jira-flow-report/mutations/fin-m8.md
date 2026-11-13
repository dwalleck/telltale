---
id: fin-m8
status: active
project: Pipeline
filter: '/*/*/IncrementalSyncTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
                  reached.Scope.ProjectKeys,
replace: |-
                  ProjectKeys = reached.Scope.HistoryFields,
---
# fin-m8: the project keys column holds another setting

Claim (C14): each setting the store was filled from is kept in its own column.
- Verified 2026-11-12 at 5c4f1d9: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 29 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
