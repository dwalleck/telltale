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
