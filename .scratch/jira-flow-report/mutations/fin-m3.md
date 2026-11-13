---
id: fin-m3
status: active
project: Pipeline
filter: '/*/*/QueryCommandTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
              "INSERT INTO derived_object (name, kind) SELECT name, CASE WHEN name IN @internalViews THEN 'internal view' ELSE type END FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT IN @before;",
replace: |-
              "INSERT INTO derived_object (name, kind) SELECT name, CASE WHEN name IN @internalViews THEN type ELSE type END FROM sqlite_master WHERE type IN ('table', 'view') AND name NOT IN @before;",
---
# fin-m3: the internal views are recorded as metric views

Claim (C16): duration_sample and duration_figures hold one row per issue or apply no minimum group size, so they are not metric views and a query cannot read them. The two `Query_refuses_sql...` cases that name them, and the list of metric views in the refusal, passed when written.
- Verified 2026-11-12 at 5c4f1d9: red, 11 failed of 18 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
- Verified 2026-11-13 at 9483f62: red, 13 failed of 25 (Pipeline, filter /*/*/QueryCommandTests/*); restored.
