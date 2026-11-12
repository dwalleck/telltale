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
