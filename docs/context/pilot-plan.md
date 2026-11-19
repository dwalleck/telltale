# Pilot plan: measuring AI value

Exported from the planning doc "Measuring AI Value: A Structured-Use Pilot" (claude.ai, Nov 11, 2026). This file is the version of record for the repo; terms are defined in `GLOSSARY.md`.

## The ask

Move most of the team from the $40 Kiro plan to the $100 plan, and the heaviest users to the $200 plan. The pilot shows whether each upgrade pays for itself.

## The story

How we use AI decides whether it is worth the spend, and we can measure the difference. Everyone already uses AI, so "AI vs. no AI" can't be measured. We compare structured AI use with ad hoc AI use in a bounded pilot, judged on success criteria written down before it starts.

Four claims:

1. **Senior review time is protected.** Structured AI use reduces the senior review effort each PR needs. Senior review is the team's scarcest resource and biggest bottleneck: only the most senior people review, first reviews wait, and mid/junior PRs churn through comment-commit-comment loops.
2. **Delivery gets more predictable.** Cycle times become more consistent, so forecasts tighten.
3. **Each tier upgrade pays for itself.** Spend is fixed flat-rate seats, so the question is outcomes per seat. The $100 Pro tier's newer models must deliver enough beyond the $40 tier (capped at Sonnet 4.6 and Opus 4.5) to justify $60 per person per month. The $200 tier goes to people who keep hitting the $100 tier's credit limit (demand, not ranking).
4. **Quality holds.** No gains at the cost of defects, reverts or reopened work. What didn't improve is reported too.

The argument is about output, quality and predictability, not hours saved: AI spend is added to payroll, not a replacement for it.

## Metrics

All durations in business days. Median and P85, not averages (averages only for totals).

| Metric | Claim | Role | Source |
| --- | --- | --- | --- |
| Review rounds per PR (stand-in for senior review effort) | 1 | Headline | GitHub |
| Not-fixed threads per PR | 1 | Supporting | GitHub review threads |
| Time to first code review (full wait from opening, counted in the sprint of the first qualifying review, without requiring merge), time to re-review, share of PR life in reviewer wait | 1 | Supporting | GitHub timeline |
| P85 cycle time by point bucket, and the median–P85 gap | 2 | Headline | Jira |
| Time in each status per sprint | 2 | Supporting | Jira |
| Delivery forecast range (SLE, Monte Carlo on daily throughput) | 2 | Supporting | Jira |
| Merged PRs and closed issues per seat, by plan tier and point bucket (seat value) | 3 | Headline | Sessions + Jira + GitHub |
| Share of ticket-branch sessions with no merged result | 3 | Supporting | Sessions + GitHub |
| Tokens and credits per merged PR (Pro accounts only) | 3 | Supporting | Sessions |
| Reopen rate, revert rate, rework, linked bugs | 4 | Guardrail | Jira + git |
| PR size; reviews per active reviewer | 4 | Guardrail | GitHub |
| Hours per point | — | Only if worklogs pass reliability checks | Jira worklogs |
| Reviewer concentration, distinct reviewers | — | Context, not claimed for AI | GitHub |

Each AI practice is flagged per PR from session logs so its effect can be compared directly: agent pre-review before requesting human review; grill-me or to-prd before coding; agent help addressing review comments; seniors using AI to review.

Worklog reliability checks: gap between worklog `started` and `created` (logged at sprint end = reconstructed), heaping at round numbers, share of issues with no hours.

## How the comparison stays fair

- Structured vs. ad hoc is labeled per piece of work, so the same person's work is compared with and without skills.
- Each participant is compared with their own baseline, and their change is compared with the whole-team trend over the same period (the non-volunteers are too few to report as a group, ADR-0003).
- Group only by attributes fixed before work starts (ADR-0006).
- Privacy rules: ADR-0003, ADR-0005.

## Brief visits (decided 2026-11-17)

The evaluation is judged with brief visits left out (`exclude`) of both time in status and cycle time. The same treatment applies to the baseline sprints and to the evaluation sprints. The rule is in `.scratch/brief-visit-treatment/spec.md` (owner decisions D83 to D110).

