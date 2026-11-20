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

Run the review of the integration branch at `max`. On the PR timing branch, a review at a lower level found ten defects, and a later one at `max` found twenty-three more. No review at `high` has been compared with those two, so `high` is not known to be enough. The reviewer reads `CODING_STANDARDS.md`.

## Briefs

- **Third-party APIs.** An implementer fetches the host's public documentation for any answer shape it reads and names in its report the shapes no test fixture or research ticket confirms.
- **Real data.** A brief points into the repository, and may name the exploration notes folder that the skill's exploration step sets up outside it. Those notes are about this repository and public documentation, never real data. The owner's work folder, and any path a ticket gives for notes or results outside the repository, stay out of every brief (`research-spikes.md`).
- **Checks.** The owner's local toolkit reads `.repo-toolkit.psd1` and runs a full check as `Invoke-FullCheck`. That file sets up the build of `Telltale.slnx` and its two TUnit test projects, the format check, the warnings check against `eng/warnings-baseline.txt`, the SQL line-ending check, mutation specs under `.scratch/{feature}/mutations`, and commit rules. Which of these `Invoke-FullCheck` runs was read from the file only, not checked against the toolkit. Where that command exists, an implementer runs it before reporting and says what it ran. Where it does not, the implementer runs `dotnet build`, `dotnet test` and, from the repository root, `eng/check-sql-line-endings.ps1 -RepoRoot (Get-Location).Path`, and says the full check was not run. The root is given as an absolute path because a relative one is resolved two ways until `.scratch/sql-line-endings-check-paths` is fixed.
