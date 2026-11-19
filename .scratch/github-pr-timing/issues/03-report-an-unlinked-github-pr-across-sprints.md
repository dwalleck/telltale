# 03: Report an unlinked GitHub PR across sprints

**What to build:** Make the existing cross-sprint CLI acceptance test pass through collection, store, metric view and report. The population is PRs in configured repositories, not Jira-linked issues.

**Blocked by:** None (can start immediately).

**Status:** ready-for-agent

- [ ] With optional GitHub configuration, collect read-only from allowlisted repositories, following pagination and refreshing older/open PRs. Use the system proxy and trust store; obtain secrets from the secret store. Without configuration make no PR requests and preserve Jira-only results.
- [ ] In the synthetic fixture, an unlinked PR opens in A and receives an Ops approval. Before merge, B shows one first code review from a later human comment-only review, four elapsed days after opening; there is no code approval or merge sample.
- [ ] On a subsequent sync C shows one merge eighteen elapsed days after opening. Re-running sync and derivation neither duplicates observations nor changes B’s wait.
- [ ] Persist only selected lifecycle and classified event facts and pseudonymized author evidence. Verify configured group-size suppression in the report and owner query; do not expose identities or raw payloads.
