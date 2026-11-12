# 10: Collection that survives real Jira

**What to build:** The collector handles what a real Jira Cloud site does at scale (ADR-0004): issue search and the bulk changelog endpoint are paged until their tokens run out; the board's sprint list is paged until its last page (owner decision, 2026-11-12); duplicate history entries are removed by history ID; a rate-limited response is retried after the server's stated delay, with a bounded number of retries; changelog timestamps with `+0000` offsets and missing field IDs on old history are parsed; and a backfill interrupted partway resumes without duplicating data.

Owner decision (2026-11-12): a fetch is one batch. A run that fails midway stores nothing and leaves the watermark unchanged; "resumes" means running it again gives the same store as an uninterrupted run (D38).

**Blocked by:** 09

**Status:** ready-for-agent

- [ ] Issues and changelogs spread over several pages are all collected
- [ ] A board whose sprints span several pages has all of them collected
- [ ] The same history entry returned on two pages is stored once and changes no figure
- [ ] A 429 response followed by success completes the sync, waiting the server's retry-after value (through the fixed clock, not real time)
- [ ] Repeated 429s beyond the retry limit fail the run with a clear message and leave the watermark unchanged
- [ ] A backfill that fails midway and is rerun ends with the same store as an uninterrupted one
- [ ] Old-style history items with no field ID, and null from/to values, are handled
