# Telltale

A .NET delivery metrics tool that collects delivery data from Jira, GitHub, local git clones and AI agent session logs into one SQLite store, and reports team-level metrics on flow, review load, seat value and quality. Its purpose is to compare structured AI use (the mattpocock/skills workflow) with ad hoc AI use, and to justify AI plan upgrades to leadership.

Start with `docs/context/handoff.md`: it explains where this project came from, what is decided, what is built, and what is still open.

## Constraints

- **Runs on one developer laptop** (Windows, corporate managed) for the first 3–4 sprints. AWS comes later, only after buy-in. Keep the host thin so moving is a new host, not a rewrite.
- **Corporate network**: outbound HTTPS goes through an intercepting proxy. HTTP clients must use the system proxy and trust the corporate root certificate.
- **Secrets** (Jira, GitHub, Confluence tokens, hashing salt) live in Windows Credential Manager or DPAPI-protected storage. Never in config files or the repo.
- **.NET 11**, C# 15, nullable enabled, warnings as errors. Use union types and other C# 15 / .NET 11 features where they make the code clearer.
- **Tests use TUnit**, not xUnit.
- **Company data stays on company systems.** Never move Jira, GitHub or session data to personal accounts or services.

## Non-negotiables

- The tool reports on the team's work, not on individuals. Person fields are pseudonymized at import (ADR-0003, ADR-0005), and no feature produces per-person results outside the two consented case studies.
- The minimum group size (withholding figures that rest on few people) is a default, not a hard rule: five and three unless `minimumGroupSize` in the configuration file says otherwise, and zero turns a check off (ADR-0008). The team asked for this tool, and its Jira figures aggregate what anyone with access to the board can already see. The metric views enforce whatever numbers are configured; no feature may show a figure the configured numbers withhold.
- Metric definitions live in `docs/context/pilot-plan.md` and the glossary. Changing a definition is a commit with a reason, because the git history is the dated record that criteria were set before results existed.

## Agent skills

### Issue tracker

Local markdown under `.scratch/` (the team's real tracker is Jira, but agents work locally; tickets can be mirrored to Jira later). See `docs/agents/issue-tracker.md`.

### Triage labels

Default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` at the root, ADRs in `docs/adr/`. See `docs/agents/domain.md`.
