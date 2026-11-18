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

A person has to give access to the two code hosts and name the repositories to look at. With those in place an agent can run the rest on the work laptop. Access can be a sign-in to each host's command-line tool rather than a stored token (see "Second route" below).

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
- **Tokens** go in Windows Credential Manager, never in a file or on a command line, and have the narrowest read-only scope that works. A token a signed-in command-line tool hands out on demand (`gh auth token`, `az account get-access-token`) is kept by that tool in the same place, and is never written anywhere by the spike. Such a token is usually broader than read-only, so the spike sends GET requests only.
- Use the commit's author date, not its commit date: a rebase or an amend rewrites the commit date. Read commits from the pull request's own record, not from the main branch, where a squash merge leaves one commit dated at the merge.
- A first commit is a lower bound on when work started. The spike does not judge whether that is good enough; it reports how the dates fall.

## Done when

- [ ] Each of the seven questions has an answer under `## Answer`, in general terms, with how it was found
- [ ] Each answer says whether it was observed on the real systems, read in a vendor's documents, or told by an administrator
- [ ] The answer says plainly whether the route works for the Azure DevOps period, for the GitHub period, or for neither, and what would stop it
- [ ] The answer lists what a spec for a first-commit start would have to decide: which commit date, several pull requests, issues with no linked pull request, how the figure is labelled as a lower bound, and whether it is a second reading or replaces leaving out (the pilot plan says a second reading unless a dated commit there says otherwise)
- [ ] The answer says by when the older sprints' links and dates must be collected, if question 7 finds a deadline
- [ ] Nothing real is in the repository; the detailed notes are named by where they are kept on the work laptop

## Answer

**Partial, 2026-11-17.** Questions 1 and 2 and the Jira side of 6 are answered; 3, 4 and 5 need access to the code hosts, and "Second route" below is the plan for them; 7 is open with the administrators. How each point was found is in brackets: *observed* means read-only calls to the real Jira site with the tool's own API token, over the delivered issues in the store, printing only HTTP statuses, field names and value types, and counts that stayed on the work laptop; *documents* means a vendor's public pages.

