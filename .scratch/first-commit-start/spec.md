# Jira issue–PR matching and first-commit evidence

Status: ready-for-agent

## Problem Statement

A delivered issue can spend only a few minutes in its first work status even though code was written earlier. Jira records the board history correctly; it does not tell us whether that visit marks the beginning of work. Jira’s Development panel may name a PR, list only a commit in a migrated repository, or contain no usable link. The owner needs to know what the available evidence establishes, for how many issues, without changing Jira cycle time.

## Solution

Add a second reading alongside the board measures. Collect commit evidence on attributed GitHub and Azure DevOps PRs whether or not the issue starts with a brief visit. For a delivered issue, select the earliest-opened attributed PR that opened by delivery, then find its earliest **author** timestamp. Show coverage and compare that timestamp with the first work-status entry for issues whose cycle starts with a brief visit. No result from this feature changes a Jira timestamp, treatment, or cycle-time figure.

## User Stories

1. As a pilot owner, I want to see the number of delivered issues with usable evidence alongside the total, so that I can judge whether the comparison is representative.
2. As a report reader, I want the brief-start subset shown separately, so that I can answer the board-timing question without assuming every issue has this problem.
3. As a collector operator, I want matching to work from either host without a Jira PR link, so that migration gaps do not erase all historical evidence.
4. As a report reader, I want direct links and agreed keys distinguished from commit-only candidates, so that shared history is not presented as proof of attribution.
5. As a report reader, I want abandoned attempts counted, so that the PR that shipped does not hide earlier work.
6. As a report reader, I want all known evidence retained when there are several PRs, so that a single comparison timestamp does not imply there was only one attempt.
7. As a report reader, I want to know when work was observed but the first timestamp is unknown, so that a later readable PR is not mistaken for the start.
8. As a pilot owner, I want pre-creation and post-delivery dates explained, so that unusual chronology is visible rather than silently corrected.
9. As a report reader, I want missing, ambiguous and unreadable evidence separated, so that non-code work and collection failures are not conflated.
10. As a pilot owner, I want Spikes excluded by issue type, so that miscellaneous tasks are not in the comparison denominator.
11. As a participant, I want protected aggregate output, so that issue keys, branches and commits cannot be used to identify people through the report.
12. As a Jira-only user, I want existing cycle-time results unchanged, so that enabling another source does not redefine the pilot baseline.

## Implementation Decisions

