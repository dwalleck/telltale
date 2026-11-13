---
id: fin-m5
status: active
project: Pipeline
filter: '/*/*/(IncrementalSyncTests|QueryCommandTests)/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 2
occurrence: 2
find: |-
          var completeFrom = await RequireFilledUnderTheConfigurationAsync(cancellationToken);
replace: |-
          var completeFrom = await Task.FromResult<DateTimeOffset?>(null);
---
# fin-m5: report and query read a store filled under other collection settings

Claim (C4, D48): report and query refuse a store filled under other collection settings.
- Verified 2026-11-12 at 5c4f1d9: red, 7 failed of 48 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Re-pointed 2026-11-12 by fin (final fix pass): a step reads the watermark once and passes the date on (review finding 9).
- Verified 2026-11-12 at 741dd98: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Verified 2026-11-12 at a198e06: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Verified 2026-11-12 at 1830824: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
- Verified 2026-11-13 at d534f7f: red, 7 failed of 55 (Pipeline, filter /*/*/(IncrementalSyncTests|QueryCommandTests)/*); restored.
