# 01: Can Jira's links to pull requests lead to the first commit of a story?

Type: research

**What to find out:** Whether the tool can get a better start for a story's **cycle time** than the board gives, by asking Jira which pull requests belong to the story and then asking the code host for those pull requests' commits. The earliest commit would be the start. This is the third **treatment** the brief visit treatment spec left for later (owner decision D88): replacing a start that is a brief visit with one from another source.

Nothing is built here. The answer decides whether a spec is worth writing and what it has to say.

Background, from the owner (2026-11-17):

- Pull requests for the older sprints are mostly in Azure DevOps. For a few recent sprints one project is in Azure DevOps and one in GitHub. After that everything is in GitHub.
- Jira's integration is installed for both hosts, so an issue's development panel links to its pull requests on either.
- The owner does not expect Jira to hold the timings. The link is wanted only to know where to look.

What the tool has today: it collects issues, their status, sprint and point history, sprints and statuses from Jira, and nothing about pull requests, branches or commits. It keeps Jira's internal issue ID and not the issue key, so matching from the code host's side by a key in a branch name would need the key at import.

**Blocked by:** None (can start immediately).

**Status:** ready-for-human

A person has to make the read-only access tokens for the two code hosts and choose the stories to look at. With those in place an agent can run the rest on the work laptop.

## Questions

1. **Jira.** Can the tool's Jira token ask for the pull requests linked to an issue, by internal issue ID, through the corporate proxy? What comes back for an issue linked to Azure DevOps and for one linked to GitHub: which fields, and is there enough to name the pull request to its host (repository and number)? Is the endpoint documented and supported, or internal? Can it be asked for many issues at once, and what are its limits?
2. **Coverage.** Of the delivered issues in the store, how many have at least one linked pull request? Look at the three periods apart: Azure DevOps only, both hosts, GitHub only. Look also at the delivered issues whose cycle starts with a brief visit, since those are the ones a new start would bring back.
3. **The hosts.** For a pull request Jira names, can each host give the list of its commits with their author dates? Does that still work after a squash merge, and after the source branch was deleted? Which read-only token scopes are needed, and do the calls pass the proxy?
4. **Is the date believable?** For the stories looked at, how does the earliest author date compare with the first move into a work status: earlier or later, and by how much? For a story whose cycle starts with a brief visit, does the first commit fall where the work plainly began?
5. **Several pull requests.** How common is a story with more than one, and does taking the earliest commit across them give a sensible start?
6. **What else is there for nothing.** The same pull request records may hold when it was opened, first reviewed and approved. Note what each host gives, for the elapsed time of a code review, without collecting it.
7. **After the migration.** Will Jira keep its links to Azure DevOps pull requests once that integration is removed, and will Azure DevOps keep the pull requests readable? This cannot be tried: ask the administrators and read the vendors' documents. If either answer is no, the links and the commit dates for the older sprints have to be collected before the migration ends.

## Rules for the work

- **Read only.** No call changes anything in Jira, Azure DevOps or GitHub.
- **Company data stays on company systems.** The work runs on the work laptop. Notes, responses and counts are kept there, outside this repository.
- **This repository is public.** No issue ID or key, pull request number, repository or project name, person, date of a real event or count from real data goes into this ticket, a commit or a test. The answer below is written in general terms: yes or no, the names of fields, and what a spec would have to handle.
- **Nothing about a person is recorded.** Pull request and commit records carry authors' and reviewers' names and addresses. The spike reads dates and identifiers only, and writes no name, address or account anywhere (ADR-0003).
- **Tokens** go in Windows Credential Manager, never in a file or on a command line, and have the narrowest read-only scope that works.
- Use the commit's author date, not its commit date: a rebase or an amend rewrites the commit date. Read commits from the pull request's own record, not from the main branch, where a squash merge leaves one commit dated at the merge.
- A first commit is a lower bound on when work started. The spike does not judge whether that is good enough; it reports how the dates fall.

## Done when

- [ ] Each of the seven questions has an answer under `## Answer`, in general terms, with how it was found
- [ ] Each answer says whether it was observed on the real systems, read in a vendor's documents, or told by an administrator
- [ ] The answer says plainly whether the route works for the Azure DevOps period, for the GitHub period, or for neither, and what would stop it
- [ ] The answer lists what a spec for a first-commit start would have to decide: which commit date, several pull requests, stories with no linked pull request, how the figure is labelled as a lower bound, and whether it is a second reading or replaces leaving out (the pilot plan says a second reading unless a dated commit there says otherwise)
- [ ] The answer says by when the older sprints' links and dates must be collected, if question 7 finds a deadline
- [ ] Nothing real is in the repository; the detailed notes are named by where they are kept on the work laptop
