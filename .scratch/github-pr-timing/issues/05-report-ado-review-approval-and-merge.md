# 05: Report ADO review, approval, and merge

**What to build:** Let an Azure DevOps-only installation report all three waits for allowlisted repositories, without GitHub credentials or Jira matches.

**Blocked by:** 03 — Report an unlinked GitHub PR across sprints.

**Status:** resolved

- [x] Collect read-only with pagination, system proxy and trust, and secret-store credentials. Include older and still-open PRs and fetch enough event history to establish the first occurrence. Sync and rebuild without duplicating samples.
- [x] In a synthetic CLI run, ignore a designated automation account by stable ID; a later non-author file-thread comment ends the review wait. A later timestamped positive vote, including approval with suggestions, ends the approval wait in its own sprint.
- [x] Attribute nonzero votes to the acting reviewer, not the system thread author; ignore self activity, general conversation and vote resets. Do not infer vote time from the current vote or apply a human Ops-group exclusion.
- [x] Only completion yields a merge sample. An abandoned PR retains qualifying pre-closure review evidence. Report protected aggregates without persisting comments or identifiable reviewer data.

## Comments

Resolved on synthetic fixtures only (2026-11-19). Nothing was run against a real Azure DevOps. The list, thread and vote shapes were taken from Microsoft's REST documentation as the implementer remembered it; the vote-thread properties (`CodeReviewThreadType`, `CodeReviewVoteResult`, `CodeReviewVotedByIdentity`) are not in the REST reference and need one look at a real vote thread before the figures are relied on. Whether old pull requests have vote threads at all is still open from ticket 02.

Checked against the real Azure DevOps afterwards with the runbook (`docs/runbooks/pr-timing-real-host-check.md`, answers recorded in commit `3d8e5bf`). The list fields and the API version held. The sampled vote threads had the shape the collector reads: `CodeReviewVoteResult`, `CodeReviewVotedByIdentity` as a key into `identities`, and `publishedDate`. A voted pull request closed in early January 2026 had vote threads. No thread was unreadable and no vote unclassifiable in the read. Still open: some text comments omit `isDeleted` (the collector takes a missing flag for not deleted), the vote time was not checked against an independent clock, nothing older than early January 2026 was checked, and no throttled answer was seen.
