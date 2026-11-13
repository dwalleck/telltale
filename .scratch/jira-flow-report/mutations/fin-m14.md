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
- Verified 2026-11-12 at 741dd98: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 8 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
