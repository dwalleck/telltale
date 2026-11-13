---
id: fin-m7
status: active
project: Pipeline
filter: '/*/*/IncrementalSyncTests/*'
file: src/Telltale.Core/Storage/Watermark.cs
expectedMatches: 1
find: |-
          if (SiteUrl.Length == 0)
replace: |-
          if (SiteUrl.Length < 0)
---
# fin-m7: a store that does not say what it was filled from gets the message about changed settings

Claim (C14): a watermark row from before the columns existed is refused with a message of its own.
- Verified 2026-11-12 at 5c4f1d9: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at cf5da5b: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
