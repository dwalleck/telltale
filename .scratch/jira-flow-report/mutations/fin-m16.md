---
id: fin-m16
status: active
project: Pipeline
filter: '/*/*/SuppressionTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/cycle_time_per_sprint_and_bucket.sql
expectedMatches: 1
find: |-
  CASE WHEN d.suppressed = 0 THEN b.issue_count END AS issue_count,
replace: |-
  b.issue_count AS issue_count,
---
# fin-m16: the bucket counts of a sprint whose delivered count is withheld are shown

Claim (owner decision D53): where a sprint's delivered count is withheld for fewer than three delivering assignees, the issue counts of its point buckets are withheld too, in the view. The test ran red first.
- Verified 2026-11-12 at b966865: red, 1 failed of 14 (Pipeline, filter /*/*/SuppressionTests/*); restored.
