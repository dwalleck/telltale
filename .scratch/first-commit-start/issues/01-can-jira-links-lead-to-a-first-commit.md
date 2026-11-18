# 01: Can Jira's links to pull requests lead to the first commit of an issue?

Type: research

**What to find out:** Whether the tool can get a better start for an issue's **cycle time** than the board gives, by asking Jira which pull requests belong to the issue and then asking the code host for those pull requests' commits. The earliest commit would be the start. This is the third **treatment** the brief visit treatment spec left for later (owner decision D88): replacing a start that is a brief visit with one from another source.

Nothing is built here. The answer decides whether a spec is worth writing and what it has to say. "Issue" means an issue of any type, as in cycle time: a sample is not limited to Story issues.

Background, from the owner (2026-11-17):

- Pull requests for the older sprints are mostly in Azure DevOps. For a few recent sprints one project is in Azure DevOps and one in GitHub. After that everything is in GitHub.
- Jira's integration is installed for both hosts, so an issue's development panel links to its pull requests on either.
- The owner does not expect Jira to hold the timings. The link is wanted only to know where to look.

What the tool has today: it collects issues, their status, sprint and point history, sprints and statuses from Jira, and nothing about pull requests, branches or commits. It keeps Jira's internal issue ID and not the issue key, so matching from the code host's side by a key in a branch name would need the key at import.

**Blocked by:** None (can start immediately).

**Status:** ready-for-human

The document review is done. The remaining checks need approved read access and repository allowlists on the work laptop, plus an administrator's retention answer. Access can be an approved CLI sign-in (see "Second route" below).

## Questions

1. **Jira.** Can the tool's Jira token ask for the pull requests linked to an issue, by internal issue ID, through the corporate proxy? What comes back for an issue linked to Azure DevOps and for one linked to GitHub: which fields, and is there enough to name the pull request to its host (repository and number)? Is the endpoint documented and supported, or internal? Can it be asked for many issues at once, and what are its limits?
2. **Coverage.** Of the delivered issues in the store, how many have at least one linked pull request? Look at the three periods apart: Azure DevOps only, both hosts, GitHub only. Look also at the delivered issues whose cycle starts with a brief visit, since those are the ones a new start would bring back.
3. **The hosts.** For a pull request Jira names, can each host give the list of its commits with their author dates? Does that still work after a squash merge, and after the source branch was deleted? Which read-only token scopes are needed, and do the calls pass the proxy?
4. **Is the date believable?** For the issues looked at, how does the earliest author date compare with the first move into a work status: earlier or later, and by how much? For an issue whose cycle starts with a brief visit, does the first commit fall where the work plainly began?
5. **Several pull requests.** How common is an issue with more than one, and does taking the earliest commit across them give a sensible start?
6. **What else is there for nothing.** The same pull request records may hold when it was opened, first reviewed and approved. Note what each host gives, for the elapsed time of a code review, without collecting it.
7. **After the migration.** Will Jira keep its links to Azure DevOps pull requests once that integration is removed, and will Azure DevOps keep the pull requests readable? This cannot be tried: ask the administrators and read the vendors' documents. If either answer is no, the links and the commit dates for the older sprints have to be collected before the migration ends.

## Rules for the work

- **Read only.** No call changes anything in Jira, Azure DevOps or GitHub.
- **Company data stays on company systems.** The work runs on the work laptop. Notes, responses and counts are kept there, outside this repository.
- **This repository is public.** No issue ID or key, pull request number, repository or project name, person, date of a real event or count from real data goes into this ticket, a commit or a test. The answer below is written in general terms: yes or no, the names of fields, and what a spec would have to handle.
- **Nothing about a person is recorded.** Pull request and commit records carry authors' and reviewers' names and addresses. The spike reads dates and identifiers only, and writes no name, address or account anywhere (ADR-0003).
- **Tokens** go in Windows Credential Manager and have the narrowest read-only scope that works. If using a CLI sign-in, check that the CLI uses approved protected storage and let it apply the credential itself. Never print or log tokens, pass them as command-line arguments, or put them in plaintext files. A signed-in identity may have broader access, so the spike sends GET requests only.
- Use the commit's author date, not its committer date: a rebase or amend can rewrite the committer date. Read commits from the pull request's own record, not from the main branch, where a squash merge leaves only the combined commit.
- If the commit belongs to the same work and its timestamp is right, work started no later than that commit. The elapsed time **from commit to delivery**, not the start timestamp, is the lower bound. The spike reports how the dates fall, not whether the measure is good enough.

