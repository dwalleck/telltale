---
id: fin-m6
status: active
project: Pipeline
filter: '/*/*/KeyThatIsNotTextTests/*'
file: src/Telltale.Cli/ConfigFile.cs
expectedMatches: 1
find: |-
  faults = IsUtf8(reader.ValueSpan) ? faults with { KeyIsNotText = true } : faults with { KeyIsNotUtf8 = true };
replace: |-
  faults = faults with { KeyIsNotText = true };
---
# fin-m6: a key that is not UTF-8 is called an escape for half of a character pair

Claim: a key holding bytes that are not UTF-8 is refused by every command with a message that is true of it, and not with the one about an escape (review of the final fix pass, F5). The test could not run red first: it was written after the two were told apart, in the same change as D110. The mutation is the state the review found.
