# 02: Off means every rule is off

**What to build:** With both numbers of `minimumGroupSize` at zero, nothing at all is withheld for the size of the group behind it. Every figure and count that any suppression rule (owner decisions D5 to D56) or the data quality footer withholds today is shown with its correct value, in the report and through `query`. What the setting does not touch stays as it is: person fields are hashed, nothing new is stored, and no view is grouped by person.

Ticket 01 proved one scenario. This ticket covers the rest, and fixes any view that still withholds something by another route when a minimum is zero.

Spec: `../spec.md` (owner decisions D65, D68; user stories 14 to 24, 30, 31 and 33 to 35; "The views"; "Testing Decisions").

**Blocked by:** 01

**Status:** resolved

- [x] Each existing suppression scenario, repeated with both numbers zero, shows the figure that was withheld, with its correct value, in the report and by `query`:
  - [x] a point bucket with two assignees: median and P85
  - [x] a time in status row with two assignees: median and total
  - [x] a delivered count with two deliverers (D49), and that sprint's cycle time table with its bucket rows (D54)
  - [x] a baseline that includes a sprint under five assignees (D50)
  - [x] a baseline that includes a sprint whose delivered count was withheld (D56)
  - [x] a pooled baseline row whose member row was suppressed in one baseline sprint (D44)
  - [x] the data quality footer's two counts with too few assignees behind them
  - [x] a footer count of nothing, shown as `0`
  - [x] a row made only of issues with no assignee
- [x] A report written with suppression off, over a synthetic store built to trip every rule at once, does not contain the word `suppressed` in its tables or footer
- [x] With suppression off, neither the console nor the report names a baseline sprint as withholding the baseline
- [x] Figures that have no data behave as they do today: a bucket with no issues has no row, and a sprint with no window is still named and skipped
- [x] In every metric view, the `suppressed` flag is `0` in every row when suppression is off
- [x] The existing test that no account ID, display name, email or issue summary appears in the database file or the report passes with suppression off
- [x] The list of metric views and their columns is the same with suppression off as with the defaults, and no view has a person column
- [x] Every existing suppression test stays as it is and passes
- [x] Expected multi-line strings are normalized to LF
- [x] All fixtures are synthetic

## Comments

Resolved 2026-11-13. Merged into `spec/optional-suppression` as 63ee7ce (source `ticket/os-02-off-means-every-rule-is-off` at 23e8d9f; commits 593f541, 24ecb55, c7de38f, 8945c46, and the merges of the integration tip d21fb96 and 23e8d9f).