## Done when

- [ ] Each of the seven questions has an answer under `## Answer`, in general terms, with how it was found
- [ ] Each answer says whether it was observed on the real systems, read in a vendor's documents, or told by an administrator
- [ ] The answer says plainly whether the route works for the Azure DevOps period, for the GitHub period, or for neither, and what would stop it
- [x] The answer lists what a spec for a first-commit start would have to decide: which commit date, several pull requests, issues with no linked pull request, how the figure is labelled as a lower bound, and whether it is a second reading or replaces leaving out (the pilot plan says a second reading unless a dated commit there says otherwise)
- [ ] The answer says by when the older sprints' links and dates must be collected, if question 7 finds a deadline
- [ ] Nothing real is in the repository; the detailed notes are named by where they are kept on the work laptop

## Answer

**Partial; work-laptop validation still open.** *Observed* below refers to the read-only Jira and GitHub work-laptop spikes. *Documents* refers to public vendor references; *repo* refers to the tool's source. No administrator has confirmed retention or a cutoff.

**Verdict.** GitHub: Jira returned usable pull request references for some delivered issues, and GitHub documents a read-only commits API. GitHub commit author dates are readable on the work laptop. Both Jira Development links and direct GitHub branch/title matching yielded paired observations, including earlier commit dates for linked brief-start cases. The sample is too small to characterize a cohort-wide timing distribution. Coverage for a confirmed GitHub-only cohort is still unverified. Azure DevOps: the Jira-led route did not work for the delivered issues checked; Jira returned no Azure DevOps pull request links. Both hosts document PR listing with the source branch, so matching the Jira key there is a candidate that does not need Jira's development links. In the mixed period, the Jira-led route has the same gap on the Azure DevOps side. The GitHub routes have been compared in the exploratory sample; validate wider coverage before generalizing. Prioritize Azure DevOps access and retention while historical data may still be readable.

### 1. Jira

- **The call.** `GET rest/dev-status/latest/issue/summary?issueId=<internal ID>` answers with basic authentication (email and API token) through the proxy, by internal issue ID, so the issue key is not needed. *(observed)*
- **Its parts.** The summary gives, per kind of data (`pullrequest`, `branch`, `repository`, `build`, `deployment-environment`, and others), an `overall` count with `lastUpdated` and `state`, and a `byInstanceType` count per source. *(observed)*
- **The detail.** `GET rest/dev-status/latest/issue/detail?issueId=<ID>&applicationType=<type>&dataType=pullrequest` gives the records. **`applicationType` must be the instance type the summary names** (for the GitHub app, `oAuth-com.github.integration.production`). The value `GitHub` that community answers give returns an empty list, with HTTP 200 and no error. *(observed)*
- **A pull request record holds:**
  - `id` (the number on the host) and `status` (for example `MERGED`);
  - `source.branch` and `destination.branch`;
  - `repositoryId`, `repositoryName` and `repositoryUrl`;
  - `url`, `lastUpdate`, `commentCount` and `ari`;
  - `author` and `reviewers`, each with a name and avatar and, for a reviewer, `approved`.

  That is enough to name the pull request to its host. It has **no created or opened time**. *(observed)*
