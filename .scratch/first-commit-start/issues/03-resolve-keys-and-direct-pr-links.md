# 03: Resolve Jira keys and direct PR links without guessing

**What to build:** Attribute a specific PR when a branch, title or direct Jira PR link identifies the issue; show conflicting evidence without assigning it.

**Blocked by:** 02 — Report the first attributed GitHub PR commit.

**Status:** ready-for-agent

- [ ] Demonstrate branch-only, title-only and direct-link-only attribution through the CLI. Resolve complete keys to imported issue IDs, including hyphen and underscore boundaries; never match a prefix of a longer issue number.
- [ ] Agreeing routes count once by host, repository and PR ID. Conflicting branch/title/direct-link evidence assigns neither issue, even if only one key resolves. Descriptions, commit messages and Jira repository or branch links alone do not assign a PR.
- [ ] A deleted source branch does not invalidate a match from the PR record. Distinguish outside-scope links from missing data without reading unapproved repositories. Do not persist prose or expose issue keys in results; apply suppression to route counts.