- Checks: the source lacked ticket 05's merge, so the merged tree differs from the source tree and the full check ran at merge (record `merge-02-record.json`, base 0fc1afa): Core 100 passed in 1 s and Pipeline 729 passed in 22.3 s, 0 failed, 0 skipped, 0 warnings, format, line endings and the SQL line-endings check pass. The implementer's check on 23e8d9f (record `os02-r1-record.json`, base fd03926) had Core 100 and Pipeline 718.
- Mutations: 14 new records, 02-m1 to 02-m14, under `.scratch/optional-suppression/mutations/`, each red at c7de38f against `SuppressionOffTests` (failed of 19: m1 9, m2 4, m3 4, m4 5, m5 8, m6 9, m7 2, m8 5, m9 5, m10 10, m11 5, m12 5, m13 5, m14 5). They put a literal five or three back into each check of `cycle_time_per_sprint_and_bucket`, `time_in_status_per_sprint`, `sprint_team` and `data_quality`, and mark or break the baseline routes. Five `jira-flow-report` records whose filter names `PrivacyTests` (01-m5, 01-m6, 01-m8, fin-m11, fin-m15) were rerun red at c7de38f, because that class's scan moved into a helper. At merge the two records whose files the merge changed relative to the source were rerun: 05-m1 red, 8 failed of 11; 05-m2 red, 2 failed of 11. The implementer records `-ValidateOnly` passing for both features at 23e8d9f.
- Record 02-m14 finds a line in `src/Telltale.Reporting/SprintReport.cs` (`var baseline = baselineSprintIds.Count == 0 ? ...`) that ticket 04 rewrites. Ticket 04's merge must re-point it to the same behaviour and rerun it red.
- No production change. Every check in the views already read `minimum_group_size` (ticket 01), so with both numbers zero every condition passes. The ticket's tests could not run red first; the mutation records are the sensitivity proof.
- Review (root's `code-review`, twelve findings, no CONFIRMED or PLAUSIBLE verdicts, so each has a disposition). Fixed here in c7de38f, each checked against the diff by the merger: F1 (the flags are found through `derived_object` and `pragma_table_info`, not a list of names), F2 (the fixture's comment says which rules the store trips and names the tests that have D44 and the footer's team check), F3 (`BaselineSql` has an outer `ORDER BY section, position`), F4 (`SUM(suppressed IS NOT 0)`, so a null flag counts as set), F5 (the scan is `PrivacyTests.AssertNothingForbiddenWasWrittenAsync`, called by both tests; the suppression-off test prints every metric view through `query` before the scan, which reads standard output), F6 (the nine metric view names are pinned; the comments say what the column-name and hash checks do not show), F7 (the column-equality half is gone; it is `OptionalSuppressionTests`'), F10 (a baseline ID the store does not hold, one not started and one still running are refused with both numbers zero), F11 (02-m10 for the team half of `delivered_suppressed`, 02-m11 to 02-m14 for the baseline routes). F8 and F9 (copied fixtures and helpers) go to the run's final fix pass. F12 (the store is derived several times) is no change.
- Ticket 01's review finding F5 (`COALESCE(t.delivered_suppressed, 1)` in `baseline_sprint`): the fallback cannot be reached through the commands, with any numbers, and the view is left as it is, failing closed. `sprint_team` has a row for every sprint window; `derive` refuses a baseline list that names a sprint without a window or one that is not completed before it builds anything; `report` and `query` read the views only when the derived data matches the current raw data and baseline list. Two tests pin the refusals with both numbers zero. No record mutates the fallback, because no test can reach it.
- Merger's read of fd03926..23e8d9f: 21 files, 1096 lines added, 1 removed. No file under `src/` changed. The one existing test file changed is `PrivacyTests.cs`: the body of `No_account_id_name_email_or_summary_reaches_the_database_the_report_or_the_console` after its exit-code assertion became `internal static AssertNothingForbiddenWasWrittenAsync`, with every line of it unchanged, and `AddScenario` went from private to internal (the one removed line). No assertion was removed or weakened. Fixtures are synthetic (`FAKE-` keys, fake sprints and people). The tests assert on tables, footer, view rows and console refusals; the method note is cut out by `TablesAndFooter` or matched only by its heading. In the default arm of the every-rule test two existing default-number messages are matched ("Baseline sprints 41 and 42 each have fewer than five assignees" on the console, and the report's "- Baseline sprints 41 and 42 each have their delivered count withheld"); ticket 04 keeps both at the default numbers.
- For ticket 04's merge: `A_sprint_in_which_nothing_was_delivered_and_one_no_issue_belonged_to_say_so_as_they_do_with_a_team` asserts that sprint 42's report, with suppression off, starts with its heading, a blank line and `Delivered issues: 0`. Ticket 04 puts a line directly under the heading when both numbers are zero, so that expected text needs the new line when ticket 04 merges.
- `test-anti-patterns` on the new class (17 methods, 19 cases) and the shared scan: no critical or high finding. Medium: several tests are long, with more than one command and assertion group (the house style of the pipeline seam). Low: the privacy test matches the delivered row of `delivered_per_sprint` by its last three cells only (`" | 2 | 0 | 0 |\n"`); the refused-baseline cases assert that `query` exits 1 without its message; `A_row_the_baseline_sprints_have_no_issue_in_still_reads_none` asserts the report only.
