---
id: fin-m19
status: active
project: Pipeline
filter: '/*/*/BaselineTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/baseline_sprint.sql
expectedMatches: 1
find: |-
         COALESCE(t.delivered_suppressed, 1) AS suppressed
replace: |-
         COALESCE(t.suppressed, 1) AS suppressed
---
# fin-m19: only a baseline sprint under five withholds the whole baseline

Claim (owner decision D56): a baseline sprint whose delivered count is withheld for fewer than three delivering assignees withholds the whole baseline, as one under five does. The two cases ran red first.
- Verified 2026-11-12 at cf5da5b: red, 2 failed of 40 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 29 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
