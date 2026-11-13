---
id: fin-m4
status: active
project: Pipeline
filter: '/*/*/IncrementalSyncTests/*'
file: src/Telltale.Pipeline/PipelineSteps.cs
expectedMatches: 2
occurrence: 1
find: |-
          var completeFrom = await RequireFilledUnderTheConfigurationAsync(cancellationToken);
replace: |-
          var completeFrom = await Task.FromResult<DateTimeOffset?>(null);
---
# fin-m4: derive rebuilds from a store filled under other collection settings

Claim (C4): derive refuses a store filled under another site, board, project list, start date or history field. The tests were written with the type they need (CollectionScope), so they could not run red first.
- Verified 2026-11-12 at 5c4f1d9: red, 6 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Re-pointed 2026-11-12 by fin (final fix pass): a step reads the watermark once and passes the date on (review finding 9).
- Verified 2026-11-12 at 741dd98: red, 6 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 6 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 6 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at bc92ab3: red, 6 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
