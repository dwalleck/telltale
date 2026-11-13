---
id: fin-m18
status: active
project: Pipeline
filter: '/*/*/SuppressionTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/time_in_status_per_sprint.sql
expectedMatches: 1
find: |-
    AND t.suppressed = 0
replace: |-
    AND t.delivered_suppressed = 0
---
# fin-m18: the time-in-status table follows the check of the delivered count

Claim (owner decision D55): the time-in-status counts of a sprint whose delivered count is withheld stay. The test ran red first on the method note; this pins the table.
- Verified 2026-11-12 at a198e06: red, 1 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
