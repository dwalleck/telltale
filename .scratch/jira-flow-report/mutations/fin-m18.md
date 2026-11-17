---
id: fin-m18
status: active
project: Pipeline
filter: '/*/*/SuppressionTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Derived/sprint_status_row.sql
expectedMatches: 1
find: |-
    AND t.team_suppressed = 0
replace: |-
    AND t.delivered_suppressed = 0
---
# fin-m18: the time-in-status table follows the check of the delivered count

Claim (owner decision D55): the time-in-status counts of a sprint whose delivered count is withheld stay. The test ran red first on the method note; this pins the table.
- Verified 2026-11-12 at a198e06: red, 1 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
- Re-pointed 2026-11-12 by fin (final fix pass): sprint_team names its team check team_suppressed (review round 4, finding 28).
- Verified 2026-11-12 at bc92ab3: red, 1 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 1 failed of 17 (Pipeline, filter /*/*/SuppressionTests/*); restored.
- Re-pointed 2026-11-17 by fin (final fix pass of the brief visit treatment spec): which sprint and status may have a time in status row is stated once, in the internal view sprint_status_row (cross-review X3, final review C3), which time_in_status_per_sprint and brief_visits_per_sprint both read. The line this record mutates moved there with its text unchanged; only the file is new, and the mutation is unchanged.
