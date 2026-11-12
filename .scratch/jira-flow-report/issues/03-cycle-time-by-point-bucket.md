# 03: Cycle time by point bucket

**What to build:** The report gains the cycle time table: for each point bucket, the issue count, and median and P85 cycle time in business days and in calendar days. Cycle time runs from the first entry into a work-role status to the last entry into a done-role status, for delivered issues. The point bucket is the issue's current estimate in this slice (ticket 04 changes it to the estimate at cycle start). The team time zone and holiday list come from configuration. Per-issue durations are computed in C# during derive; the views compute median and P85. See the spec's Metric rules.

Owner decisions (2026-11-12): a row's count is the number of cycle times behind it, and the column is headed "Issues with a cycle time" (D16). Calendar days are elapsed hours divided by 24 (D17). Days print to one decimal (D18).

**Blocked by:** 01

**Status:** resolved

- [x] Time in Blocked before any work-role status does not start the cycle
- [x] Time in Blocked after the cycle started counts toward cycle time
- [x] An issue that skips Development starts its cycle at its first entry into Review or QE
- [x] A move from Done to Shipped does not move the cycle end
- [x] A reopened issue's cycle runs to its final entry into Done, and its reopen count is recorded in derived data
- [x] An issue that went straight to Done is delivered and counted, but contributes no cycle time
- [x] A cycle spanning a weekend and a configured holiday shows fewer business days than calendar days, by the right amount
- [x] Median is the middle value or the mean of the two middle values; P85 is nearest rank; both are covered for one value, even counts and ties
- [x] Each table row shows its issue count, and issues with no estimate appear as unestimated
- [x] Cycle calculation is covered by pure Core tests as well as through the pipeline

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as 555d610 (source `ticket/03-cycle-time-by-point-bucket` at 46321d0).

- Checks: the merged tree equals the source tree, so the implementer's full check on 46321d0 stands (record `ticket03-merge-record.json`, base fd0e843): Core 66 and Pipeline 250 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 17 records (03-m1 to 03-m17). 15 are active and red at aa8bffb, on the tree that holds integration tip fd0e843; the two commits after it change only records. 03-m13 and 03-m15 are stale, with a reason: the guard they mutated in `Cycles.Measure` is gone since `Delivery.Of` reads the same entries. None rerun at merge, because the merge changed no file relative to the source. Eight records of other tickets follow lines this ticket changed and still mutate the same behaviour: 01-m1, 01-m2, 01-m3, 01-m25, 01-m31 and 02-m2 (`Delivery.cs`: `intervals` became `entries`), 02-m7 and 02-m8 (`Derivation.cs`: the `Abandoned` arm gained the reopen count); red at aa8bffb, 02-m2 at 16df4a5. 03-m10 expects 3 matches. All 110 records validate (108 active, 2 stale).
- Review: 9 findings. Fixed here: 1, 6, 7, 8, 9. Settled by an owner decision: 2 (D16), 3 (D17), 5 (D18). Deferred: 4 to ticket 06.
- D16: a row's count is the number of cycle times behind its figures, and the column is headed "Issues with a cycle time". A delivered issue that never entered a work-role status is in "Delivered issues" and in no row; a bucket delivered entirely that way has no row.
- D17: calendar days are elapsed time divided by 24 hours. Business days come from the business calendar (team time zone, weekends, `holidays`).
- D18: days print to one decimal. The bucket label is the estimate in round-trip form (`3`, `0.5`, `2.001`), and `unestimated` for no estimate.
- `Delivery.Of` now reads `StatusTimeline.Entries` instead of the status intervals: `Delivery.Of(currentStatusId, currentResolutionId, entries, roles, abandonedResolutionIds)`. The delivery moment is the last move into a done-role status, including a reopen closed again at the same instant, and a move into done at the instant of creation is a delivery. D8 and D9 hold and are pinned by `DeliveryTests` and by 01-m31 and 01-m26. `Cycles.Measure` and `Cycles.ReopenCount` read the same entries; a work status entered at the delivery moment gives a 0-day cycle.
- The cycle time view finds deliveries by `delivered = 1`, never by `cycle_end IS NOT NULL` (ticket 02's note). `The_cycle_time_view_reads_only_rows_marked_delivered_whatever_else_they_carry` tells the two apart at the store, since no pipeline run can (03-m16). An abandoned issue has a cycle end, a reopen count, and no cycle, bucket or delivery sprint.
- Median and P85 exist only in SQL (`cycle_time_per_sprint_and_bucket`), picked by integer rank; Core has no second implementation. `holidays` is a required setting and may be empty.
- Three cycle time tests that asserted the end of the report now assert the "Time in status" heading right after the last expected row, since ticket 05's section follows the table.
- Ticket 04 picks up: change the `PointBucket =` line of the `Delivered` arm in `Derivation.cs` to the estimate at `Cycle.Start`. Column and view already say `point_bucket`.
- Ticket 06 picks up finding 4: the view publishes figures for a bucket of any size. Wrap `cycle_time_per_sprint_and_bucket`; its `ranked` CTE is the place to join `issue` for assignee counts, `issue_count` stays visible (D5), and the report's `CycleTimeRow` figures must become nullable.
- Ticket 07 picks up: an issue delivered between sprints has a cycle time in `issue_cycle` with a null `delivery_sprint_id` and is in no row of the view. `reopen_count` is recorded for every issue, delivered or not, and no view reports it yet. The anomalies of D8 and D9 are still only "not delivered".
- For later tickets: `Derivation.Derive` builds each `IssueCycle` once per outcome arm; `IssueCycle` has a fifth constructor parameter `ReopenCount` and the `init` properties `Cycle` and `PointBucket`. `StatusTimeline` has a second positional member, `Entries`. Rules about transitions read the entries; rules about durations read the intervals.
