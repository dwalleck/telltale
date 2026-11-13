---
id: fin-m2
status: active
project: Pipeline
filter: '/*/*/MinimumGroupSizeStalenessTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 1
find: |-
              || derivedFrom.MinimumAssigneesBehindAFigure != current.MinimumAssigneesBehindAFigure)
replace: |-
              || derivedFrom.MinimumTeam != current.MinimumTeam)
---
# fin-m2: a change of the number of assignees behind a figure alone is not refused as a change of minimumGroupSize

Claim (final review R2): derived data built under another number of assignees behind a figure is refused by report and query with the message that names minimumGroupSize, whatever the team number is. The guard existed before the tests that change one number alone, so they could not run red first. As in fin-m1, the whole-record comparison still refuses, and the tests of the message go red.
- Verified 2026-11-13 at b49bfeb: red, 2 failed of 8 (Pipeline, filter /*/*/MinimumGroupSizeStalenessTests/*); restored.
