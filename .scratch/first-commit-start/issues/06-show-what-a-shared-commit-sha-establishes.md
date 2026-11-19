# 06: Show what a shared commit SHA establishes

**What to build:** Follow Jira-linked commits through approved PR histories, including migrated history, and distinguish potential matches from independent attribution.

**Blocked by:** 03 — Resolve Jira keys and direct PR links without guessing; 05 — Report first-commit evidence from ADO.

**Status:** ready-for-agent

- [ ] With synthetic Jira, GitHub and ADO responses, distinguish a single SHA candidate, multiple candidates, a complete search with no match, and an incomplete or unreadable lookup. Search independently of branch names.
- [ ] Even a unique SHA-only candidate supplies no issue timestamp. If an independent key or direct PR link agrees, record corroboration without double counting. An alternative SHA candidate neither overrides a valid attribution nor silently selects a release PR.
- [ ] Expose only protected aggregate outcomes, never raw SHAs or PR identifiers in reports or owner queries. No conclusion about overwritten Jira links is required.
