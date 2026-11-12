---
id: fin-m5
status: active
project: Pipeline
filter: '/*/*/(IncrementalSyncTests|QueryCommandTests)/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 2
occurrence: 2
find: |-
          await RequireFilledUnderTheConfigurationAsync(cancellationToken);
replace: |-
          await Task.CompletedTask;
---
# fin-m5: report and query read a store filled under other collection settings

Claim (C4, D48): report and query refuse a store filled under other collection settings.
- Verified 2026-11-12 at 5c4f1d9: red, 7 failed of 48 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
