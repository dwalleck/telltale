# 02: Which visits count

**What to build:** The brief visit count is right at every edge of its rule. A visit is one unbroken stay in a listed status, measured in clock time over the issue's whole history; it is brief when it ended and was shorter than that status's threshold. An issue is counted for a sprint and a status when it is in that status's time in status row and one of its brief visits overlaps the time the row counts. The owner can trust that a count reflects how long issues really stayed, and not where a sprint boundary or a weekend fell.

Spec: `../spec.md` (owner decisions D59, D60, D61; the confirmed reading "Which visits count toward a sprint"; user stories 15 to 22; "Testing Decisions").

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] A stay just under the threshold counts; a stay exactly at the threshold does not; a stay just over does not
- [ ] A stay whose start and end are the same instant counts
- [ ] A stay the issue is still in is not counted, however short it is so far
- [ ] An issue with two brief visits to one status is counted once for that status
- [ ] An issue with brief visits to two listed statuses is counted once under each
- [ ] A long stay cut to seconds by the sprint's start or end is not counted
- [ ] A long stay cut to seconds by the issue joining or leaving the sprint is not counted
- [ ] A stay that spans a weekend or a holiday and is longer than the threshold by the clock is not counted, however little business time it holds
- [ ] A brief visit that falls entirely on a weekend, in an issue that is in the status's row through other time in the sprint, is handled as the overlap rule says, and a test states which way and why
- [ ] A brief visit made in an earlier sprint does not count the issue in a later sprint where its stay was long
- [ ] A brief visit to a status that is not listed gives no line and no row
- [ ] Two listed statuses with different thresholds are each judged by their own
- [ ] A fractional threshold (half a minute) is applied as written
- [ ] A daylight-saving change inside a stay does not change its length: clock time is the difference between the two instants
- [ ] The interval rule and the overlap rule are covered by pure-function tests; the cases above are also asserted through the report or `query`
- [ ] If the overlap rule cannot be applied to a case as the spec words it, the case is reported as a decision needed and not settled in code
- [ ] All fixtures are synthetic
