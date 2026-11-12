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
