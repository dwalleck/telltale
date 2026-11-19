# 02: Time in a pull request and time to first review, from the pull requests alone

Type: research

**What to find out:** Two things, from every pull request in the team's repositories on **both hosts**, Azure DevOps and GitHub, whether or not it can be tied to a Jira issue:

1. **What the pull requests say about elapsed time:** how long a pull request is open before it merges, and how long it waits for its first review. Group time to merge by merge sprint. Group time to first code review by first qualifying review sprint, regardless of when or whether the PR closes.
2. **Why a pull request does or does not lead to a Jira issue.** For each one, record what is there to link it and what is missing. The owner then knows what would have to change, in the data, the tool or the team's habits, for the two to be joined.

A third thing follows from the move from Azure DevOps to GitHub:

3. **What the migration did to the links.** The owner's hypothesis (2026-11-18): when the repositories moved to GitHub, Jira's link to an issue's original Azure DevOps pull request was replaced by a link to a commit in the GitHub copy of the repository. If so, an older issue's development panel now points at migrated history, not at the pull request where the review happened.

Nothing is built here. [Ticket 01](01-can-github-measure-pull-request-time.md) showed the GitHub fields exist and that starting from GitHub finds more pull requests than starting from Jira. This spike measures over the whole pull request population on both hosts and explains the gap. Linking to an issue is not a condition for being measured.

**Blocked by:** None (can start immediately).

**Status:** ready-for-human

It runs on the work laptop. The GitHub half can run now, with the signed-in GitHub CLI and the approved repository list already kept there. The Azure DevOps half needs approved read access (an Azure CLI sign-in or a Code (Read) token in Credential Manager), the organization, projects and repositories to read, and the owner's word on which sprints fall in which period. The two halves can run apart. Report the Azure DevOps half as not read until it runs, never as zero.

## Questions

1. **Time in a pull request.** For every pull request completed in the sprint windows, what are open to merge and ready for review to merge?
   - GitHub: `created_at` to `merged_at`. Ready for review comes from the timeline's `ready_for_review` event, or from opening when the pull request was never a draft.
   - Azure DevOps: `creationDate` to `closedDate`, for status `completed` only (`abandoned` also closes). Does Azure DevOps record when a draft was published, and where: a thread, an event, or nowhere? If nowhere, ready for review cannot be told from opening for a draft.

   Which pull requests do not give a clean figure? Count each kind apart: reopened, turned back into a draft, or the ready time inferred.
2. **Time to first review.** Measure open to the first review, ready for review to the first review, and open to the first approval.
   - GitHub: the reviews list, `submitted_at` and `state`.
   - Azure DevOps: the reviewer record has a `vote` but not its time. Do the pull request threads (`publishedDate`, system threads with `CodeReviewVoteResult`) give the time of each vote, for every pull request, including old ones? Is a first comment from a reviewer usable as a first review, and how often is it the only sign?

   Count apart:
   - pull requests completed with no review;
   - pull requests whose only reviews are from automation (bot or service accounts, Copilot, build validation). On Azure DevOps, the owner confirmed that the designated SonarQube accounts were exclusively automated: exclude every comment and vote from those accounts, regardless of its text. Do not apply the GitHub Ops-group exclusion on Azure DevOps;
   - pull requests whose first review is a comment rather than an approval or a request for changes.
3. **Per sprint.** Median and 85th percentile per sprint (first code review by review time, time to merge by merge time), in business time and in elapsed time, as the tool reports other measures. Give them per host and combined for the sprints that used both. How many pull requests each figure rests on, and how many distinct authors.
4. **What links a pull request to an issue.** On GitHub, check both source branch and title before assigning an issue. Count agreement, branch only, title only, differing keys, and neither separately. One unambiguous key in either field suffices when it resolves to an imported issue. If branch and title name different keys, count the PR once under conflicting keys and once in its merge sprint’s PR timing, even if only one key resolves. Attribute neither the PR nor its commit evidence to either issue. Description and commit-message keys are mentions, not fallback assignments. Every merged PR still counts for PR timing. For Azure DevOps, investigate the following classes in order:
   - the key of an issue in the store, in its source branch (`head.ref` on GitHub, `sourceRefName` on Azure DevOps);
   - such a key in its title only;
   - such a key in its description only, as a key or as a link to a Jira issue;
   - such a key in a commit message only;
   - on Azure DevOps, a link to a work item only, with no key anywhere;
   - a key of an issue in a Jira project the tool does not import;
   - something shaped like a key that names no issue (a typo, a wrong project, a missing number);
   - no key anywhere.

   Count each class per sprint, host and repository.
