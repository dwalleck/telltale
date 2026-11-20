# 03: Report an unlinked GitHub PR across sprints

**What to build:** Make the existing cross-sprint CLI acceptance test pass through collection, store, metric view and report. The population is PRs in configured repositories, not Jira-linked issues.

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] With optional GitHub configuration, collect read-only from allowlisted repositories, following pagination and refreshing older/open PRs. Use the system proxy and trust store; obtain secrets from the secret store. Without configuration make no PR requests and preserve Jira-only results.
- [x] In the synthetic fixture, an unlinked PR opens in A and receives an Ops approval. Before merge, B shows one first code review from a later human comment-only review, four elapsed days after opening; there is no code approval or merge sample.
- [x] On a subsequent sync C shows one merge eighteen elapsed days after opening. Re-running sync and derivation neither duplicates observations nor changes B’s wait.
- [x] Persist only selected lifecycle and classified event facts and pseudonymized author evidence. Verify in the report and owner query that PR figures are shown under the default minimum group size with a single PR author, while a Jira figure in the same run is still withheld (owner decision D114); do not expose identities or raw payloads.

## Comments

Resolved on synthetic fixtures only (2026-11-19). Nothing was run against a real GitHub host. A sync asks for a pull request's reviews again only when its list entry changed, which assumes a new review moves the pull request's `updated_at`; that is not checked on the enterprise host. The suppression checkbox was replaced by owner decision D114 while the ticket was built.

Checked against the real GitHub host afterwards with the runbook (`docs/runbooks/pr-timing-real-host-check.md`, answers recorded in commit `3d8e5bf`). On the sampled pull request a review moved `updated_at`, the token read the Ops team, the first read fit the rate limit for the pilot repository and start date, the second sync was faster, and hand-checked pull requests were counted in the sprints expected. Still open: `updated_at` was checked on one pull request, a wider backfill was not tested, and no rate-limited answer was seen.
