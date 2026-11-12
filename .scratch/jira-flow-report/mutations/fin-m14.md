---
id: fin-m14
status: active
project: Pipeline
filter: '/*/*/QueryCommandTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
              raw.SQLITE_READ => database == "temp" || (string.IsNullOrEmpty(second) && first is not null && ((HashSet<string>)userData).Contains(first)),
replace: |-
              raw.SQLITE_READ => true,
---
# fin-m14: a query may read any table

Claim (review finding 3): allowing a recursive query grants no read; the refusals of a raw and of an internal object, also inside a recursive query, passed when written.
