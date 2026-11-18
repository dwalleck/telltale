---
id: fin-m5
status: active
project: Pipeline
filter: '/*/*/DiscoverKeyWrittenTwiceTests/*'
file: src/Telltale.Cli/ConfigFile.cs
expectedMatches: 1
find: |-
  key = reader.GetString()!;
replace: |-
  key = Encoding.UTF8.GetString(reader.ValueSpan);
---
# fin-m5: keys are compared as they were typed

Claim: a key written once plainly and once with an escape for one of its letters is one key written twice, and `discover` says so without quoting it (D108; review of the final fix pass, F2). The test could not run red first: the walk has compared the keys' text, not their typed form, since it was written, and the test first written for this held no escape at all. The mutation compares the keys as typed.
