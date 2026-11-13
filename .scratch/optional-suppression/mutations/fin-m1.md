---
id: fin-m1
status: active
project: Pipeline
filter: '/*/*/MinimumGroupSizeStalenessTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 1
find: |-
          if (derivedFrom.MinimumTeam != current.MinimumTeam
replace: |-
          if (derivedFrom.MinimumAssigneesBehindAFigure != current.MinimumAssigneesBehindAFigure
---
# fin-m1: a change of the team number alone is not refused as a change of minimumGroupSize

Claim (final review R2): derived data built under another team number is refused by report and query with the message that names minimumGroupSize, whatever the other number is. The guard existed before the tests that change one number alone, so they could not run red first. With this half gone the whole-record comparison (R5) still refuses, with its less exact message, so the tests of the message go red and the tests that nothing is shown do not.
