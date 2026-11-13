---
id: fin-m13
status: active
project: Pipeline
filter: '/*/*/TimeInStatusTableTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/time_in_status_per_sprint.sql
expectedMatches: 1
find: |-
  AND CAST(f.status_id AS TEXT) = CAST(st.status_id AS TEXT)
replace: |-
  AND f.status_id + 0 = st.status_id || ''
---
# fin-m13: the status join compares a number with text

Claim (review finding 1): the time-in-status tests fail when the join between a status and its figures compares a number with text, which is what an unspecified affinity of a mixed key column could come to.
- Verified 2026-11-12 at 741dd98: red, 19 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 19 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 19 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 19 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 19 failed of 35 (Pipeline, filter /*/*/TimeInStatusTableTests/*); restored.
