# 03: Other numbers, and refused settings

**What to build:** The owner of a deployment sets the two numbers of `minimumGroupSize` to values other than the defaults and zero, and every existing suppression rule applies with those numbers in place of five and three. One check can be turned off while the other stays. A configuration file whose setting cannot be right is refused when it is loaded, with a message that names the file and the setting.

Spec: `../spec.md` (owner decisions D64, D65, D66; user stories 4 to 7 and 10; "Configuration"; "Testing Decisions").

**Blocked by:** 01

**Status:** resolved

- [x] With a team minimum of four, a sprint of four assignees is reported and a sprint of three is withheld as a sprint under the minimum is today
- [x] With two assignees behind a figure, a row with two assignees shows its figures and a row with one is withheld, keeping its issue count
- [x] With the team minimum at zero and the per-figure number at its default, a small sprint is reported and a row with two assignees is still withheld
- [x] With the per-figure number at zero and the team minimum at its default, a sprint under five is still withheld and, in a sprint that passes, every row shows its figures
- [x] With numbers above the defaults, a sprint and a row that the defaults show are withheld
- [x] The delivered-count rules (D49, D54) and the baseline rules (D44, D50, D56) are each exercised at a non-default number, so that nothing still compares against a fixed five or three
- [x] One is not zero: with one assignee behind a figure, a row made only of unassigned issues and a data quality count of nothing are withheld
- [x] The file is refused, with a message that names the file and the setting, when:
  - [x] the setting is present and one of its two numbers is missing
  - [x] a number is negative
  - [x] a number is fractional
  - [x] a number is quoted
  - [x] a number is `null` or a boolean
- [x] A per-figure number greater than the team minimum is accepted and applied, whether the team minimum is zero or not: with a team minimum of three and four assignees behind a figure, a sprint of five shows a row with four assignees and withholds a row with three (owner, 2026-11-13; this replaces the refusal the ticket first listed)
- [x] The setting, like every other, is refused when it contains a key the tool does not know
- [x] Each case is asserted in the report and by `query`
- [x] Expected multi-line strings are normalized to LF
- [x] All fixtures are synthetic

## Comments

Resolved 2026-11-13. Merged into `spec/optional-suppression` as 56acbf0 (source `ticket/os-03-other-numbers-and-refused-settings` at 5d463bb; commits 1d46329, 98eac1e, the verdict commits d1ce5a4 and 5d463bb, and the merges of the integration tip e4a16c0 and b940323).

- Checks: the source held the integration tip 914d814 but not ticket 04's merge, so the merged tree differs from the tree of 5d463bb and the full check ran on it (record `merge-03-record.json`, base 6cb75f8): Core 105 passed in 1.0 s and Pipeline 792 passed in 28.6 s, 0 failed, 0 skipped, build with 0 warnings in 1.9 s, format, line endings and the SQL line-endings check pass. The implementer's check on 5d463bb (record `os03-r1-record.json`): Core 100, Pipeline 765, 0 failed. The merge had no conflict, and no test of this ticket met ticket 04's wording: its report assertions start at `Delivered issues` or end at the footer, so no expectation was changed at merge.
- Mutations: 13 new records, 03-m1 to 03-m13, under `.scratch/optional-suppression/mutations/`. 03-m1 to 03-m10 and 03-m13 are active and were red at 1d46329; 03-m13 was rerun red at 98eac1e (1 failed of 21, `RefusedMinimumGroupSizeTests`). 03-m11 and 03-m12 are stale by os-D6: the comparison of the two numbers they mutated was removed with its tests. 01-m1, 01-m3 and 01-m4 were rerun red at 98eac1e, because their filter names a class in which one assertion changed. At merge the 12 records whose files the merge changed relative to the source HEAD were rerun and are each red: 02-m14 (5 of 19), 04-m1 (6 of 27), 04-m2 (2 of 27), 04-m3 to 04-m5 and 04-m7 (1 of 27 each), 04-m6 (2 of 27), 04-m10 (11 of 68), 04-m11 (6 of 27), 04-m8 (1 of 5), 04-m9 (3 of 5). No `jira-flow-report` record was re-pointed, marked stale or rerun. The implementer records `-ValidateOnly` passing for both features.
- Production change: `MinimumGroupSizeConverter` (new, `Telltale.Cli`, registered in `ConfigFile.Options`) reads `minimumGroupSize` by hand and refuses, by the setting's name, anything but an object, a missing number, a number written twice, an unknown key, and a number not written as a plain whole number that fits an integer (a fraction, 5.0, 1e1, 3000000000, a quoted number, null, a boolean). `MinimumGroupSize.NumberRefused` is the one statement of "minimumGroupSize.<name> must be written as a whole number of zero or more.", used by the converter and by `TelltaleSettings` for a negative. No view's SQL changed; the rules already read the configured numbers (ticket 01), and this ticket's tests show it for each rule.
- os-D6 (user, 2026-11-13; changes D66): any two whole numbers of zero or more are accepted. The refusal of more assignees behind a figure than a non-zero team minimum was built first and then removed, with its message, its pipeline and core tests (`MinimumGroupSizeRulesTests`, a file this ticket added and deleted) and its two mutation records. The test the reworded ticket names is in `OtherMinimumGroupSizeNumbersTests`: three and four, a sprint of five, a row with four assignees shown and a row with three withheld, in the report and by `query`. No code, message, test or comment still states the cross-check; `docs/decisions.md` D66 keeps one sentence of history.
- Review (root's `code-review`, eight findings, no CONFIRMED or PLAUSIBLE verdicts, so each has a disposition). Fixed here in 98eac1e, each checked against the diff by the merger: F3 (the cross-check removed, os-D6), F4 (5.0, 1e1 and 3000000000 are refused cases, and the message says "must be written as"), F5 (the comments of `sprint_team` and `cycle_time_per_sprint_and_bucket` are true for any numbers, zero included; comment lines only), F6 (one statement of the message for both layers), F8 (the copied fixture and the repeated cases went with `MinimumGroupSizeRulesTests`). No change here: F1 (ticket 04's work, merged as 72397bd), F2 (the owner's decisions D64 to D69; ticket 06 finishes the `CLAUDE.md` wording), F7 (ADR-0008 and the spec chose a derive setting).
- Beyond the review: `MinimumGroupSizeConverter.Write` threw, and ticket 05's `ConfigFile.Written` serialises the default setting through the same options, so `discover`'s tests failed once the two tickets met at 914d814. `Write` now writes the two numbers under the names `Read` reads.
- One existing assertion changed, in ticket 01's `OptionalSuppressionTests`, the test of a negative number: the message now ends "must be written as a whole number of zero or more." (F4 and F6: a negative gets the shared message). The assertion is as strict as it was. No test on the merged tree expects the old message. The diff removes no other assertion.
- Merger's read of 914d814..5d463bb: 25 files, 1241 lines added, 6 removed. With the setting absent, or written as five and three, the converter gives the same two numbers as before and nothing else is on that path. A refused value is never put in a message; the message for an unknown key gives the key's name, as it must to name the setting. Fixtures are synthetic (`FAKE-` keys, fake sprints and people). No stray file, and no document outside the mutation records was edited.
- `test-anti-patterns` on `OtherMinimumGroupSizeNumbersTests` (12 methods, 15 cases) and `RefusedMinimumGroupSizeTests` (6 methods, 21 cases): no critical, high or medium finding. Low: the test that a team of zero accepts any number behind a figure is an acceptance test in the class of refusals, and after os-D6 it overlaps the three-and-four test; one parameterised baseline test branches on its argument's text to build its fixture.
