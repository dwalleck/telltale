---
id: fin-m20
status: active
project: Pipeline
filter: '/*/*/BaselineTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 1
find: |-
          await NameWithheldBaselineSprintsAsync(numbers, cancellationToken);
replace: |-
          await Task.CompletedTask;
---
# fin-m20: report on its own does not name a withheld baseline sprint on the console

Claim (review round 4, finding 27): report names a baseline sprint whose delivered count is withheld on the console, as derive does, and a run says it once. The two cases ran red first.
- Verified 2026-11-12 at bc92ab3: red, 2 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 2 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 2 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Re-pointed 2026-11-13 in the final fix pass of optional-suppression (finding R7): the report step now passes the numbers it read once, so its call differs from derive's and is the only match; the behaviour mutated is the same.
- Verified 2026-11-13 at b49bfeb: red, 2 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
