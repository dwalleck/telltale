---
id: fin-m16
status: active
project: Pipeline
filter: '/*/*/SuppressionTests/*'
edits:
  - file: src/Telltale.Storage.Sqlite/Sql/Views/cycle_time_per_sprint_and_bucket.sql
    expectedMatches: 1
    find: |-
      WHERE t.delivered_suppressed = 0
    replace: |-
      WHERE t.suppressed = 0
  - file: src/Telltale.Storage.Sqlite/Sql/Views/cycle_time_per_sprint_and_bucket.sql
    expectedMatches: 1
    find: |-
      WHERE t.delivered_suppressed = 1;
    replace: |-
      WHERE t.suppressed = 1;
---
# fin-m16: a sprint whose delivered count is withheld shows its bucket rows

Claim (owner decision D54, replacing the row form of D53): a sprint with a team of five whose delivered count is withheld has one suppressed row in the cycle time view and no bucket rows, whatever was delivered. The three cases ran red first.
- Verified 2026-11-12 at a198e06: red, 3 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
