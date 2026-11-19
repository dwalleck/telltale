# 04: Distinguish GitHub code approval from review

**What to build:** Show when a GitHub PR received its first human code approval independently of when it received its first code review.

**Blocked by:** 03 — Report an unlinked GitHub PR across sprints.

**Status:** ready-for-agent

- [ ] Use submission time for comment-only reviews, changes requested and approvals. A later dismissal preserves review time but is not evidence of approval. A first qualifying approval can end both waits.
- [ ] Exclude authors’ own activity, ordinary conversation, automation and configured Ops members. Track Ops approval separately. Disclose that classification uses current membership; if the roster cannot be read, do not emit affected timing figures.
- [ ] Demonstrate in a synthetic CLI run that first code approval is assigned to its own event sprint, includes the full wait from opening, and requires neither merge nor Jira linkage. Draft or reopen transitions do not restart the clock.
