# 08: Report mixed-host sprints and partial coverage

**What to build:** Show host-specific and combined sprint figures for PRs in configured repositories, making visible which hosts contributed.

**Blocked by:** 04 — Distinguish GitHub code approval from review; 05 — Report ADO review, approval, and merge; 06 — Report sprint timing distributions.

**Status:** resolved

- [x] Through the CLI, demonstrate a mixed sprint with unequal host sample sizes where pooling individual waits gives different results from averaging host statistics. Check the combined count, median and nearest-rank P85 for both clocks.
- [x] Distinguish hosts not configured or not read, failed reads, and successful reads with no observations. Label partial combined coverage and keep independently known figures available; never turn missing hosts into zero waits.
- [x] PR identity includes host, repository and PR number. Repeated records do not inflate totals and number collisions do not collapse distinct PRs.
- [x] Check in both report and owner query that combined and per-host figures are shown under the default minimum group size and with the checks disabled (owner decision D114). Do not expose individuals or imply shared repositories contain only team PRs.
