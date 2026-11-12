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
