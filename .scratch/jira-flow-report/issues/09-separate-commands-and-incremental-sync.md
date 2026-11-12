# 09: Separate commands and incremental sync

**What to build:** `backfill`, `sync`, `derive` and `report` each work on their own, and `run` is sync then derive then report. `backfill` fetches everything from the configured start date. `sync` fetches only issues updated since the watermark, overlapping by a few minutes, and upserts by Jira's own IDs. `derive` drops and rebuilds all derived data from raw. `report` defaults to the most recently completed sprint and accepts a sprint ID.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] After a backfill, a sync picks up an issue changed in the fake Jira and the report reflects it
- [ ] A sync with nothing changed leaves the store and the report identical
- [ ] Running derive twice gives identical derived data
- [ ] Generating a completed sprint's report twice from the same data gives byte-identical files
- [ ] `report` with no argument reports the most recently completed sprint; with a sprint ID it reports that sprint
- [ ] The watermark advances only after a batch is fully written
