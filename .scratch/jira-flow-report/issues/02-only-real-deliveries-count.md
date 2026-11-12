# 02: Only real deliveries count

**What to build:** Abandoned issues, epics and sub-tasks stop counting as delivered. An issue in a done-role status whose resolution is in the configured abandoned list is not delivered. Epics and sub-tasks are left out of collection or of every metric, so they appear in no figure.

Owner decisions (2026-11-12): `abandonedResolutionIds` is a required setting and may be empty (D12). Epics, sub-tasks and types above epic level are left out at collection, and when a sync sees that a stored issue has become one of them it deletes that issue's rows (D13, D14). An issue type with neither a hierarchy level nor a sub-task flag stops the run (D15).

**Blocked by:** 01

**Status:** resolved

- [x] An issue that is Done with an abandoned resolution is not in the delivered count
- [x] An issue that is Done with any other resolution, or none, is in the delivered count
- [x] Epics and sub-tasks in the fixtures change no figure in the report or the views
- [x] The abandoned resolution IDs come from configuration

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as ca4a290 (source `ticket/02-only-real-deliveries` at 6a27288).

- Checks: the merged tree equals the source tree, so the implementer's full check on 6a27288 stands (record `ticket02-r2-record.json`, base f1a619d): Core 50 and Pipeline 221 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 11 records (02-m1 to 02-m11), all red at b77a8cf, on the tree that holds integration tip f1a619d. None rerun at merge, because the merge changed no file relative to the source. Two records of other tickets follow lines this ticket changed and still mutate the same behaviour: 01-m26 (its line was split) and 05-m18 (its line gained a closing bracket); both red at b77a8cf. All 93 records validate.
- Review: 9 findings. Fixed here: 2, 3, 4, 6, 7, 8, 9. Settled by an owner decision: 1 (D13). Deferred: 5 to ticket 10.
- D12: `abandonedResolutionIds` is a required top-level setting and may be empty. A configuration without it is refused before any Jira call. Entries are trimmed, and an entry that is not digits is refused with a message that names the setting.
- D13: epics, sub-tasks and types above epic level are left out at collection: only the issue type and the ID of such an issue are read, nothing of it is stored and no history is fetched for it. When a sync sees that a stored issue has become one of them, `WriteRawAsync` deletes that issue's `issue` row and `change` rows, in the batch's transaction (`RawData.OutOfScopeIssueIds`).
- D14: hierarchy levels of 2 and higher are left out like epics.
- D15: an issue type with neither `subtask: true` nor a `hierarchyLevel` stops the run and nothing is stored. `subtask: true` decides on its own, with or without a level.
- Limit of D13: the delete happens when a sync sees the converted issue. An issue converted before `backfillStart` and never touched since is not seen once sync is incremental (ticket 10); it leaves at a `backfill` (D4, ticket 14).
- Ticket 10 picks up finding 5: out-of-scope issues are still fetched and count against the single search page; paging removes the failure. Do not add `hierarchyLevel` to the JQL, it is unverified on the target site.
- Ticket 03 picks up two things. An abandoned issue keeps its `cycle_end` in `issue_cycle` with `delivered = 0` and no delivery sprint, so deliveries are found by `delivered = 1`, never by `cycle_end IS NOT NULL`; 03 adds the test that tells the two apart. 03 also moves `Delivery.Of` to the timeline's entries (the delivery moment is the last move into a done-role status, including a same-instant reopen).
- With the owner, the spec is silent: an abandoned issue's time before done still counts in the time in status table (issue count and median), like any other sprint member's. `time_in_status_per_sprint` does not read `issue_cycle`. No test pins this.
- For later tickets: `Derivation.Derive(raw, boardId, roles, abandonedResolutionIds, calendar, asOf)` and `Delivery.Of(currentStatusId, currentResolutionId, intervals, roles, abandonedResolutionIds)`. `IssueOutcome` has a third case, `Abandoned`, so every switch over it needs an arm. Ticket 11 (`discover`) must write `abandonedResolutionIds`, even when empty. A raw search answer written by hand in a test needs `"hierarchyLevel": 0` in `issuetype`, or the run stops at the scope check. The fake Jira has `FakeIssue.OfType(...)` and `FakeIssueType`.