5. **What Jira's links add.** For the pull requests Jira's Development panel names, which class above did they fall in? Is any pull request Jira links one that holds no key the spike could find?
6. **The other direction.** Of the delivered issues in the same sprints, how many have at least one pull request by the classes above, on either host, and how many have none? For those with none, is there any sign of code? Jira listing a branch or repository counts; that is the only place left to look.
7. **Who is not in the team.** Shared repositories hold pull requests for other teams' Jira projects. Does the key's project, or the repository, tell the team's pull requests apart from other teams'? What would decide it when neither does?
8. **The migration and Jira's links.** Test the owner's hypothesis:
   - For delivered issues of the Azure DevOps period, what does Jira's Development panel hold now: GitHub repositories, branches or commits, and no pull request? Is the commit or branch it names in the GitHub copy of a repository that was migrated?
   - Are the commit SHAs the same in the Azure DevOps repository and its GitHub copy? If the history was carried over unchanged, a commit Jira names can be found in the Azure DevOps repository, and from there the Azure DevOps pull request that holds it.
   - Did the migration bring the pull requests over as well (GitHub Enterprise Importer can), or only the git history? If the pull requests came over, do they keep their original created, merged and review times, or the time of the migration?
   - Did Jira ever hold links to Azure DevOps pull requests, which were then removed with the integration, or did it never hold them? This is for the Jira administrators and, if kept, the audit log; the spike cannot tell "removed" from "never there" by itself.
   - Whatever the answer, can the Azure DevOps pull request be reached from the issue another way: the key in its branch (question 4), or the commit Jira names (above)?

## Rules for the work

Access, where results go, people and what may go in this repository follow [`docs/agents/research-spikes.md`](../../../docs/agents/research-spikes.md). This ticket adds:

- Authors and reviewers are used in memory to tell automation from people, as well as to count distinct authors.

## Done when

- [ ] Each question has an answer under `## Answer`, in general terms, with how it was found
- [ ] Each answer says whether it was observed on the real systems, read in a vendor's documents, or told by an administrator
- [ ] The answer says, per host, which measures can be read as recorded, and which need a rule a spec has to decide (draft time, reopened, no review, automation only, a vote with no time)
- [ ] The answer lists what would link more pull requests to issues; the ranking by how many pull requests each would join is kept in the work folder, since it is drawn from real counts. For example: reading titles, descriptions or commit messages as well as branches; following a commit Jira names back to its Azure DevOps pull request; importing more Jira projects; or a change in how branches are named
- [ ] The answer says whether the owner's hypothesis about the migration holds, what is still unknown, and who has to answer it
- [ ] The answer says how a report would tell the team's pull requests from other teams' in a shared repository, or that it cannot yet
- [ ] Nothing real is in the repository; the detailed notes are named by where they are kept on the work laptop

## Answer

**Observed, GitHub timing (exploratory).** Starting from merged PRs rather than Jira links, paginated reads of reviews and timelines succeeded in the selected closed sprint windows. The work-folder report `notes/github-review-window-results.md` gives elapsed and business-day medians, nearest-rank P85s, contributing PRs and distinct known authors for open-to-merge, ready-to-merge, open-to-first-human-review, ready-to-first-human-review and open-to-first-human-approval. These measure recorded waits, not review effort. Opening can be an inferred ready time; returned-to-draft histories are omitted from ready-based measures. The windows overlap: merges that cannot be assigned uniquely are counted separately. Bot detection cannot rule out automation behind ordinary user accounts.

**Still open.** The run classified branch and title text; it did not resolve candidate keys to imported issues or attribute commit evidence. A later single-repository ADO study read PR histories and searched complete commit lists and source/merge tips independently of issue keys. Shared SHAs can identify a single completed PR, several, or none. In the previously uncorroborated subgroup, expanding the search yielded ambiguous or absent matches, not new unique attributions. Current links and sampled Jira issue changelogs do not establish historical link replacement. Detailed outcomes are in the work folder (`notes/payments-final-coverage.md`). No Jira cycle measure changed.
