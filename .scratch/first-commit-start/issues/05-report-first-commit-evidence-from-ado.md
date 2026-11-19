# 05: Report first-commit evidence from ADO

**What to build:** Produce the same second reading from Azure DevOps alone or alongside GitHub, independently of reviews, merge and PR timing reports.

**Blocked by:** 02 — Report the first attributed GitHub PR commit; PR timing 05 — Report ADO review, approval, and merge.

**Status:** ready-for-agent

- [ ] Reuse read-only allowlisted ADO collection, existing secret handling, system proxy and trust. An ADO-only synthetic CLI run needs no GitHub credentials and attributes an unreviewed, unmerged PR by its unambiguous source-branch key.
- [ ] Read author timestamps from complete paginated commit histories, retain later evidence separately, and never report a partial minimum as the first timestamp.
- [ ] In a mixed-host fixture select by opening instant across hosts. Equal PR numbers on different hosts or repositories neither collide nor duplicate issue counts. Apply the same privacy and Jira non-regression checks as the GitHub path.
