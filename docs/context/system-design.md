# System design

How the tool collects, stores and reports the data. Decisions with real trade-offs are ADRs in `docs/adr/`; this file holds the working design and the source-system details an implementer needs.

## Shape

Four collectors feed one SQLite store; reports read only from its metric views.

```
Jira Cloud  ─┐
GitHub      ─┤                 ┌─ raw tables (never edited)
Repo clones ─┼─► sync job ─► SQLite ─┼─ derived tables (rebuilt every run)
Session logs─┘                 └─ metric views ─► Confluence page, ad hoc SQL
```

Every collector keeps a watermark, fetches only what changed since it, and upserts by the source system's own IDs, so reruns and missed runs are harmless.

## Host and operations (laptop phase)

- Runs on demand or via Task Scheduler with "run as soon as possible after a missed start". A missed run costs nothing; what matters is a full run before each sprint's report. Corporate tooling may restrict scheduled tasks, so manual runs must be first-class.
- First backfill is large: run once, plugged in, overnight. Afterwards runs take minutes.
- Check proxy and root-certificate trust before anything else; a TLS failure on the first Jira call usually means this.
- AWS later: EventBridge-scheduled Lambda or ECS task, Secrets Manager, RDS Postgres. Lambda's 15-minute limit is fine for incremental syncs but not the backfill.

## Project structure

```
src/
  Telltale.Core/            no dependencies; model, interval math, business-day calendar,
                           stats (median, P85, Monte Carlo), identity hashing, abstractions
  Telltale.Collectors/      Jira/, GitHub/, Git/, Sessions/ (one project, folder per source)
  Telltale.Storage.Sqlite/  migrations (raw, derived, views), upserts, watermarks,
                           derived rebuild, metric views with minimum group size
  Telltale.Reporting/       queries over views, charts (PNG), Confluence page builder + publisher
  Telltale.Contracts/       session summary JSON schema, versioned
  Telltale.Cli/             composition root; the only laptop-specific project
tools/
  SessionExporter/         single-file exe participants run
  ReplayBench/             later; writes results into the same store
tests/                     Core tests carry most of the weight; collectors use recorded,
                           sanitized responses; storage uses in-memory SQLite
config/                    appsettings.json (no secrets), holidays.json
```

CLI commands: `backfill`, `sync`, `derive`, `report`, `publish`, and `run` (sync → derive → report).

Candidate libraries: `HttpClient` (no Jira SDK, ADR-0004), `Microsoft.Data.Sqlite` + Dapper, plain `git` commands, ScottPlot for PNG charts, System.CommandLine or Spectre.Console.Cli.

## Already built: Telltale.Core

Interval math (`Interval`), business-day calendar (`BusinessCalendar`), status timelines (`StatusTimeline`), sprint membership (`SprintMembership`), and per-sprint time in status (`TimeInStatus`), with 24 xUnit tests. The tests were verified with a stand-in runner, not real xUnit; the first `dotnet test` on the laptop is the real run.

Behaviors to preserve:

- Changelog entries are deduplicated by history ID and ordered by time, then numeric history ID.
- Inconsistent history produces anomaly messages but never stops the build of a timeline; the sync should log them.
- A sprint ends at its completion date; an uncompleted sprint runs to now.
- Statuses are tracked by ID, never name.
- Time in status is reported as calendar time, business time and business days.

## Jira collector

- Changed issues: JQL `updated >= "<watermark>"`, overlapping the window by a few minutes (JQL `updated` has minute precision; upserts make overlap harmless).
- Changelogs: `POST /rest/api/3/changelog/bulkfetch` with `issueIdsOrKeys` (≤1000), `fieldIds` (status + the Sprint custom field, ≤10), `maxResults` (≤10000), `nextPageToken`; stop when the token is null. Response: `issueChangeLogs[]` of `{ issueId, changeHistories[] }`; each history has `id`, `created`, `author`, `historyMetadata`, `items[]` with `field`, `fieldId`, `fieldtype`, `from`, `fromString`, `to`, `toString`. Grouped by numeric issue ID, not key.
- Changelog items: `from`/`to`/`fromString`/`toString` can be null; `fieldId` can be missing on old history (fall back to `field`); `fieldtype` is `jira` or `custom`; user fields may carry `tmpFromAccountId`/`tmpToAccountId`. Sprint items hold comma-separated sprint IDs.
- `created` timestamps use `+0000` (no colon) offsets; parse with an explicit format.
- Sprints: Agile API `/rest/agile/1.0/board/{boardId}/sprint` (start, end, complete dates, state).
- Also: `/rest/api/3/field` (find the Sprint and story point field IDs), `/rest/api/3/status` (ID → name, category), worklogs (`started` vs `created` for reliability checks).
- `historyMetadata.cause` / actor can identify automation; workflow migrations and bulk moves create status changes no human made.

## GitHub collector

GraphQL, one query per PR (reviews, review threads with comments, timeline items, size) instead of 4–5 REST calls per PR.

- Time to first review: `ready_for_review` (or creation if never draft) → first submitted review.
- Reviewer wait vs. author wait: order of reviews and pushes on the timeline.
- Review rounds: count from timeline order (review with comments → push → review), not by comparing commit IDs (force pushes rewrite them) and not by `CHANGES_REQUESTED` state (many reviewers only comment).
- Not-fixed threads: `reviewThreads` with `isResolved`, `isOutdated`, each comment's author, time and commit. A reviewer comment in the same thread after a newer push is a likely "still not fixed". It's a heuristic: spot-check a sample before reporting it.
- Exclude bot accounts from reviewer and PR counts. AI reviews posted to the PR show as `Bot` reviews.
- PR size: `additions`, `deletions`, `changedFiles`.

## Git collector

From a local clone: reverts (PR titles / commit messages starting "Revert"), and rework (lines changed again within three weeks of merge) via `git log` / `git blame`. Not practical through the API.

## Session exporter and importer

Pilot participants run a small exporter that reads local Kiro session logs and writes versioned JSON summaries (`schemaVersion`) to a shared folder (OneDrive/Teams); the laptop imports them. Contents: session ID, start/end, branch, model, skills invoked, tokens where the plan reports them. No identity, prompts or code (ADR-0005). The shared folder is the permanent copy.

**Unknown, and it blocks the exporter design:** where Kiro stores session logs locally and what they contain, including whether skill invocations are detectable. Find a sample session file first.

## Reporting

Read only from metric views. Build the Confluence page in storage format and update it through the REST API. Charts are PNGs rendered by the tool. Layout: `confluence-page.md`.

## Forecasting

Service level expectation from cycle time percentiles; aging work in progress (in-progress items compared with the SLE, also per status); Monte Carlo on daily or weekly throughput (10,000 runs, items not points), simulated in business days and converted to calendar dates. Little's Law for explanation only.
