---
id: fin-m17
status: active
project: Pipeline
filter: '/*/*/BaselineTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/baseline_cycle_time_per_bucket.sql
expectedMatches: 1
find: |-
      WHERE f.scope = 'sprint' AND f.measure = 'cycle business' AND t.delivered_suppressed <> 0
replace: |-
      WHERE f.scope = 'sprint' AND f.measure = 'cycle business' AND t.delivered_suppressed < 0
---
# fin-m17: the buckets of a baseline sprint whose delivered count is withheld do not count as suppressed

Claim (owner decision D54; review round 2, finding 14): the baseline fails closed. A baseline sprint whose delivered count is withheld has no bucket rows of its own, and every bucket it has a cycle time in is withheld from the baseline. The test passed when written, because the row form of D53 still had the rows.
