---
id: fin-m4
status: active
project: Pipeline
filter: '/*/*/TreatmentWithNoStatusListedTests/*'
file: src/Telltale.Core/Configuration/TelltaleSettings.cs
expectedMatches: 1
find: |-
  if (thresholds.Count == 0 && BriefVisitTreatments.LeavesOut(written.TimeInStatus))
replace: |-
  if (thresholds.Count == 0)
---
# fin-m4: the setting is refused whenever no status is listed

Claim: `briefVisitTreatment` written with nothing left out (an empty object, or `count`) while no status is listed is accepted by `derive` and `report`, and gives the report and the store that no setting gives (story 3; final review T10: only a Core test of the record covered it). The test passed when it was first run, because ticket 02 built the setting to mean `count` when a part is absent and to refuse only `exclude` with no status listed, so it could not run red first.
- Verified 2026-11-17 at bb2c5b2: red, 4 failed of 4 (Pipeline, filter /*/*/TreatmentWithNoStatusListedTests/*); restored.
