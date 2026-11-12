# 14: `backfill` rebuilds the raw tables

**What to build:** An issue or sprint that was deleted in Jira, or moved off the board, stops being counted. `sync` only adds and updates rows, so such rows stay in the raw tables (ticket 01 review, finding 10). `backfill` becomes the way to clear them: a completed backfill leaves the raw tables holding exactly what Jira returned for the configured board and start date, and the next derive and report no longer count what is gone. A backfill that fails partway must not leave the store reporting fewer deliveries than before it started, and must still resume as ticket 10 requires. The method note says that figures can include work since removed from Jira until the next backfill.

**Blocked by:** 10

**Status:** ready-for-agent

- [ ] An issue deleted from the fake Jira is counted after a sync and no longer counted after a backfill
- [ ] A sprint removed from the board in the fake Jira is gone from the store after a backfill
- [ ] A backfill interrupted partway leaves every earlier report unchanged, and a rerun ends with the same store as an uninterrupted backfill
- [ ] Rows for issues still in Jira are identical before and after a backfill, including hashed person fields
- [ ] The watermark after a backfill is the same as after an uninterrupted first backfill
