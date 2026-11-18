---
id: fin-m1
status: active
project: Pipeline
filter: '/*/*/DiscoverKeyWrittenTwiceTests/*'
file: src/Telltale.Cli/Commands/DiscoverCommand.cs
expectedMatches: 1
find: |-
  RequireReadableBriefVisitTreatment(text, path);
replace: |-
  _ = text;
---
# fin-m1: with a key written twice, the treatment is not read first

Claim: when a key is written twice in another setting and `briefVisitTreatment` has a fault its own reader finds, `discover` reports the treatment, in `run`'s words (D104; ticket 05, "With the user", item 2; final review T13). The test passed when it was first run, because ticket 05's review fix built that order (D105), so it could not run red first. The mutation takes away the reading of the setting before the message about the key written twice.
- Verified 2026-11-17 at bb2c5b2: red, 2 failed of 13 (Pipeline, filter /*/*/DiscoverKeyWrittenTwiceTests/*); restored.
- Verified 2026-11-17 at 9a93374: red, 2 failed of 13 (Pipeline, filter /*/*/DiscoverKeyWrittenTwiceTests/*); restored.
