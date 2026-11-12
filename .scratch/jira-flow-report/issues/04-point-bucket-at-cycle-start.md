# 04: Point bucket from the estimate at cycle start

**What to build:** An issue's point bucket becomes its story point estimate at the moment its cycle started, so a later re-estimate cannot move it between buckets (ADR-0006). The collector fetches story point history alongside status history, and derive reconstructs the value in effect at cycle start.

Owner decision (2026-11-12): a delivered issue that never entered a work status has no cycle start; its point bucket is its estimate at the delivery moment (D35).

**Blocked by:** 03

**Status:** ready-for-agent

- [ ] An issue re-estimated after its cycle started stays in the bucket of its estimate at cycle start
- [ ] An issue re-estimated before its cycle started is in the bucket of the later estimate
- [ ] An issue with no estimate at cycle start is unestimated, even if estimated later
- [ ] An issue with no point history uses its current estimate
- [ ] Point-at-time reconstruction is covered by pure Core tests
