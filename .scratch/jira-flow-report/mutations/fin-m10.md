---
id: fin-m10
status: active
project: Pipeline
filter: '/*/*/BaselineTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 1
find: |-
          BaselineSprintRules.RequireWithinWhatWasFetched(starts, _settings.BaselineSprintIds, completeFrom);
replace: |-
          _ = starts.Count;
---
# fin-m10: a report does not ask the start-date rule of an unchanged baseline list

Claim (B7): report asks the before-range rule of the baseline sprints at every run, also when neither the list nor the raw data changed.
- Verified 2026-11-12 at 5c4f1d9: red, 1 failed of 38 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 1 failed of 38 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 1 failed of 39 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 1 failed of 40 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 1 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-12 at 1830824: red, 1 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
- Verified 2026-11-13 at d534f7f: red, 1 failed of 41 (Pipeline, filter /*/*/BaselineTests/*); restored.
