# Implementing a spec in this repository

Additions to the `implement-spec` skill for Telltale. Each one is a step the PR timing spec needed and the skill does not name.

## After exploration: the owner rules on open points

The exploration ends with a list of what the spec and its tickets leave undecided or state two ways. Put each to the owner with a recommendation and wait for the answers before the first implementer starts. The five questions in `triage-labels.md` ("When a spec is `ready-for-agent`") are the ones most often open. A point about what a figure means, or about the minimum group size, is the owner's alone (ADR-0008, `docs/decisions.md`).

Write the answers where every implementer reads them, and record an owner decision in `docs/decisions.md` under the next free D-number before code is built on it.

## Before parallel tickets start: hand out what they share

Tickets that run at the same time each get, in their brief:

- a migration number of their own (the next free ones in ticket order; an unused number is fine);
- the files and views they own and the ones another ticket owns;
- the name of anything two tickets both add (a column, a measure), so the merge meets the same line twice and not two designs.

## Merging

An implementer merges the integration branch into its own and runs the full suite before it reports. When its branch then contains the integration tip, the orchestrator merges it without a merger agent. A merger agent is for a merge that conflicts.

After every merge into the integration branch, run the full suite there before the next step.

## Review

Run the review of the integration branch at `high`. A review at a lower level found ten defects in the PR timing branch; a later one at `max` found twenty-three more. The reviewer reads `CODING_STANDARDS.md`.

## Briefs

- **Third-party APIs.** An implementer fetches the host's public documentation for any answer shape it reads and names in its report the shapes no test fixture or research ticket confirms.
- **Real data.** A brief names the repository only. The owner's work folder and any path a ticket gives for notes or results outside the repository stay out of it (`research-spikes.md`).
- **Checks.** The owner's local toolkit reads `.repo-toolkit.psd1` and runs the full check (format, line endings, warnings, the SQL line-ending check, mutation specs) as `Invoke-FullCheck`. Where that command exists, an implementer runs it before reporting. Where it does not, it runs `dotnet build`, `dotnet test` and `eng/check-sql-line-endings.ps1 -RepoRoot .`, and says the full check was not run.
