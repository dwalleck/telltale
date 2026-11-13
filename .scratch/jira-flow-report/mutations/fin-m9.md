---
id: fin-m9
status: active
project: Pipeline
filter: '/*/*/MethodNoteAndFooterTests/*'
file: src/Telltale.Storage.Sqlite/Sql/Views/data_quality.sql
expectedMatches: 1
find: |-
         CASE WHEN t.suppressed = 0 AND b.assignees >= g.assignees_behind_a_figure THEN b.issues END AS delivered_between_sprints_count,
replace: |-
         CASE WHEN t.suppressed = 0 THEN b.issues END AS delivered_between_sprints_count,
---
# fin-m9: the footer shows a count with fewer than three assignees behind it

Claim (B4): a footer count needs three distinct assignees among the issues it counts. The test was written together with the view.
- Verified 2026-11-12 at 5c4f1d9: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-12 at b966865: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
- Verified 2026-11-13 at b49bfeb: red, 2 failed of 20 (Pipeline, filter /*/*/MethodNoteAndFooterTests/*); restored.
