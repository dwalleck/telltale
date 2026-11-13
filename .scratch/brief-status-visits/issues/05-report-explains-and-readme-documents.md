# 05: The report explains brief visits, and the README documents the setting

**What to build:** A reader of a sprint report understands the brief visit lines without opening the configuration file: each names its status and its threshold, they come in board order, and the method note says what a brief visit is, that nothing was excluded or corrected because of one, and that the rule was added after the first run on real data. Someone setting the tool up can write the setting from the README alone. Brief visits never look like changelog anomalies, on the console or in the footer.

Spec: `../spec.md` ("The report", "Documents changed with the code"; user stories 10 to 13, 23 to 26 and 35).

**Blocked by:** 02, 03, 04

**Status:** resolved

- [x] Each footer line names the status and states its threshold in minutes
- [x] The lines come after the existing footer lines, in board order, the order of the time in status table
- [x] A shown count and a withheld count each have fixed wording, in the report's existing style
- [x] The method note has a paragraph on brief visits only when at least one status is listed. It says: a brief visit is one unbroken stay shorter than the status's threshold; it is measured in clock time; it is counted and no figure is excluded or corrected; a zero and a withheld count both read `suppressed`; the rule was added on 2026-11-12 after the first run on real data
- [x] With no status listed, the report has no brief visit lines and no extra method note text, and is identical to today's apart from the as-of time
- [x] A baseline sprint's counts appear when `report --sprint` is run on that sprint; the reported sprint's baseline columns gain nothing
- [x] The console output of `derive` and `run` is the same with and without brief visits in the data
- [x] The footer's changelog anomaly count is the same with and without brief visits in the data
- [x] The README's configuration example and "Setting formats" table include the setting, with its format and an example
- [x] The README's list of what is refused at load includes the refused forms of the setting
- [x] The README's "What a report shows" mentions the footer line, the `query` section lists the new view, and `derive` is listed as needed after a threshold change
- [x] Each README claim about the setting was checked against the built tool
- [x] The project handoff document has one dated line pointing at the spec
- [x] The wording of the lines and the method note paragraph is fixed by tests, with expected multi-line strings normalized to LF
- [x] Nothing in the documents or fixtures names a real site, status ID, sprint or configured threshold

## Comments

Resolved 2026-11-13. Merged into `spec/brief-status-visits` as 2b89cdd (source `ticket/bv-05-report-explains-and-readme-documents` at 93fa366; commits 7f6c07a, 3a31b87, e3996ad, d4608af, c00ac39, 88978d0, 15cbd13 and 93fa366). Root ran the merge: the merger subagent reviewed the diff, found nothing blocking, and stopped before merging because the branch changes `CLAUDE.md` and it had only an agent's word for the user's consent. The user chose that wording directly (bv-D13).

- Checks: the merged tree equals the tree of 93fa366, but the implementer's record is for 15cbd13 (one documents-only commit earlier), so the full check ran on the merged tree (record `merge-05-record.json`, base ab114bf): Core 153 passed in 0.9 s and Pipeline 947 passed in 44.6 s, 0 failed, 0 skipped, build with 0 warnings, format, line endings and the SQL line-endings check pass. The implementer's check on 15cbd13 (record `bv05-r1-record.json`) had the same totals.
- Mutations: six new records, 05-m1 to 05-m6, under `.scratch/brief-status-visits/mutations/`, red at 88978d0; 05-m1 was re-pointed after review fix F1 (same mutation). 01-m4 to 01-m10 and 03-m1 to 03-m8 were rerun red at 88978d0, because their test classes' fixtures moved to the harness (F10). None stale. `-ValidateOnly` passes for all three features at 15cbd13. None rerun at merge: no file differs from the source.
- Production change: `SprintReport` has the method note's last paragraph on brief visits, printed only when the derived data was built with a status listed, in a variant for the numbers in force, and prints a threshold in plain digits; `NumbersInForce.BriefVisitsChecked` (from `DerivedSource`) says whether a status was listed. No view, table or rule changed.
- Review (root's `code-review`, eleven findings, no verdicts, each with a disposition; all fixed, each checked against the diff by the merger subagent): F1 and F2 (the report learns whether a status was listed from the derived record; no optional parameter), F3 (bv-D11), F4 (the README gives what `discover` prints), F5 (bv-D13, made by root in 93fa366), F6 (bv-D14), F7, F8 (plain digits; `0.00001` printed as `1E-05` before), F9 (the comparison removes one line at the paragraph's place and one phrase, and fails if either is elsewhere), F10 (`Scenarios.CaughtUp` in the harness), F11 (cases for 0 and 3, and 5 and 0).
- User decisions, 2026-11-13: bv-D7 (the method note says a short median does not show how many issues had a brief visit); bv-D9 (the README says whoever edits the configuration file is trusted with it); bv-D10 (the paragraph accepted as written, in every variant); bv-D11 (the README's sentence on messages narrowed to the setting's own rules); bv-D12 (rounding recorded as D80); bv-D13 (`CLAUDE.md`'s parenthesis names D78 and D79 as recorded exceptions); bv-D14 ("The footer's two counts of the whole store" when a status is listed). `docs/decisions.md` has D71 to D80 for bv-D1 to bv-D9 and bv-D12.
- One existing assertion changed: the with-and-without comparison in `BriefVisitCountTests`, because the ticket requires a paragraph when a status is listed. Fixture-only: `CaughtUp` and `CaughtUpInSprint42` became `Scenarios.CaughtUp`, same dates and durations; no expected value moved.
- README claims were checked against a published build (logs in the source worktree's `.ipe-logs/bv05-claims/`); `discover` and `run` claims in process against the fake Jira.
- Left as it is: `query` prints a very small `threshold_minutes` with an exponent (`1E-05`), because its table printer writes every REAL that way; the report prints plain digits.
- `test-anti-patterns` on `BriefVisitsExplainedTests` (11 methods, 25 cases) and the changed assertion: no critical or high finding. Medium: the console test is long. Low: one paragraph check by a short fragment; the changed assertion transforms the report before comparing, with guards.
