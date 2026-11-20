# 07: Explain missing reviews at sprint close

**What to build:** Explain which PRs do not contribute a first code review, separately from the timing distribution, on GitHub and ADO.

**Blocked by:** 04 — Distinguish GitHub code approval from review; 05 — Report ADO review, approval, and merge.

**Status:** ready-for-agent

- [ ] Show still waiting versus closed without qualifying review at sprint close (run time for an active sprint). Distinguish automation-only and, on GitHub, Ops-only activity from no submitted activity. Explain overlapping counts if shown together.
- [ ] Distinguish successful empty history from failed, truncated or unclassifiable history. Withhold a wait if missing evidence could change its first event; still count an independently known merge. Never substitute a zero-duration sample.
- [ ] In synthetic CLI fixtures, a review after sprint close does not erase the fact that the PR was waiting at close. Verify both in the report and through the owner query command that the diagnostic counts are shown under the default minimum group size (owner decision D114).