- **Branch records** carry a `lastCommit` with `authorTimestamp`: the last commit only, not the first. *(observed)*
- **Persons.** The records carry author and reviewer names. A collector would have to read only IDs, branch names and dates, and drop the rest at the boundary (ADR-0003). *(observed)*
- **Status of the endpoint.** It is internal: "intended for internal use only", "not supported and they can change without any notice". Atlassian closed the request for a public API for Azure DevOps development information as "Timed out" in March 2025. The [public Development Information API](https://developer.atlassian.com/cloud/jira/software/rest/api-group-development-information/#api-rest-devinfo-0-10-repository-repositoryid-get) serves integrations and looks up recent data by repository ID, not pull requests by issue ID with the tool's token. *(documents: [JRACLOUD-86769](https://jira.atlassian.com/browse/JRACLOUD-86769))*
- **Batching and limits.** No supported bulk form or endpoint-specific quota was found. The spike asked one issue at a time with a short pause and met no limit; that does not establish one. If used, budget one summary per issue and detail per reported instance with PRs, and respect `429`/`Retry-After`. *(observed; documents: [Jira Cloud rate limiting](https://developer.atlassian.com/cloud/jira/platform/rate-limiting/))*

### 2. Coverage

- **Azure DevOps.** No delivered issue has development information from Azure DevOps. Their remote links (`rest/api/3/issue/<ID>/remotelink`) are Confluence pages only. Why the integration left no links is a question for the administrators: it may never have been connected to these repositories, or the keys were not in branch names, or it was connected after the work. *(observed)*
- **GitHub.** Every development link found came from the GitHub app. Some delivered issues have a linked pull request and many do not. The earlier Jira-only counts were recorded in `notes/first-commit-spike-2026-11-17.md`; that file was not found in the current work folder. The follow-up coverage and brief-start counts are in `notes/first-commit-github-linkage-comparison-expanded.md` on the work laptop. Atlassian documents using the key in the branch name for branches, commit message for commits, and PR title for pull requests; do not assume each of these yields a PR record. *(observed; documents: [Integrate with GitHub](https://support.atlassian.com/jira-cloud-administration/docs/integrate-with-github/))*
- **Not settled.** The exploratory sample includes brief-start coverage, but a confirmed migration-period roll-up and broader brief-start coverage are still open. Compare unique delivered issues with at least one Jira PR, at least one branch-key match, and at least one usable timestamp, against all delivered issues of any type in each period. In the mixed period report per host and deduplicate the combined result. Repeat for delivered issues flagged `starts_with_brief_visit`; keep deliveries between sprints separate rather than silently dropping them. *(proposed; repo: `issue_cycle.sql`)*

### 3. The hosts

| Host | Documented pull request commits read | Minimum read access |
| --- | --- | --- |
| GitHub | `GET /repos/{owner}/{repo}/pulls/{number}/commits`; `sha` and `commit.author.date` (not `commit.committer.date`). Page through the result; the endpoint caps the whole list at 250. The suggested [repository commits endpoint](https://docs.github.com/en/rest/commits/commits#list-commits) is not itself PR-scoped and needs **Contents: read**. Do not substitute branch history or claim a minimum from a truncated list. | Repository **Pull requests: read** on a fine-grained PAT or GitHub App token for the selected repositories. *(documents: [List commits on a pull request](https://docs.github.com/en/rest/pulls/pulls#list-commits-on-a-pull-request))* |
| Azure DevOps | `GET /{organization}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/commits?api-version=7.1` on `dev.azure.com`; `value[].commitId` and `value[].author.date` (not `committer.date`). The API documents `$top` and `continuationToken`. | PAT **Code (Read)** (`vso.code`) and repository read access; or a short-lived [Azure DevOps Entra token](https://learn.microsoft.com/en-us/azure/devops/cli/entra-tokens?view=azure-devops) under an identity with read access. *(documents: [Get Pull Request Commits](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-commits/get-pull-request-commits?view=azure-devops-rest-7.1))* |

Both APIs take a repository and PR number, not a live branch. The references do not guarantee that the full original commit set remains available after a squash or source-branch deletion. GitHub PR and commit GETs succeeded on the work laptop in the approved repositories. Sampled multi-commit PRs with squash-consistent merge topology still returned the original commits and their author dates; no deleted-source-branch example was confirmed in the bounded sample. The deletion case and Azure DevOps access still need validation. *(observed on GitHub; documents for the API contract)*

### 4. Is the date believable?

- **The first exploratory run skipped quartiles because the script required five paired dates.** That was a separate exploratory rule, not Telltale's group-size suppression; both configured group limits were zero. Individual offsets were later inspected on the work laptop. The follow-up rebuilt the derived tables offline from stored history with configured thresholds and cycle time set to `count`, preserving the untreated Jira start alongside the brief-start flag. It matched source-branch keys and considered PRs merged by delivery in the latest completed sprint windows in the snapshot; migration phase was not verified. That early run does not establish a cohort-wide direction. A rerun of the same sample excluded the catch-all Spike type by Jira type ID from both the matched count and the denominator. The original matcher wrongly rejected an underscore immediately after the issue number. A Jira-linked PR had the key in both its title and source branch. A corrected branch-only scan of the non-Spike brief-start cohort recovered the match; the cohort's paired timing sample remained too small to report. For the case that exposed the bug, GitHub returned the complete PR author dates, and the earliest preceded the untreated Jira start. This is a case check, not a cohort result. The follow-up below repeats the broader scan with the correction. The work-laptop note `notes/first-commit-github-non-spike-branch-boundary.md` supersedes the early brief-start branch results. Individual elapsed offsets are in `notes/first-commit-github-observed-offsets.md`, outside the checkout. *(observed)*
- **Lookup comparison on the same non-Spike cohort.** Within the approved repositories, Jira Development and the union of corrected GitHub branch and PR-title matching covered the same issues. PR titles added coverage beyond branch names for the full cohort, but not for brief starts. Jira named some PRs outside the approved repositories; those were not fetched. Equal issue coverage does not establish that the routes found the same PRs or that every matched PR belongs to the work. *(observed)*
- **Timing observations.** Deduplicating by repository and PR number and using complete author-date histories from eligible PRs merged by delivery yielded both earlier and later timestamps across all non-Spike deliveries. The usable brief-start pairs were earlier than Jira, but not every brief-start issue yielded a pair. Keep missing cases visible and do not infer a timing distribution from this sample. Coverage, overlap and observed elapsed offsets are in `notes/first-commit-github-linkage-comparison.md` on the work laptop. An owner-approved expansion of the repository list added no matching PRs for the same cohort and changed neither coverage nor paired offsets. Some Jira links remained outside the expanded scope and were not followed. The expanded comparison and local allowlist are in `notes/first-commit-github-linkage-comparison-expanded.md` and `notes/first-commit-github-repositories.txt` on the work laptop. *(observed)*
- For the full comparison, compute `earliest PR author date − first entry into a work-role status` on the same delivered issue (negative means earlier than Jira). Use the untreated board start: `cycle_start` can be null under `exclude` even when `starts_with_brief_visit` is set. Reconstruct via the existing timeline and cycle rules in memory or use a separate untreated derivation; do not compare against a missing start or change the live treatment. Keep issues with no work entry or no usable commit separate. *(repo: `Cycles.cs`, `issue_cycle.sql`; proposed measurement)*
- For brief-start cases, compare also to the end of the brief visit, the next sustained work stay if there is one, and delivery. Report the direction and spread of offsets and flag dates after delivery, implausibly early dates, and cases with no clear second work stay. Compare candidate dates against the work sequence; Jira and a commit both record events, not the true start. Git permits [overriding the author date](https://git-scm.com/docs/git-commit), and force-pushes can change which commits a PR contains. *(documents; proposed; no real-system result)*

### 5. Several pull requests

**Frequency and sensibility are unmeasured.** Several PRs do not disqualify an issue: by the owner's direction (2026-11-18), the first PR (opened earliest) gives the start, as its earliest author date. Count issues with multiple distinct PRs, deduplicating by host, repository and PR ID, including a PR found by both lookup routes. Count how often another matched PR holds an earlier author date than the first PR. Check whether the winner comes from initial work or a release backport, revert, follow-up, or stacked branch carrying another issue's commits. An earliest date is only as meaningful as the match and the PRs admitted; do not pick a winner without noting inaccessible or truncated candidates. *(proposed; no real-system result)*

### 6. What else the records give for nothing

- From Jira, a pull request has `status`, `lastUpdate`, `commentCount` and, per reviewer, `approved`. It has no opened, first-review or approval time. *(observed)*
- **GitHub:** the PR has `created_at`, `closed_at`, and `merged_at`. The separate reviews list returns chronological entries with `state` and `submitted_at`: first submitted review (skip pending) and first visible `APPROVED` are candidates, not a guarantee of first-ever approval if reviews were dismissed. Opening a draft is not the same as being ready for review. *(documents: [Pull requests](https://docs.github.com/en/rest/pulls/pulls#list-pull-requests), [List reviews](https://docs.github.com/en/rest/pulls/reviews#list-reviews-for-a-pull-request))*
- **Azure DevOps:** `creationDate` and `closedDate` are on the PR; close includes abandonment. The reviewer record has `vote`, not the time of the vote. The threads API documents comment `publishedDate` and shows a system `VoteUpdate` thread with `CodeReviewVoteResult`. That is a candidate for historical approval timing, not a guarantee of complete history or a submitted-review event. The first comment is not necessarily the first review. *(documents: [PR list](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-requests/get-pull-requests?view=azure-devops-rest-7.1), [Reviewers](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-reviewers/list?view=azure-devops-rest-7.1), [Threads](https://learn.microsoft.com/en-us/rest/api/azure/devops/git/pull-request-threads/list?view=azure-devops-rest-7.1))*
- This first-commit spike did not collect review timing. The separate [GitHub PR timing spike](../../github-pr-timing/issues/01-can-github-measure-pull-request-time.md) observed submitted reviews and approvals, and confirmed an explicit ready-for-review event in a spot check.

### 7. After the migration

- **Jira:** no vendor guarantee found for PR links after app uninstall or organization disconnect. [JRACLOUD-91672](https://jira.atlassian.com/browse/JRACLOUD-91672) documents app removal deleting deployment data only; it is not evidence about PRs. No Azure DevOps links were observed in Jira for the delivered cohort, so there is nothing there to rely on for the older sprints. Ask the Jira admin which integration and repositories were connected and whether display/API data survives disconnection. *(documents and earlier Jira observation; admin answer pending)*
- **Azure DevOps:** Microsoft says retiring a repo in place by renaming and locking its default branch preserves PR records and history, while deletion removes the repo from normal access; the 30-day restore window is not API read availability. GitHub Enterprise Importer can migrate PRs and Git history; a Git-only import is not a PR archive. Ask the migration admin which method is being used, whether the old repos and read permissions will be kept, and whether migrated PR commits and source refs can be read if the archive is the intended fallback. *(documents: [Azure repo deletion](https://learn.microsoft.com/en-us/azure/devops/repos/git/delete-existing-repo?view=azure-devops), [GitHub Enterprise Importer](https://docs.github.com/en/migrations/using-github-enterprise-importer/migrating-from-azure-devops-to-github-enterprise-cloud/about-migrations-from-azure-devops-to-github-enterprise-cloud), [Microsoft migration options](https://learn.microsoft.com/en-us/azure/devops/organizations/projects/migrate-public-project?view=azure-devops#migrate-repositories); admin answer pending)*
- **Deadline:** no date confirmed. Unless the admins confirm and the read-only check verifies a usable archive or migrated equivalent, collect the older sprints' issue-to-PR matches and author dates in a separately approved company-local archive **before the earliest deletion, decommissioning, or read-access cutoff**. The aggregate spike notes are not that archive. If cutover is the only known milestone, plan to finish before it rather than count on recovery.

### What a spec would have to decide

**Direction from the owner:** Jira is the starting source, not the final authority on when work happened. Enrich cycle time with commit evidence when Jira's brief first work visit misses work that demonstrably preceded it. This is a reason to pursue the second reading, not to rewrite Jira's recorded time in Development or change the pilot's primary measure.

- Use the earliest **author** date of the issue's first PR (opened earliest), not the committer or merge date; several PRs do not disqualify an issue (owner direction, 2026-11-18). Define which PRs count (including open, abandoned, release, stacked and post-delivery PRs), deduplication, and what happens on truncated or inaccessible history or suspect dates.
- Decide how the second reading handles no match, ambiguous matches and non-code work. The owner identified a dedicated Jira issue type used for catch-all support and miscellaneous work. Jira returns a structured type and the store already keeps `issue_type_id`. The filtered comparison excluded that type by ID from both the numerator and denominator and kept the unfiltered baseline. Whether to carry the exclusion into the commit-based reading is a spec decision; board-based metrics were not changed. Report coverage and missing reasons; don't silently mix board starts into a commit-based series. If using Jira, tolerate an internal endpoint's disappearance and discover the instance type from the summary, not a hard-coded label.
- Define how the Jira key is imported and used (D113), what can be displayed, and what limited historical evidence may be retained on company systems. Drop person fields at ingestion; apply the existing reporting suppression.
- Label the measure e.g. **first PR commit to delivery (lower bound on elapsed work-to-delivery time)**, with the attribution and date caveats. Keep it a **second reading**, not a replacement for leaving out brief starts, unless a dated change to the [pilot plan](../../../docs/context/pilot-plan.md) says otherwise (D88). Decide the population and whether Jira and commit-based results should also be compared on the same covered issues.

## Second route: the key in the branch name (proposed 2026-11-17)

The team's policy on both hosts puts the Jira key in the source branch. Matching on that key does not depend on Jira's development links. This is the viable Azure DevOps route to test, not yet an observed result.

### How

1. **List PRs in the owner-approved repositories** rather than searching per issue. GitHub: `GET /repos/{owner}/{repo}/pulls?state=all&per_page=100`, follow pagination links, source in `head.ref`. Azure DevOps: `GET /{org}/{project}/_apis/git/repositories/{repo}/pullrequests?searchCriteria.status=all&api-version=7.1`, page with `$top` and `$skip`, source in `sourceRefName`. *(documents: PR list links in question 6)*
2. **Match whole keys** against the delivered-issue set, ignoring case. Accept separators after the numeric part, including `-` and `_` as in `feature/<key>-thing` and `feature/<key>_thing`. Reject alphanumeric continuations so prefixes of longer keys do not match. Treat multiple keys as ambiguous for the spike rather than silently choosing one.
3. **Read the matched PR's own commits** as in question 3. Use the earliest author date in a complete, usable result and record incomplete reads separately. Never substitute the target branch or assume squash/delete survival without the spot checks.

### Access

- **GitHub.** If using an approved interactive CLI sign-in, `gh api --method GET --paginate` can make the calls without giving the spike the token. The login may grant write-capable scopes; use GET only and confirm organization/SSO authorization and credential-manager storage (the [CLI can fall back to plaintext](https://cli.github.com/manual/gh_auth_login); do not use that fallback). Do not rely on a personal CLI sign-in for unattended collection.
- **Azure DevOps.** Use Code (Read) in Credential Manager or the Azure CLI's Entra sign-in. For example, `az rest --method get --resource 499b84ac-1321-427f-aa17-267ca6975798 --url <approved-Azure-DevOps-API-URL>` lets the CLI apply the token without printing or passing it as an argument. Check the system proxy and corporate root trust for each client; do not bypass TLS validation.
- The signed-in GitHub CLI worked on the approved repositories; Azure DevOps has not been checked. Use filtered in-memory results; no raw HTTP dumps, verbose auth logs, or issue-level exports for this spike.

### What the matching has to handle

- **The key.** The tool keeps the internal issue ID only, and Jira's search returns the key with every issue. The owner has agreed that the store may keep it (owner decision D113), so a collector can store it at import. The spike can still take the keys from Jira when it runs, and needs no change to the store. Keys stay out of this repository, and what a report or view may show of them is for the spec to say.
- **Policy and practice.** A branch-key match without a Jira PR link establishes a linkage gap, not its cause. App repository coverage needs admin confirmation. No match is not proof of no PR: it can also mean a missing key or repository, inaccessible history, or a link made by a title rather than a branch.
- **Persons.** Branch names are used only in memory to extract keys; do not save them or the authors, emails or account fields returned by the hosts (ADR-0003).
- **After the migration.** The direct route needs readable PRs and source refs; a Git-only mirror does not establish either (question 7).

### Remaining validation

- **GitHub:** Expand to confirmed GitHub-only sprint windows. Verify a deleted-source-branch example, quantify multiple-PR effects, and spot-check that the earliest commit belongs to the issue's work. The approved repository list is already on the work laptop.
- **Azure DevOps:** Get approved local read access and a repository allowlist, test the direct branch-key route, and ask admins about retention and any cutoff. Confirm the sprint-to-period mapping for both hosts.
- Keep detailed outcomes on the work laptop. See `notes/github-timing-session-index.md` for the current results and which earlier files were superseded. Publish only general findings here.

### Coverage funnel (proposed 2026-11-18)

The exploratory runs applied several rules at once, so it is not known which of them removed the most issues. Jira's own links are thin before any rule: the Jira-only count on 2026-11-17 found fewer delivered issues with a linked pull request than with a linked branch or repository, and none in the Azure DevOps period. The funnel separates the two: what the sources hold, and what each of the spike's rules removes.

**Population.** Every delivered issue in the store, of every type, Spike included, grouped by delivery sprint and by period (Azure DevOps only, both hosts, GitHub only). Deliveries between sprints are counted in their own row, not dropped.

**Stages, in this order.** Each stage counts the issues still in after it. The rule's effect is the difference from the stage before.

1. **Any link or match, anywhere readable.** Counted per route and as their union:
   - Jira Development: a pull request record; a branch or repository but no pull request record (counted apart, as a linkage gap);
   - the key in a pull request's source branch;
   - the key in a pull request's title.

   Use every repository the identity can read on each host, not only the approved list. Use the corrected matcher (`-` and `_` after the key). Keys of issues outside the store's projects are counted apart, not as misses.
2. **In an approved repository.** Count Jira links that point outside the list apart: they are a scope choice, not missing data.
3. **Not Spike.** Excluded by issue type ID. Report the numbers before and after this stage side by side.
4. **Merged by delivery.** Count apart: merged after delivery, open, and closed without merge.
5. **A usable author date.** Count apart: a commit list that came back truncated, a read that failed, and a date after delivery.

**Several pull requests are not a stage.** By the owner's direction (2026-11-18), an issue with more than one pull request stays in, and its **first pull request**, the one opened earliest, gives the start: that pull request's earliest author date. Deduplicate by host, repository and number, so a pull request found by more than one route counts once. The run counts, without removing them, the issues with several pull requests, and among those, how often another pull request holds an earlier author date than the first one (a stacked branch or a branch cut from older work can). The one-pull-request rule belonged to the paired comparison with Jira's Code Review time, which is the [PR timing research](../../github-pr-timing/issues/01-can-github-measure-pull-request-time.md), not this funnel.

**Also report:**
- the same funnel for the delivered issues whose cycle starts with a brief visit;
- the funnel with each rule switched off on its own, to show how much of the gap comes from each rule and how much from missing data;
- for issues with no match at stage 1, whatever can be told without guessing: a branch or repository link in Jira, or nothing at all.

**What it answers.** Whether low coverage comes from the sources (no link, no key in a branch or title, Azure DevOps not linked) or from the spike's own rules. It also gives the coverage a spec would start from under each choice of rule.

**How.** Read only, on the work laptop, and aggregates only: counts per stage, sprint and period, kept in the work folder. No keys, branch names, pull request numbers or person fields in the notes. The Azure DevOps rows wait for access; until then they are reported as not read, not as zero.