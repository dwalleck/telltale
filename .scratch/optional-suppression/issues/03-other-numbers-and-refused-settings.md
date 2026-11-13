# 03: Other numbers, and refused settings

**What to build:** The owner of a deployment sets the two numbers of `minimumGroupSize` to values other than the defaults and zero, and every existing suppression rule applies with those numbers in place of five and three. One check can be turned off while the other stays. A configuration file whose setting cannot be right is refused when it is loaded, with a message that names the file and the setting.

Spec: `../spec.md` (owner decisions D64, D65, D66; user stories 4 to 7 and 10; "Configuration"; "Testing Decisions").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] With a team minimum of four, a sprint of four assignees is reported and a sprint of three is withheld as a sprint under the minimum is today
- [ ] With two assignees behind a figure, a row with two assignees shows its figures and a row with one is withheld, keeping its issue count
- [ ] With the team minimum at zero and the per-figure number at its default, a small sprint is reported and a row with two assignees is still withheld
- [ ] With the per-figure number at zero and the team minimum at its default, a sprint under five is still withheld and, in a sprint that passes, every row shows its figures
- [ ] With numbers above the defaults, a sprint and a row that the defaults show are withheld
- [ ] The delivered-count rules (D49, D54) and the baseline rules (D44, D50, D56) are each exercised at a non-default number, so that nothing still compares against a fixed five or three
- [ ] One is not zero: with one assignee behind a figure, a row made only of unassigned issues and a data quality count of nothing are withheld
- [ ] The file is refused, with a message that names the file and the setting, when:
  - [ ] the setting is present and one of its two numbers is missing
  - [ ] a number is negative
  - [ ] a number is fractional
  - [ ] a number is quoted
  - [ ] a number is `null` or a boolean
  - [ ] the per-figure number is greater than a team minimum that is not zero
- [ ] A per-figure number greater than a team minimum of zero is accepted
- [ ] The setting, like every other, is refused when it contains a key the tool does not know
- [ ] Each case is asserted in the report and by `query`
- [ ] Expected multi-line strings are normalized to LF
- [ ] All fixtures are synthetic
