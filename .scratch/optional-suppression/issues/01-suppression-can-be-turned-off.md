# 01: Suppression can be turned off, end to end

**What to build:** The owner adds the `minimumGroupSize` setting to the configuration file with a team number and an assignees-behind-a-figure number. With both at zero, a sprint that the minimum group size rule withheld is reported in full, in the sprint report and through `query`. With the setting absent the numbers are five and three and nothing changes. This is the tracer bullet: one scenario through configuration, the derive settings, the metric views and the report. Tickets 02 to 05 widen it.

The rule's definition changes here, so the record changes in the same commit: the new ADR, the glossary and the owner decisions list.

Spec: `../spec.md` (owner decisions D64, D65, D66, D68; "Configuration", "The views", "Documents changed with the code", and the owner's reason under "Further Notes").

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] A configuration file with `minimumGroupSize` giving two whole numbers of zero or more loads; a file without the setting loads and behaves as five and three
- [x] With both numbers zero, a synthetic sprint of four assignees shows its delivered count, its cycle time rows and its time in status rows, with figures, in the report
- [x] The same figures are returned by `query` against the metric views, and every view keeps the columns it has today
- [x] The two numbers are stated in one place that every metric view reads; no view states a number of its own
- [x] The numbers are recorded with the other derive settings: after the setting changes in either direction, `report` refuses and says to run `derive`, and no `backfill` is needed
- [x] With the setting absent, and with it written as five and three, the report and the `query` results for that sprint are identical to today's, apart from the as-of time
- [x] Every existing test passes unchanged, including the checks that no query recovers, bounds or tells zero from non-zero for a withheld delivered count
- [x] A new ADR amends ADR-0003: the numbers are defaults a deployment may change or set to zero; pseudonymization and the absence of per-person reports are unchanged; it gives the date, the alternatives considered, the owner's reason as the spec records it, and says the reasoning covers Jira-derived figures. ADR-0003 gains a first line pointing to it and is otherwise unchanged
- [x] The glossary's "Minimum group size" entry says five and three are defaults and that configuration may set other numbers, zero turning a check off
- [x] The owner decisions list has D64 to D69 in its "Suppression" section, with a sentence that the earlier decisions describe the rule at its default numbers and apply at whatever numbers are configured
- [x] The commit message gives the reason for the definition change and says it was made on 2026-11-12, after the first run on real data
- [x] All fixtures are synthetic

## Comments

Resolved 2026-11-13. Merged into `spec/optional-suppression` as e359e2c (source `ticket/os-01-suppression-can-be-turned-off` at f7fc75d; commits 1830824, c24e455, d534f7f, f7fc75d).

- Checks: the merged tree equals the source tree, so the implementer's full check on f7fc75d stands (record `os01-r1-record.json`, base 5931877): Core 100 and Pipeline 699 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge (99 files).
- Mutations: 4 new records under `.scratch/optional-suppression/mutations/`. 01-m1 (the configured numbers are ignored), 01-m3 (the team check reads the other number) and 01-m4 (a bucket's figures are checked against the team number) are active and red at d534f7f. 01-m2 is stale: review fix F9 removed the hand mapping it mutated. Earlier records re-pointed to `MinimumGroupSize.Default`, where five and three are now stated: 06-m4, 06-m5, 07-m14, each red after the move. The implementer reran 84 `jira-flow-report` records at d534f7f: 77 red, 7 already stale. None rerun at merge, because the merge changed no file relative to the source. The implementer records `-ValidateOnly` passing for both features at f7fc75d (jira-flow-report 275 active, 16 stale; optional-suppression 3 active, 1 stale).
- Review (root's `code-review`, ten findings, no CONFIRMED or PLAUSIBLE verdicts, so each has a disposition). Fixed here, each checked against the diff by the merger: F1 for negative numbers (refused with a message naming the file and the setting), F4 (`DerivedSource` states both numbers as positional members), F6 (view comments say the configured numbers, five and three by default), F7 (`TelltaleSettings` keeps one copy), F8 and F9 (the store reads the `run` row straight into `DerivedSource`; no cast, no private row type), F10 (`Each_of_the_two_numbers_is_applied_to_its_own_check`: `{4, 2}`, `{4, 3}`, `{5, 2}`). Open on purpose, under the ticket breakdown the user approved: F1's cross-check (`assigneesBehindAFigure` greater than a non-zero `team`) and F3 (an explicit `null` loads as absent) are ticket 03's; F2 (the method note and the README still say five and three under other numbers) is ticket 04's and ticket 06's; F5 (`baseline_sprint` withholds a baseline sprint with no `sprint_team` row whatever the numbers are) is ticket 02's.
- Merger's read of 5931877..f7fc75d: the only SQL changed in a view is `minimum_group_size` (it selects `run.minimum_team` and `run.minimum_assignees_behind_a_figure` in place of the literals); six other views changed in comments only. `run` gained two integer columns and nothing else is stored. No assertion was removed or changed. The one existing test line changed is the `DerivedSource` construction in `CycleTimeTests` (line 448), which now names five and three because F4 made the members required. Fixtures are synthetic. No project, package or toolkit file changed. `test-anti-patterns` on the new tests (8 methods, 26 cases): no critical or high finding.
- Shape for tickets 02 to 05: `Telltale.Core.Configuration.MinimumGroupSize` (`Default` is the one place five and three are stated); `TelltaleConfig.MinimumGroupSize` is nullable and absent means the default; the rules on the numbers go in `TelltaleSettings.CheckedMinimumGroupSize`; a report or query reads the numbers from the `minimum_group_size` view; the staleness check is the last one in `PipelineSteps.RequireCurrentDerivedDataAsync`.
- With the user, not settled by this merge: (1) the pointer line in ADR-0003 is above the title, as the file's first line; (2) two intro sentences of `docs/decisions.md` were changed (when the decisions were made, and why D58 to D63 are missing) besides the sentence and rows the ticket names; (3) a store derived by an earlier build needs one `derive` after upgrading, because `run` gained two columns and the derived objects' version changed.
- Also for the user's eye: ADR-0008 gives a reason for rejecting each alternative, and says `query` as well as `report` refuses data derived under other numbers. The spec names the alternatives without reasons and names `report` only. The reasons follow from the spec's problem statement and out-of-scope list, and the `query` refusal is what the code does and a test pins.