- **Reason:** a stay shorter than its status's threshold records when the board was updated, not how long the work took. Its usual cause is an issue moved through several statuses at once after the work was done. The recorded figures are then wrong, usually too short, by an amount that differs from sprint to sprint, and they would move if people began to update the board more promptly, which the structured workflow may itself cause.
- **When:** decided after the recorded figures of past sprints had been seen (D89), and before the setting was built or any evaluation sprint was judged. No evaluation figure is judged under the recorded treatment.
- **What the adjusted figures are:** figures of the issues that were moved on time. Small issues are left out more often than large ones, so cycle time is read by point bucket and never as one figure for a sprint. The number of delivered issues whose cycle starts with a brief visit is given once for each sprint, not for each point bucket, and is read beside that sprint's figures, for each baseline sprint and for each evaluation sprint (D87).
- **Later:** a start taken from another source, such as the first commit on the issue's branch or a pull request event, is a second reading when it is built (D88). For the PR-based comparison, take the earliest commit author date on the earliest-opened attributed PR opened by the Jira delivery moment; it may merge later or never merge. Flag PRs opened after delivery and retain their evidence separately, even when a commit predates delivery: it may have been carried into a backport. An abandoned attempt is still evidence that work had started. If the selected author timestamp predates Jira issue creation, include it unchanged and flag it as “commit predates issue creation”; the date alone cannot distinguish early work from inherited history. If that first PR’s commit history is unreadable or incomplete, report the first attributed PR commit as unknown and omit it from the timestamp comparison. Report known commit evidence from later attributed PRs separately, not as a substitute. The elapsed time from commit to delivery is a lower bound, because work can begin before the first commit. It does not replace this treatment unless a dated commit here says so.

Amended on 2026-11-17, after the treatment was built, to say what was built: a cycle that starts with a brief visit is usually too short and can be too long (D107), the number of such cycles is given for each sprint, and the decisions run to D110. The decision of this section did not change.

The thresholds are company configuration and are not in this repository. The owner fixed them on 2026-11-17, before the pilot started, and their dated record is kept on company systems. The SHA-256 fingerprint of that record's thresholds, salted so that it gives nothing away, is `535c5f51b95c496155de3c7493a479dbdb16fadea3a52646af0c6805abd704a1`; the record says how to check it. A changed threshold needs a new dated record and a new fingerprint here.

## The pilot

- **Duration:** 3–4 sprints.
- **Who:** the group volunteering to use the mattpocock/skills workflow, on the $40 seats everyone already has.
- **Pro seats:** only two exist: the project owner's (single-user, tied to SSO, cannot be shared or reassigned) and possibly one more, which goes to a consenting mid-level developer as a second case study. Leadership will likely approve no more than that.
- **Early signal:** the owner alternates issues between the newest models and Sonnet 4.6/Opus 4.5. Sessions are split into delivery and research, since the owner's credit use includes research like this project.
- **Replay benchmark:** rerun 10–20 past issues from the commit before their merge, in a fresh clone with no later history, in four conditions (each model tier × with/without skills). Score on the merged PR's own tests passing, human interventions, credits, and a blind senior review of the diff. Use a fixed answer sheet for grill-me questions so runs are comparable. Run each condition at least twice if credits allow; start with ~5 issues to prove the harness. Measures quality and effort, not speed.
- **Success criteria:** leadership won't define them, so the owner writes them, includes at least one failure condition, and sends them to leadership as a dated record before the pilot starts.
- **Decision at the end:** expand, adjust or stop. Ask in phases: structured use on existing seats first, tier evidence next, broad upgrade last.

## Reporting

A Confluence page per sprint, generated and published through the REST API. See `confluence-page.md`.

## Open questions (owner's, not technical)

- Which mid-level developer takes the second Pro seat?
- Do the Pro accounts' credits last a sprint of heavy use?
- Which past issues go into the replay benchmark?
- Track this measurement work as its own Jira epic, so its AI use is attributable?
- Which sprint starts the pilot, and how many sprints of baseline history exist?
- When do the written success criteria go to leadership?
