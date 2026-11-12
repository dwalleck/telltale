---
id: fin-m1
status: active
project: Pipeline
filter: '/*/*/StoreTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
          _readOnlyConnectionString = ReadOnlyConnectionString(databasePath);
replace: |-
          _readOnlyConnectionString = _connectionString;
---
# fin-m1: a query over the metric views opens the file with a connection that can write

Claim (B14): the view-query connection is read-only. `A_query_over_the_views_opens_the_file_read_only_so_it_never_creates_one` passed when written.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 25 (Pipeline, filter /*/*/StoreTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 25 (Pipeline, filter /*/*/StoreTests/*); restored.
