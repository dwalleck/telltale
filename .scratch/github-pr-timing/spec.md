# PR timing across GitHub and Azure DevOps

Status: ready-for-agent

## Problem Statement

The pilot needs to distinguish a wait for code review from time spent waiting to merge. Jira time in status does not tell us when a reviewer responded. Starting from Jira links misses unlinked PRs; starting only from merges hides reviews on PRs that remain open. A quick automation comment or Ops approval can also make a long wait for code review look short.

## Solution

Start with all PRs in the configured repositories. In each sprint report show three independent waits from PR opening: first code review, first code approval, and merge. Count each wait in the sprint window containing the event that ended it, without clipping its duration to that window. Show contributing PR count, median and P85 in both elapsed and business days. Label the population as configured repositories, not necessarily the team’s own PRs; the figures measure recorded waits, not reviewer effort.

## User Stories

1. As a pilot owner, I want to enable either host independently, so that missing access to one does not masquerade as zero activity.
2. As a report reader, I want unlinked PRs included, so that Jira integration coverage does not determine my timing figures.
3. As a report reader, I want an open PR counted as soon as a human reviews it, so that I need not wait for merge.
4. As a report reader, I want a review in a later sprint to include the wait since opening, so that work carried across sprints does not look faster.
5. As a report reader, I want first approval distinguished from first comment-only review, so that a response is not mistaken for sign-off.
6. As a report reader, I want a later dismissal to preserve the first review time, so that recorded interaction is not erased.
7. As a report reader, I want Ops and automation separated from human code review, so that neither stops the wrong clock.
8. As a report reader, I want abandoned PRs distinguished from merged ones, so that closing without merge never contributes a merge time.
9. As a report reader, I want missing reviews and unreadable histories counted separately, so that I can interpret the denominator.
10. As a pilot owner, I want host-specific and combined figures, so that I can compare migration-period sprints without averaging percentiles.
11. As a report reader, I want the business calendar and percentile rules used elsewhere in the pilot, so that comparisons use the same units and conventions.
12. As a participant, I want aggregate reporting and no individual reviewer records exposed, so that the pilot measures work rather than people.
13. As an operator, I want a second sync to pick up a later merge without duplicating the earlier review, so that reruns are safe.
14. As a Jira-only user, I want my existing configuration and reports to keep working without host credentials, so that enabling PR timing is a choice.

## Implementation Decisions

- **Collection scope.** Add optional GitHub and Azure DevOps (ADO) host settings with explicit repository allowlists. Collect read-only, with pagination, system proxy and corporate certificate trust. Keep credentials in the existing secret store. With no host configured, make no PR requests and leave Jira-only behavior unchanged. Treat a host not read, a failed read and an empty successful result as different states.
- **Lifecycle.** Identify a PR by host, repository and PR identifier. Backfill and refresh PRs opened before the reporting window as well as open, completed and abandoned PRs. Read enough history to establish the *first* qualifying event, not merely the first event visible in a sprint. Respect the run’s as-of time. Repeated pages, syncs and derivations must not add duplicate observations.
- **One start, three possible ends.** Use the original PR opening instant, including for drafts or reopened PRs. First code review and first code approval can occur before closure; a first approval can satisfy both measures. For merge use GitHub’s merge timestamp, or ADO’s closure timestamp only for a completed PR. Count qualifying pre-closure reviews even when a PR ultimately closes without merge. Never substitute closure, the current time, or zero for a missing review.
- **GitHub.** Use the submission time of the first non-author human review: comment-only, changes requested, approved, or subsequently dismissed. A dismissed submission is a review, not by itself proof of approval. Exclude ordinary conversation, authors’ own activity, automation and the configured Ops group’s activity from code review and code approval. Use current group membership and disclose that limitation for historical data. If group membership cannot be read, do not assume the roster is empty. Track Ops-only approval separately from code approval.
- **ADO.** Use the earliest qualifying timestamped non-author file-thread text comment or nonzero human reviewer vote. For first approval, accept positive votes including approval with suggestions. Resolve the actor behind the vote event; do not use a system thread author or a reviewer’s current vote as a timestamp. Ignore general conversation, reset votes and automation, including configured stable identity IDs for accounts that act exclusively as bots. Do not apply a human Ops-group exclusion on ADO.
- **Incomplete evidence.** An unclassifiable earlier event or truncated history that could change the first qualifying event makes that measure unavailable. A known merge may still contribute. Report separately, as of sprint close (or the run time for an active sprint), waiting without qualifying review, closed without qualifying review, automation-only activity, Ops-only activity and unreadable histories. These are explanatory counts, not zero-duration samples.
- **Sprint and statistics.** Use the configured board’s sprint windows, inclusive start and exclusive end. Count each observed end event once; retain events between windows or in overlapping windows as unassigned rather than guessing. Pool individual waits for combined host figures, not host medians. Label combined coverage if a configured source is unavailable. Use the existing team time zone, holidays, business-day calculation, ordinary median and nearest-rank P85; round only for display. No observations means no duration figure.
- **Storage and reporting.** Follow the existing SQLite selected-raw-facts → rebuilt derived data → metric views design (ADR-0002). Retain only lifecycle and classified event facts needed to rebuild; no comment bodies, raw payloads, account names or email addresses. Use pseudonymized PR authors for contributor counts; do not expose per-person results. In the metric views enforce configured team and per-figure group thresholds (ADR-0003, ADR-0008), including on diagnostic breakdowns. The sprint report and owner query read the same protected figures. PR imports do not modify Jira cycle time or its treatment.

## Testing Decisions

- Use the existing in-process CLI pipeline harness as the acceptance seam: synthetic host responses, fixed clock, real collection, temporary SQLite store, real derivation and sprint report. Assert public metric rows and report behavior rather than method calls or raw table layout. Prior art is the existing sprint timing, incremental sync and suppression pipeline tests.
- **First acceptance test, currently red:** an unlinked synthetic GitHub PR opens in sprint A and receives Ops approval there. A human submits a comment-only review in B. Before merge, B must already show one first code review with a four-elapsed-day wait and no code approval or merge. After a later sync, C must show one merge with an eighteen-elapsed-day wait; B’s observation must remain unchanged. No Jira issue is created. The isolated test compiled and ran; configuration rejected the unrecognized GitHub setting before collection or timing assertions ran. Accepting the setting is necessary, not sufficient: both report runs and their assertions must pass.
- At the same seam, exercise an ADO automation comment followed by a human file comment and a later positive vote; a dismissed GitHub submission; an abandoned PR; a weekend or holiday; events at a window edge; unreadable history versus a successful empty read; and the host-pooled nearest-rank P85. Check configured and disabled suppression through the report and the actual owner query command. Keep real repo names, people and production observations out of fixtures.

## Out of Scope

Jira issue–PR matching, first-commit evidence, changing Jira cycle time, proving what migration did to Jira links, per-person reporting, review effort, re-review and time in reviewer wait. Ready-for-review timing is not a v1 requirement: do not present opening time as a measured draft-publication time. Do not infer team membership from unlinked PRs in shared repositories.

## Further Notes

The two research tickets in this feature’s local tracker contain the feasibility evidence and host-specific caveats. Their early merge-sprint comparisons are not the definition of first code review. The glossary and pilot plan assign that wait to the sprint of the qualifying review. The red acceptance test records the starting point for implementation, not a passing result.
