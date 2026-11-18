# 02: Time in a pull request and time to first review, from the pull requests alone

Type: research

**What to find out:** Two things, from every pull request in the team's repositories, whether or not it can be tied to a Jira issue:

1. **What the pull requests say about elapsed time:** how long a pull request is open before it merges, and how long it waits for its first review. Measured per pull request and per sprint, by merge time.
2. **Why a pull request does or does not lead to a Jira issue.** For each one, record what is there to link it and what is missing. The owner then knows what would have to change, in the data, the tool or the team's habits, for the two to be joined.

Nothing is built here. [Ticket 01](01-can-github-measure-pull-request-time.md) showed the GitHub fields exist and that starting from GitHub finds more pull requests than starting from Jira. This spike measures over the whole pull request population and explains the gap. Linking to an issue is not a condition for being measured.

**Blocked by:** None (can start immediately).

**Status:** ready-for-human

It runs on the work laptop with the signed-in GitHub CLI and the approved repository list already kept there. Azure DevOps waits for access.

## Questions

1. **Time in a pull request.** For every pull request merged in the sprint windows, what are open to merge and ready for review to merge? Ready for review comes from the timeline's `ready_for_review` event, or from opening when the pull request was never a draft. Which pull requests do not give a clean figure? Count each kind apart: reopened, converted back to a draft, or the ready time inferred.
2. **Time to first review.** Measure open to the first submitted review, and ready for review to the first submitted review. Also measure to the first visible approval. Count apart:
   - pull requests merged with no submitted review;
   - pull requests whose only reviews are from automation (bot accounts, Copilot);
   - pull requests whose first review is a comment rather than an approval or a request for changes.
3. **Per sprint.** Median and 85th percentile of each measure per sprint, by merge time, in business time and in elapsed time, as the tool reports other measures. How many pull requests each figure rests on, and how many distinct authors.
4. **What links a pull request to an issue.** Sort each pull request into the first of these that holds:
   - the key of an issue in the store, in its source branch;
   - such a key in its title only;
   - such a key in its description only, as a key or as a link to a Jira issue;
   - such a key in a commit message only;
   - a key of an issue in a Jira project the tool does not import;
   - something shaped like a key that names no issue (a typo, a wrong project, a missing number);
   - no key anywhere.

   Count each class per sprint and per repository.
5. **What Jira's links add.** For the pull requests Jira's Development panel names, which class above did they fall in? Is any pull request Jira links one that holds no key the spike could find?
6. **The other direction.** Of the delivered issues in the same sprints, how many have at least one pull request by the classes above, and how many have none? For those with none, is there any sign of code? Jira listing a branch or repository counts; that is the only place left to look.
7. **Who is not in the team.** Shared repositories hold pull requests for other teams' Jira projects. Does the key's project, or the repository, tell the team's pull requests apart from other teams'? What would decide it when neither does?

## Rules for the work

- **Read only.** GET requests only, through the signed-in `gh` (`gh api --method GET --paginate`), so the token never passes through the spike's code. Nothing in GitHub or Jira is changed.
- **Company data stays on company systems.** Notes and counts stay in the work folder on the work laptop, outside this repository.
- **This repository is public.** No key, pull request number, repository or project name, person, date of a real event or count from real data goes into this ticket, a commit or a test. The answer here is written in general terms.
- **Nothing about a person is recorded.** Authors and reviewers are used only in memory: to tell automation from people, and to count distinct authors behind a figure. No name, address or account is written anywhere (ADR-0003). Results are per sprint and per repository, never per person.
- **Titles, descriptions, branch names and commit messages are read in memory only**, to find keys. None of them is saved.
- Keep a pull request that fits no rule: count it under its reason, do not drop it.

## Done when

- [ ] Each question has an answer under `## Answer`, in general terms, with how it was found
- [ ] The answer says which measures give a clean figure for most pull requests, and which need a rule a spec has to decide (draft time, reopened, no review, automation only)
- [ ] The answer lists, in order of how many pull requests each would join, what would link more pull requests to issues. For example: reading titles, descriptions or commit messages as well as branches; importing more Jira projects; or a change in how branches are named
- [ ] The answer says how a report would tell the team's pull requests from other teams' in a shared repository, or that it cannot yet
- [ ] Azure DevOps is listed as not read, with what its records would give (`creationDate`, `closedDate`, and reviewer votes from the threads)
- [ ] Nothing real is in the repository; the detailed notes are named by where they are kept on the work laptop

## Answer

Not started.
