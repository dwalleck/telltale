# 02: Report the first attributed GitHub PR commit

**What to build:** Show that a delivered issue’s work was already underway before a brief first Development visit, without changing any Jira cycle measure. Reuse GitHub collection; no PR timing report is required.

**Blocked by:** PR timing 03 — Report an unlinked GitHub PR across sprints.

**Status:** ready-for-agent

- [ ] Write the defining synthetic CLI acceptance test: an issue created Tuesday has two branch-key PRs. A opens Tuesday with a Monday-noon author timestamp and is abandoned; B opens Wednesday and merges before Friday delivery. The first work-status entry is Thursday noon in the same UTC offset.
- [ ] Retain available evidence for both PRs. The protected report counts one covered issue, shows a −72 hour paired offset and flags the pre-creation timestamp without clamping it. Use author time from complete paginated commit history, not committer time.
- [ ] Later sync and rebuild neither duplicate the issue nor switch its timestamp to B. Jira-only figures match their original values under both brief-visit treatments. Use selected facts only; verify configured suppression through report and owner query.
