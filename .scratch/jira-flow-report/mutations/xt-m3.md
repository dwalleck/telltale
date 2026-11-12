---
id: xt-m3
status: active
project: Pipeline
filter: '/*/*/StoreTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
          catch (SqliteException exception) when (NamesSomethingMissing(exception))
replace: |-
          catch (SqliteException exception) when (NamesSomethingMissing(exception) || exception.SqliteErrorCode != 0)
---
# xt-m3: every database failure of a view query is reported as "run derive"

Claim (review finding 1): only a missing table, view or column maps to the "run derive" message; a damaged, locked or unopenable store keeps the database's own error.
Expected red: `A_report_rendered_from_a_file_that_is_not_a_database_gets_the_databases_error_and_not_a_message_to_run_derive` (it also ran red before the fix).
