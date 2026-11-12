# 09: Separate commands and incremental sync

**What to build:** `backfill`, `sync`, `derive` and `report` each work on their own, and `run` is sync then derive then report. `backfill` fetches everything from the configured start date. `sync` fetches only issues updated since the watermark, overlapping by a few minutes, and upserts by Jira's own IDs. `derive` drops and rebuilds all derived data from raw. `report` defaults to the most recently completed sprint and accepts a sprint ID.

Owner decisions (2026-11-12): `sync` asks for issues updated since the watermark minus 48 hours, as a quoted JQL date; the overlap covers any Jira account time zone and search-index lag (D26). `sync` on an empty store fetches from the start date (D27). `run` without a sprint reports the most recently completed sprint; when none has completed it updates the store, says so and exits successfully (D28).

**Blocked by:** 01

**Status:** resolved

- [x] After a backfill, a sync picks up an issue changed in the fake Jira and the report reflects it
- [x] A sync with nothing changed leaves the store and the report identical
- [x] Running derive twice gives identical derived data
- [x] Generating a completed sprint's report twice from the same data gives byte-identical files
- [x] `report` with no argument reports the most recently completed sprint; with a sprint ID it reports that sprint
- [x] The watermark advances only after a batch is fully written

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as 76e7490 (source `ticket/09-separate-commands-incremental-sync` at 6148402).

- Checks: the merged tree equals the source tree, so the implementer's full check on 6148402 stands (record `ticket09-r2-record.json`, base c25e8ea): Core 66 and Pipeline 281 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 10 records (09-m1 to 09-m10), all active and red at 5ec1ef6, the tree that holds integration tip c25e8ea; the one commit after it changes only records. None rerun at merge, because the merge changed no file relative to the source. 05-m17 is re-pointed: the catch it mutated in `SprintReport.ReadTimeInStatusAsync` is gone, and it now mutates `return built` in `SqliteStore.ReadDerivedSourceAsync` (red at 5ec1ef6, 13 failed of 22). All 120 records validate (118 active, 2 stale).
- Review: 11 findings. Fixed here: 2, 3, 5, 6, 7, 9, 10, 11. Settled by an owner decision: 1 and 4 (D26), 8 (D28). Finding 3 is fixed with a digest of the raw data instead of a timestamp, accepted by root.
- The fixes for findings 2, 3, 5 and 9 were written before their tests ran, so those tests never ran red. 09-m5 (a sync trusts a watermark fetched under other settings), 09-m6 (report accepts derived data older than the raw data), 09-m7 (two commands share the store) and 09-m8 (derive and report create an empty database) are their proof.
- D26: `sync` asks for `updated >= "yyyy-MM-dd HH:mm"`, the watermark less 48 hours, in UTC, as a quoted JQL date. The overlap covers any Jira account time zone and search-index lag. The quoted bound is checked only against the fake Jira; research note 05 still marks it unverified against a real site.
- D27: `sync` on a store with no watermark fetches from `backfillStart`, as a backfill does.
- D28: `run` without `--sprint` reports the most recently completed sprint. When none has completed it updates the store, says no report was written and exits 0. `report` without `--sprint` fails in that case.
- Commands: `backfill`, `sync`, `derive`, `report`, each built with `PipelineCommand.Create`; `run` is sync, derive, report. `JiraCollector.BringUpToDateAsync(store, CollectionMode, ct)`.
- Watermark: migration `0003_watermark.sql` (ticket 05 has 0002), one row per collector: what it covers (board, project keys, start date) and the newest Jira `updated` fetched. `WriteRawAsync(batch, reached, ct)` writes it in the batch's transaction, together with ticket 02's deletion of out-of-scope issues. It is set, not raised. An out-of-scope issue does not move it. `sync` and `run` stop before any Jira call when `boardId`, `projectKeys` or `backfillStart` no longer match what the watermark covers; the owner runs `backfill`.
- `derive` stores a SHA-256 digest of the raw data it read (derived table `derived_source`). `report` refuses when there is none or when the raw tables now give another digest ("Run derive first."). The digest is a hash over rows that already hold only hashed person fields.
- One command at a time on a store: `StoreLock` holds the empty file `<database>.lock` open without sharing. `derive` and `report` refuse a missing database and create neither a database nor a lock file.
- Tests of other tickets changed: ticket 05's catch for a missing time in status view is removed, as root directed, and its test now runs `report` on such a store and expects "The store has no derived data yet. Run derive first." Ticket 01's `Sprints_stored_for_a_board_that_is_no_longer_the_configured_one_take_no_delivery` backfills and derives after the board ID is corrected (finding 2 refuses the sync) and no longer writes sprint 41's report. Ticket 03's `CycleTimeTests` call to `ReplaceDerivedAsync` passes a digest.
- Ticket 10 picks up: write each page with `WriteRawAsync(batch, reached)`; the search is ordered by `updated`, so a page's newest `updated` is how far that page reaches. The search still stops the run when a further page exists.
- Ticket 14 picks up: `CollectionMode.Backfill` is where the rebuild of the raw tables goes (D4). Because the watermark is set, not raised, an interrupted rebuild leaves the next sync fetching more, never less.
- For later tickets: a second `run` fetches only issues updated within 48 hours of the newest one stored, so a fixture issue added later with older dates needs `backfill`, and so does a change of `boardId`, `projectKeys` or `backfillStart` between runs. `PipelineSteps` is disposable (it holds the store lock); `ReportAsync(sprintId, needsSprint, ct)`. Harness: `PipelineHarness.RunAllAsync`, `ReadStoreContentAsync`, `ReadReportBytesAsync`; `FakeJira.AccountTimeZone` and `SearchAnswers`.
