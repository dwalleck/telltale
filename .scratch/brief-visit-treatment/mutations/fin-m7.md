---
id: fin-m7
status: active
project: Pipeline
filter: '/*/*/QueryRefusesTheStatusRowViewTests/*'
file: src/Telltale.Storage.Sqlite/SqliteStore.cs
expectedMatches: 1
find: |-
  CASE WHEN name IN @internalViews THEN 'internal view' ELSE type END
replace: |-
  CASE WHEN name IN @internalViews AND name <> 'sprint_status_row' THEN 'internal view' ELSE type END
---
# fin-m7: the internal view of the rows a table may have is recorded as a metric view

Claim: `query` refuses the internal view `sprint_status_row` (review of the final fix pass, F6). The test passed when it was first run, because every view a Derived script creates is recorded as internal, so it could not run red first. The mutation is that of brief-status-visits 03-m8 and of 02-m18, for this view.
