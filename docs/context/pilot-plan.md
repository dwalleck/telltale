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
| Time to first review, time to re-review, share of PR life in reviewer wait | 1 | Supporting | GitHub timeline |
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
