---
id: fin-m3
status: active
project: Pipeline
filter: '/*/*/LeavingOutSaysNothingOnTheConsoleTests/*'
file: src/Telltale.Core/Derive/Derivation.cs
expectedMatches: 1
find: |-
              Anomalies = anomalies,
replace: |-
              Anomalies = [.. anomalies, .. cycles.Where(cycle => cycle.Delivered && cycle.StartsWithBriefVisit && cycle.Cycle is null).Select(cycle => new IssueAnomaly(cycle.IssueId, ChangelogAnomalyKind.StatusHistory))],
---
# fin-m3: a delivered issue whose cycle was left out is recorded as a changelog anomaly

Claim: The anomaly count and the console output of `derive` and `run` are the same when cycle time leaves brief visits out as when it counts them (story 39; final review T2: only time in status had a test and a record, 02-m8). The test passed when it was first run, because leaving a cycle out never added an anomaly or a console line, so it could not run red first. The mutation is 02-m8's, for the issues cycle time leaves out.
