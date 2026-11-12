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
