---
id: fin-m2
status: active
project: Pipeline
filter: '/*/*/TimeInStatusTableWithNoRowTests/*'
file: src/Telltale.Reporting/SprintReport.cs
expectedMatches: 1
find: |-
                  : "No issue of this sprint spent time in a status that is not done.\n");
replace: |-
                  : "No issue of this sprint spent time in a status.\n");
---
# fin-m2: the sentence for a time in status table with no row is another one under count

Claim: while brief visits are counted for time in status, a sprint with a team in which no issue spent time in a status that is not done says "No issue of this sprint spent time in a status that is not done.", with no status listed, with statuses listed, and with `timeInStatus` written as `count` (final review C2: the wording under `count` stays what it was). The three rows passed when they were first run, because the sentence is older than this feature and no test stated it, so they could not run red first.
