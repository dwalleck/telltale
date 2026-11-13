# Handoff

Context carried over from a long planning conversation (claude.ai, Nov 11, 2026) so a Claude Code session can continue it. The next step is to sharpen the design and turn it into a spec, tickets and code.

## What this project is

The project owner (a senior .NET engineer) needs to justify AI plan upgrades to leadership who think in spreadsheets and default to "time saved". The approach: measure structured AI use (the mattpocock/skills workflow) against ad hoc AI use on flow, senior review load, seat value and quality, using Jira, GitHub, local git and agent session logs. This repo is the tool that collects and reports that data.

## Read in this order

1. `GLOSSARY.md` — the project's language. Settled.
2. `docs/adr/` — seven decisions with real trade-offs. Settled unless the owner reopens one.
3. `docs/context/pilot-plan.md` — the story, the metrics, the evaluation. The "why".
4. `docs/context/system-design.md` — the working technical design and source-system details. The "how", including what is already built.
5. `docs/context/confluence-page.md` — the report layout.
6. `src/Telltale.Core` and `tests/Telltale.Core.Tests` — the code that exists.

## What is open

**Update, Nov 13, 2026:** the minimum group size is a configuration setting, and it is built: `minimumGroupSize`, five and three by default, zero turning a check off. The spec is `.scratch/optional-suppression/spec.md`; the decision is ADR-0008, which amends ADR-0003.

**Update, Nov 12, 2026:** the first spec is built and merged. The Jira path works end to end against synthetic fixtures: `discover`, `secrets set`, `backfill`, `sync`, `derive`, `report`, `run` and `query` (see `README.md`). The owner's decisions made while building it are in `docs/decisions.md`. The next step is ticket 13, the first run against the real Jira on the work laptop: `.scratch/jira-flow-report/issues/13-first-real-run-on-work-laptop.md`. Start there.

**Status-duration quality investigation, Nov 12, 2026:** [Status-duration quality handoff](status-duration-quality-handoff.md) records the implemented total-business-days column, interpretation pitfalls found during local report investigation, evidence consistent with late Jira status updates, and the unresolved anomaly-detection and PR-based timing design. It contains no operational report values or company identifiers. Read it before changing duration eligibility or adding fallback measurements.

**Update, Nov 11, 2026:** a grilling session settled the Scope, Definitions and Implementation items below; the answers are in `GLOSSARY.md`, ADR-0002, ADR-0003 and `system-design.md`. The first spec is the Jira path end to end. What remains open: the company facts (found by `discover` on the work laptop), the Kiro session details (first ticket of the sessions spec), the table schema (for the spec), and three owner tasks: the volunteers' consent to cohort reporting, the baseline sprint list, and the date the success criteria go to leadership.

The original list, kept for the record:

**Scope**
- What does the first spec cover? Recommendation: the Jira path end to end as a tracer bullet (collector → raw tables → derived intervals → time in status and cycle time per sprint → a plain report), then later specs for GitHub review metrics, sessions, quality guardrails, Confluence publishing, and the replay benchmark.

**Definitions to confirm**
- Cycle time start and end (the glossary says in-progress category → done category; confirm against the team's workflow).
- Which statuses map to which category, and whether "Blocked" is its own category.
- Review round, when a reviewer leaves several reviews before the author pushes.
- Rework: lines changed again by anyone, or only outside the original PR's author?
- Linked bugs: which Jira link type connects a bug to the issue that caused it?
- Baseline window: how many sprints before the evaluation.

**Facts to discover (look up, don't ask)**
- Where Kiro stores session logs and what they contain; whether skill invocations are detectable. Blocks the exporter.
- Jira site, boards and projects in scope; Sprint and story point field IDs.
- GitHub cloud or Enterprise Server; token type allowed (PAT vs. GitHub App).
- Confluence space and page; API token scope.
- Company holiday list.

**Implementation choices**
- CLI parser (System.CommandLine vs. Spectre.Console.Cli).
- Secrets provider (Credential Manager vs. DPAPI file).
- Schema for raw and derived tables.

## Suggested skills, in order

1. `/grill-with-docs` — interview on the open frontier above, updating `GLOSSARY.md` and `docs/adr/` as terms and decisions settle.
2. `/to-spec` — **in the same session** as the grilling, since it synthesizes the current conversation. Publishes to `.scratch/<feature>/spec.md`.
3. `/to-tickets` — vertical slices with blocking edges, one file per ticket under `.scratch/<feature>/issues/`.
4. `/implement-spec` — integration branch, one implementer subagent per ticket using `/tdd`, then `/code-review`.

## Other artifacts (outside the repo)

- Planning doc (claude.ai Docs); its content is in `pilot-plan.md`.
- Design canvas (claude.ai); its layout is in `confluence-page.md`.
