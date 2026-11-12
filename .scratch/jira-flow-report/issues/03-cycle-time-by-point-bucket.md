# 03: Cycle time by point bucket

**What to build:** The report gains the cycle time table: for each point bucket, the issue count, and median and P85 cycle time in business days and in calendar days. Cycle time runs from the first entry into a work-role status to the last entry into a done-role status, for delivered issues. The point bucket is the issue's current estimate in this slice (ticket 04 changes it to the estimate at cycle start). The team time zone and holiday list come from configuration. Per-issue durations are computed in C# during derive; the views compute median and P85. See the spec's Metric rules.

**Blocked by:** 01

**Status:** ready-for-agent

- [ ] Time in Blocked before any work-role status does not start the cycle
- [ ] Time in Blocked after the cycle started counts toward cycle time
- [ ] An issue that skips Development starts its cycle at its first entry into Review or QE
- [ ] A move from Done to Shipped does not move the cycle end
- [ ] A reopened issue's cycle runs to its final entry into Done, and its reopen count is recorded in derived data
- [ ] An issue that went straight to Done is delivered and counted, but contributes no cycle time
- [ ] A cycle spanning a weekend and a configured holiday shows fewer business days than calendar days, by the right amount
- [ ] Median is the middle value or the mean of the two middle values; P85 is nearest rank; both are covered for one value, even counts and ties
- [ ] Each table row shows its issue count, and issues with no estimate appear as unestimated
- [ ] Cycle calculation is covered by pure Core tests as well as through the pipeline