**The route, in short.** It can work for the GitHub period, but only for the issues that carry a link. It does not work for the Azure DevOps period: Jira holds no link to an Azure DevOps pull request for any of the team's delivered issues, through any route the spike found.

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
- **Status of the endpoint.** It is internal: "intended for internal use only", "not supported and they can change without any notice". Atlassian closed the request for a public API for Azure DevOps development information as "Timed out" in March 2025. *(documents: [JRACLOUD-86769](https://jira.atlassian.com/browse/JRACLOUD-86769))*
- **Not settled.** Whether it can be asked for many issues at once, and its rate limits. The spike asked one issue at a time with a short pause and met no limit.

### 2. Coverage

- **Azure DevOps.** No delivered issue has development information from Azure DevOps. Their remote links (`rest/api/3/issue/<ID>/remotelink`) are Confluence pages only. Why the integration left no links is a question for the administrators: it may never have been connected to these repositories, or the keys were not in branch names, or it was connected after the work. *(observed)*
- **GitHub.** Every branch, repository and pull request link comes from the GitHub app. Some delivered issues have a linked pull request and many do not. The counts by sprint are in the working folder on the work laptop, `notes/first-commit-spike-2026-11-17.md`. A link exists only where someone put the issue key in a branch name, a commit message or a pull request title. *(observed; the app's linking rule: documents)*
- **Not settled.** Coverage among issues whose cycle starts with a brief visit.

### 6. What else the records give for nothing (Jira side)

- From Jira, a pull request has `status`, `lastUpdate`, `commentCount` and, per reviewer, `approved`. It has no opened, first-review or approval time. *(observed)*
- Elapsed review time would have to come from the host itself. Jira gives which pull request to ask about, and that is all.

### 7. After the migration (documents only so far)

- No Atlassian page found says what happens to the development information on issues when the GitHub or Azure DevOps app, or an organization, is removed.
- For deployment data, a bug report says removing the app that sent it deletes it from Jira, with "no workaround": "The deployment information is lost forever". *(documents: [JRACLOUD-91672](https://jira.atlassian.com/browse/JRACLOUD-91672), deployments only)* Treat the GitHub links as liable to go the same way if that app is ever removed.
- For Azure DevOps there is nothing in Jira to keep. Whether its pull requests stay readable after the migration is still for the administrators.

### What a spec would also have to decide (in addition to the list under "Done when")

- **Issues with no linked pull request.** These are common enough that a first-commit start would not cover every issue. The team's policy already asks for the key in the branch name, so the gap is in Jira's links, in work with no pull request, or in branches that do not follow the policy. The second route below finds out which. Whatever is left is either shown, or closed by a change in how the team works, which would itself be a dated decision.
- **The issue key.** Matching from the host's side (a key in a branch name) needs the key. The tool stores only the internal issue ID today; the owner has agreed it may store the key (D113), and the spec says how it is collected and what, if anything, a report shows of it.
- **An internal endpoint.** It can change without notice, and the instance type must be read from the summary, not written into the code.

## Second route: the key in the branch name (proposed 2026-11-17)

The team's policy, on both hosts, is that a branch name holds the Jira key of its issue (the project key, a hyphen, a number). So the hosts can be asked directly, without Jira's links. This is the only route for the Azure DevOps period, where Jira holds none.

### How

1. **List every pull request of each repository**, open and closed, rather than searching per issue. That is one paged call per hundred pull requests:
   - GitHub: `GET /repos/{owner}/{repo}/pulls?state=all&per_page=100`, branch in `head.ref`.
   - Azure DevOps: `GET {org}/{project}/_apis/git/repositories/{repo}/pullrequests?searchCriteria.status=all`, branch in `sourceRefName`.
2. **Take the key from the branch name** with a pattern that ignores case and needs a boundary after the number, so `feature/<key>-thing` matches and a key does not match a longer one that starts with it.
3. **Read each matched pull request's own commits**:
   - GitHub: `GET /repos/{owner}/{repo}/pulls/{number}/commits` (at most 250).
   - Azure DevOps: `.../pullRequests/{id}/commits`.

   These are the pull request's commits, not the base branch's, and are kept after a squash merge and after the branch is deleted. Take the earliest **author** date.
4. **Note, without collecting, what the same records give for the elapsed time of a review** (question 6): when the pull request was created and closed on both hosts, and GitHub's review submission times (`/pulls/{number}/reviews`).

### Access

- **GitHub.** If a long-lived token cannot be had, a sign-in to the GitHub CLI is enough. `gh api --paginate` makes the calls itself, so the token never passes through the spike's code, and `gh auth token` gives it to a program that needs it. Four things come with it:
  - The token is the person's own and usually holds `repo` and `read:org`, which can write.
  - With SAML single sign-on the organization may need the token authorized first (`gh auth refresh`).
  - It ends with the sign-in.
  - That is fine for a spike or a run someone starts, and not for an unattended schedule.
- **Azure DevOps.** A personal access token with Code (Read), or a short-lived Entra token from a signed-in Azure CLI: `az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798` (Azure DevOps' resource ID).
- Neither has been tried yet, through the proxy or otherwise.

### What the matching has to handle

- **The key.** The tool keeps the internal issue ID only, and Jira's search returns the key with every issue. The owner has agreed that the store may keep it (owner decision D113), so a collector can store it at import. The spike can still take the keys from Jira when it runs, and needs no change to the store. Keys stay out of this repository, and what a report or view may show of them is for the spec to say.
- **Several pull requests for one key**: a follow-up, a pull request into a release branch, a cherry-pick, a revert. The earliest commit across them is the natural start. Whether pull requests into release branches count has to be decided.
- **A branch cut from another feature branch.** Its first commit can be older than the work. Reading the pull request's own commits, not the branch's history, mostly avoids this.
- **A rebase or a force push** can drop or reorder commits. The author date survives a rebase, and the commit date does not.
- **Persons.** Pull request and commit records hold names, addresses and accounts. The spike reads IDs, commit SHAs, branch names and dates only (ADR-0003).
- **Policy and practice.** Jira's links cover fewer delivered issues than the policy suggests. Matching over every repository tells apart three causes:
  - the GitHub app not having access to every repository;
  - work with no pull request;
  - branches that do not follow the policy.
- **After the migration.** The route works for the Azure DevOps period only while its pull requests stay readable (question 7).

### The spike to run

- **Read only, on the work laptop.** It lists the pull requests of the repositories the owner names, on both hosts. It matches their branch names to the keys of the store's delivered issues, fetched from Jira as it runs and not stored.
- **Aggregates only**, kept in the working folder:
  - per sprint and host, how many delivered issues have a matching pull request;
  - how many have more than one;
  - how many branches hold no key, or a key of no delivered issue;
  - how the earliest author date falls against the board's cycle start: earlier or later, by how much, and separately for the issues whose cycle starts with a brief visit.
- **What it answers:** questions 2 to 5 for both hosts, and the hosts' side of 6.
- **What it needs from the owner:**
  - access to GitHub (a CLI sign-in is enough) and to Azure DevOps;
  - the GitHub organization and repositories;
  - the Azure DevOps organization, projects and repositories.