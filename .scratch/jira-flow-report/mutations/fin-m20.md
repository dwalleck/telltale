---
id: fin-m20
status: active
project: Pipeline
filter: '/*/*/BaselineTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 2
occurrence: 2
find: |-
          await NameWithheldBaselineSprintsAsync(cancellationToken);
replace: |-
          await Task.CompletedTask;
---
# fin-m20: report on its own does not name a withheld baseline sprint on the console

Claim (review round 4, finding 27): report names a baseline sprint whose delivered count is withheld on the console, as derive does, and a run says it once. The two cases ran red first.
- Verified 2026-11-12 at bc92ab3: red, 2 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
