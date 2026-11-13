---
id: fin-m6
status: active
project: Pipeline
filter: '/*/*/IncrementalSyncTests/*'
file: src/Telltale.Core/Storage/Watermark.cs
expectedMatches: 1
find: |-
              .. SiteUrl != configured.SiteUrl ? ["jira.siteUrl"] : Array.Empty<string>(),
replace: |-
              .. SiteUrl.Length == 0 ? ["jira.siteUrl"] : Array.Empty<string>(),
---
# fin-m6: a changed site is not among the settings a refusal names

Claim (C5): what the store was filled from names the Jira site, and a changed site is refused naming jira.siteUrl.
- Verified 2026-11-12 at 5c4f1d9: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at 741dd98: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
- Verified 2026-11-12 at a198e06: red, 3 failed of 30 (Pipeline, filter /*/*/IncrementalSyncTests/*); restored.
