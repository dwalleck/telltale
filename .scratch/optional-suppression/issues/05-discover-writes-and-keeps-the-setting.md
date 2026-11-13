# 05: `discover` writes and keeps the setting

**What to build:** Someone setting the tool up for a team sees the minimum group size in the configuration file `discover` writes, with its default numbers, so they know the rule exists and where to change it. An owner who has changed the numbers, or removed the setting, does not have that undone when `discover` rewrites the file.

Spec: `../spec.md` (owner decision D69; user stories 3, 8 and 9; "Configuration").

**Blocked by:** 01

**Status:** resolved

- [x] The first `discover` writes `minimumGroupSize` with a team number of five and an assignees-behind-a-figure number of three
- [x] The written file loads, and a report from it is identical to one from the same file without the setting
- [x] A `discover` rerun keeps numbers the owner changed, including both at zero
- [x] A rerun keeps the default numbers as written
- [x] A rerun on a file without the setting leaves it without the setting
- [x] A rerun still saves its backup of the file before rewriting it
- [x] `discover` never refuses, warns about or comments on numbers the loader accepts, whatever they are
- [x] A rerun on a file whose `minimumGroupSize` the loader refuses stops with the loader's message and leaves the file and its backups untouched, as for every other setting `run` would refuse (owner, 2026-11-13)
- [x] All fixtures are synthetic

## Comments

Resolved 2026-11-13. Merged into `spec/optional-suppression` as 556170e (source `ticket/os-05-discover-writes-and-keeps-the-setting` at 9ac7eca; commits 781aad2, 405b7c5, 6251a3b, 9ac7eca, and the merges of the integration tip 11e114e and e85feb7).

- Checks: the merged tree equals the source tree, so the implementer's full check on 9ac7eca stands (record `os05-r1-record.json`, base fd03926): Core 100 and Pipeline 710 passed, 0 failed, 0 skipped, 0 warnings, format, line endings and the SQL line-endings check pass. Line endings were checked again at merge (5 files).
- Mutations: 2 new records under `.scratch/optional-suppression/mutations/`. 05-m1 (a rerun writes the default numbers over the file's) is red at e85feb7, 8 failed of 11. 05-m2 (the first discover writes a team number of four) is red at e85feb7, 2 failed of 11; it was re-pointed after review fix F5 rewrote its line. None rerun at merge, because the merge changed no file relative to the source. The implementer records `-ValidateOnly` passing for both features at 9ac7eca, and no `jira-flow-report` record affected (the change only adds lines).
- Review (root's `code-review`, eight findings, no CONFIRMED or PLAUSIBLE verdicts, so each has a disposition). Fixed here in 6251a3b, each checked against the diff by the merger: F1 (the refused rerun must say "run would refuse it" and name `minimumGroupSize.team`), F2 (behaviour kept; `A_first_discover_that_completes_a_file_made_by_hand_leaves_it_without_the_setting` names the case), F5 (`ConfigFile.Written` serialises `MinimumGroupSize.Default` with the options the file is read by; no literal shape in `discover`), F7 (cited as owner decision D70), F8 (`DiscoverAsSetupAsync` asserts exit 0 and an empty error at each setup step). No change: F3 (a refused setting on a rerun is caught after Jira was called, as for holidays and `baselineSprintIds`; root reports it to the user as a possible follow-up) and F6 (the first-write message is fixed by existing tests; the README is ticket 06's). F4 (three copied test helpers) goes to the run's final fix pass.
- Owner decision os-D4 of 2026-11-13, cited as D70: `discover` keeps its rule of never writing a file `run` would refuse. A rerun on a file whose `minimumGroupSize` the loader refuses stops and leaves the file and its backups untouched; "never refuses, warns about or comments on" means numbers the loader accepts. No production change was needed for it; one test pins it. Ticket 06 writes D70 into `docs/decisions.md`.
- Merger's read of fd03926..9ac7eca: five files, 253 lines added, none removed. `DiscoverCommand` gains one hunk: under `previous is null` (no file existed) it sets `minimumGroupSize` from `MinimumGroupSize.Default`; a rerun starts from the file's own JSON, so it neither adds the setting nor changes numbers. `ConfigFile.Written` is internal and has that one caller. No existing test file changed, so no assertion was removed or changed. Fixtures are synthetic. No project, package, toolkit or document file changed. `test-anti-patterns` on the new class (8 methods, 11 cases): no critical, high or medium finding; low: two tests (`A_rerun_with_nothing_to_change_...`, `A_rerun_keeps_the_file_with_the_owners_numbers_as_its_backup_...`) assert the exit code but not an empty error.
