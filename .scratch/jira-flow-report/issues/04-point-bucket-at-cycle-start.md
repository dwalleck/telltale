# 04: Point bucket from the estimate at cycle start

**What to build:** An issue's point bucket becomes its story point estimate at the moment its cycle started, so a later re-estimate cannot move it between buckets (ADR-0006). The collector fetches story point history alongside status history, and derive reconstructs the value in effect at cycle start.

Owner decision (2026-11-12): a delivered issue that never entered a work status has no cycle start; its point bucket is its estimate at the delivery moment (D35).

**Blocked by:** 03

**Status:** resolved

- [x] An issue re-estimated after its cycle started stays in the bucket of its estimate at cycle start
- [x] An issue re-estimated before its cycle started is in the bucket of the later estimate
- [x] An issue with no estimate at cycle start is unestimated, even if estimated later
- [x] An issue with no point history uses its current estimate
- [x] Point-at-time reconstruction is covered by pure Core tests

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as 02db48a (source `ticket/04-point-bucket-at-cycle-start` at 3d3fb2f).

- Checks: the merged tree equals the source tree, so the implementer's full check on 3d3fb2f stands (record `ticket04-record.json`, base a70ca47): Core 80 and Pipeline 406 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 16 records (04-m1 to 04-m16), all active and red: 04-m1, 04-m15 and 04-m16 at d54b468, the others at 2a5acd6. None rerun at merge, because the merge changed no file relative to the source. Four records of other tickets were re-pointed to lines this ticket changed, each with the same mutation, and rerun red at 2a5acd6: 03-m11, 03-m12 and 03-m17 (the status entry now carries its history ID) and 05-m3 (the `fieldIds` line; it still drops the Sprint field). All 171 records validate on the merged tree (168 edits checked, 3 records stale).
- Review: 9 findings. Fixed here: 1, 2, 4, 5, 7, 8, 9. Settled by an owner decision: 3 (D35). Deferred to ticket 10: 6 (the story point field adds entries to a changelog page while paging is unsupported).
- D35: a delivered issue that never entered a work status has no cycle start; its point bucket is its estimate at the delivery moment. `Delivered` carries the history ID of the last move into done, so a re-estimate recorded after that move at the same instant does not count. Such an issue is in no row of the cycle time view (D16); the bucket is in `issue_cycle.point_bucket`.
- Rule: `PointHistory.EstimateAt(moment, momentHistoryId, currentEstimate, changes)` gives what the last change before the moment set; before the first change, what that change replaced; with no history, the current estimate. At the moment's own timestamp the history order decides (finding 7): a re-estimate recorded before the move into work, or in the same changelog entry as it, counts; one recorded after it does not. For an issue created in a work status a re-estimate at the creation instant is ignored. Root confirmed both readings.
- Collector: the bulk changelog request also asks for `jira.storyPointFieldId`. Its items are stored as changes of field `points`, each side a finite, non-negative number in invariant round-trip form, or null for no estimate. The number is read from `from`/`to`, from `fromString`/`toString`, or both, as text or as a JSON number; when both places hold a value they must be the same number. Anything else stops the run before anything is stored, with a message that names the setting and quotes no value. No schema change.
- Watermark: what it covers now ends with `; history status,<sprintFieldId>,<storyPointFieldId>`. `sync` and `run` stop and ask for a backfill when either field ID changed, and on a store filled before this ticket (its watermark has no history part). The message of ticket 09 for `boardId`, `projectKeys` and `backfillStart` is unchanged and wins when those changed. `discover` does not read the watermark; a field ID it changes stops the next `sync`.
- Shared files changed: `StatusTimeline.cs` (`StatusEntry.HistoryId`), `Cycles.cs` (`Cycle.StartHistoryId`), `Delivery.cs` (`Delivered.HistoryId`), `ChangeOrdering.cs` (`CompareHistoryIds`), `Derivation.cs` (the `Delivered` arm ends in `.WithBucketFromHistory(...)`), `RawData.cs` (`ChangeFields.Points`). Six cases in `DeliveryTests.cs` (tickets 01 and 02) now state the history ID of the delivery; they assert what they did, and the ID as well. A test in another branch that compares `new Delivered(at)` as a whole value fails after merging this until it states the ID. Harness: `FakeIssue.Reestimated`, `FakeJira.PointHistoryAsNumbers`.
- Ticket 14 picks up: a backfill is upsert-only today (D4), so after `jira.storyPointFieldId` changes, the `points` rows fetched under the old field stay in the store beside the new ones until backfill rebuilds the raw tables. Until then the buckets of issues with history under both fields can be wrong after such a change.
- Left as it is, by root: a stored `points` value that is not a number (possible only by editing the database by hand) fails `derive` with a format error instead of a plain message. The comment in migration `0001_raw_tables.sql` still says the `change` table's field is 'status'.
- Ticket 13 must confirm on a real site, because nothing here ran against one:
  - How Jira writes a number field's history: whether the numbers are in `fromString`/`toString` with `from`/`to` empty, in `from`/`to`, or in both, and whether as text or as JSON numbers. Research note 05 marks it unverified. All of these are read.
  - The form of the number: a decimal comma or a thousands separator stops the run with "holds a value that is not a story point number this tool reads".
  - That a first estimate and a cleared estimate come as an empty or missing value, not as `0` or other text.
  - That a re-estimate made in the same edit as a status move shares that move's history ID.
