# 13: First real run on the work laptop

**What to build:** The owner runs the tool against the real Jira for the first time, on the managed Windows laptop. This is where the assumptions made off the laptop are confirmed. Any difference from the documented Jira shapes is fixed by adding a synthetic fixture that reproduces it; no real Jira data is copied into the repo or off company systems.

**Blocked by:** 10, 11, 12 (10 added 2026-11-12: without paging the run stops on a board with more than about 100 issues)

**Status:** ready-for-human

- [ ] The .NET 11 SDK is installed on the work laptop, or the obstacle to installing it is recorded
- [ ] All tests pass on the work laptop
- [ ] `secrets set` stores the secrets in Credential Manager and the tool reads them back
- [ ] `discover` reaches Jira through the corporate proxy and writes the configuration file
- [ ] Confirmed or corrected: abandoned work is Done with a "Won't Do" resolution
- [ ] Confirmed or corrected: the status roles (New, Approved, Committed not started; Development, Review, QE work; Blocked blocked; Done, Shipped done)
- [ ] Confirmed or corrected: one Scrum board whose sprints do not overlap
- [ ] Confirmed: which JQL search endpoint the site accepts
- [ ] A backfill completes and a report is produced for the most recent completed sprint
- [ ] Team time zone and company holiday list are entered in the configuration