- **Population.** Start from all delivered non-Spike issues, not just matched issues, and group by delivery sprint. Retain a between-sprints row. Use the configured Spike type ID, not a name or a board filter. No Spike-specific collection is required; incidentally collected host facts need not be deleted. Exclusion applies only to issue–commit reporting, not Jira metrics or independent PR timing.
- **Sources.** Read only configured, approved repositories. Reuse host collection and PR identity from PR timing where available; do not require a submitted review, a merge, or a published PR timing report. Use the internal Jira issue ID for joins; D113 permits storing its key locally for resolution. Match complete Jira keys with token boundaries, including hyphen and underscore separators; never match an issue number prefix. Resolve against the imported project scope. Identify a PR by host, repository and PR identifier, not a live branch; paginated and repeated lookups must not duplicate it.
- **Attribution on both hosts.** An unambiguous key in either the source branch or title, or a Jira Development link naming a specific PR, suffices. If these sources disagree, attribute that PR to neither issue, even if only one key resolves. Descriptions and commit messages are mentions, not assignments. A branch or repository link in Jira is not a PR link. Multiple routes to one PR corroborate it; they do not create more PRs.
- **Shared SHA.** A Jira-linked commit found on a PR creates a candidate even if only one PR contains it. It cannot establish attribution on its own. Preserve corroboration when an independent attribution agrees, and distinguish unique, multiple, absent and unreadable SHA candidates. Neither choose a convenient release PR nor let an unsupported SHA-only candidate displace an independently attributed PR.
- **Evidence versus selection.** Keep available evidence for all attributed PRs, including open, abandoned, merged and post-delivery ones. Choose by PR opening instant, not earliest author date across PRs. Opening exactly at delivery qualifies. An earlier abandoned attempt or a PR merged after delivery can win. Flag a PR opened after delivery and retain its evidence, but never select it, even when its commits predate delivery.
- **Date quality.** For the selected PR use the minimum author timestamp from its complete commit history, never committer, opening or merge time. If history is unreadable, truncated or lacks a usable minimum, keep the issue in coverage but out of the paired comparison. A later attributed PR can establish “work observed by this date,” not the missing first timestamp. If two or more PRs tie for earliest opening, use the shared earliest author timestamp once only if *all* tied histories are complete and agree. Otherwise flag “first PR ambiguous”; never break the tie by PR number.
- **Chronology.** Keep an author date before issue creation unchanged and flag “commit predates issue creation.” If it is after delivery, retain it as evidence, flag “commit after delivery” and omit the comparison; do not clamp, substitute or emit a negative duration. An author date equal to delivery is not after delivery. A commit shows work existed by an instant, not how much effort was spent or whether every inherited commit belongs to the issue.
- **Report.** Give distinct issue counts for source routes (which can overlap), their union, attributed PRs, issues with a PR opened by delivery, and issues with a usable first timestamp. Show why evidence stops short: no match in readable scope, outside scope, candidate only, conflicting attribution, post-delivery PR only, ambiguous first PR, missing history or unusable date. Mark sources not read or failed; never imply a complete search or that an unmatched board issue necessarily involved code. Repeat coverage for the existing brief-start flag, independently of the Jira cycle-time treatment. For paired cases show contributing issues, before/equal/after counts and median signed elapsed offset (commit minus first Jira work-status entry; negative means earlier). Use the original first work-status entry even when treatment excludes the Jira cycle. Never fill a missing pair from board time.
- **Storage and privacy.** Follow ADR-0002: collect selected source facts, rebuild attribution and comparisons, and expose protected aggregates through metric views. Retain route, PR identity, relevant timestamps and completeness needed to explain a result, not prose, raw payloads or person fields from commit histories. Do not expose issue keys, PR numbers, SHAs or per-issue timelines in reports or owner query. Apply the configured team and per-figure suppression to counts, offsets and diagnostics (ADR-0003, ADR-0008). Use existing secret handling and system proxy/trust; keep company evidence on company systems.

## Testing Decisions

Use the existing in-process CLI pipeline as the acceptance seam: synthetic Jira and host responses, fixed clock, real collection and derivation, temporary SQLite store, sprint report and protected owner query. Test observable counts and timing rather than helper calls. Prior art: brief-visit treatment, incremental collection, protected query and PR timing pipeline scenarios.

- **Defining case:** A synthetic non-Spike issue is created Tuesday, first enters Development Thursday at noon for a brief visit, and is delivered Friday. Attributed PR A opens Tuesday with a Monday-noon author timestamp and is later abandoned. PR B opens Wednesday and merges before delivery. Expect one covered issue, evidence from both PRs, a **−72 hour** paired offset (same UTC offset), and a pre-creation flag. A later sync must neither double-count nor switch the start to B. Compare Jira-only measures before and after enabling evidence under both brief-visit treatments.
- Repeat with A’s history truncated: keep B’s evidence and the coverage denominator but report no paired offset. Exercise equal and unequal minima on tied openings, post-delivery opening with an older commit, and pre-delivery opening with a post-delivery author timestamp.
- Exercise both hosts, direct-only attribution, branch-only and title-only attribution, conflicts across routes, deleted branches, duplicate matches, key-prefix collisions, unique and multiple SHA-only candidates, and a source read failure distinct from a successful empty search.
- Verify ordinary starts still collect evidence; Spike drops from both numerator and denominator; between-sprint deliveries remain visible. Check a positive and a zero offset as well as a negative one. Verify suppression through report and actual owner query, without real company data.

## Out of Scope

Changing Jira cycle time, replacing the brief-visit treatment, imputing starts, attributing individual effort, recalculating PR timing or proving whether Jira historically overwrote an ADO link. No requirement to inspect repositories outside the approved scope.

## Further Notes

The glossary and pilot plan record the owner’s attribution and eligibility decisions. Earlier research using a merged-by-delivery funnel established feasibility, not production coverage: open and abandoned attempts are eligible here. Recompute coverage under these rules before comparing results with the pilot. Implementation tickets and a runnable acceptance test are separate follow-up work.
