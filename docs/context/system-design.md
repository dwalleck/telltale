# System design

How the tool collects, stores and reports the data. Decisions with real trade-offs are ADRs in `docs/adr/`; this file holds the working design and the source-system details an implementer needs.

**Built and planned.** Built: the Jira collector, the GitHub and Azure DevOps pull request collectors, the SQLite store, derive, the per-sprint markdown report and the `query` command. Each section below is marked **Built** (it describes code in the repository) or **Planned** (design for code that does not exist yet; check the repository before relying on it). Last checked against the code on 2026-11-15; the pull request collectors on 2026-11-20.

## Shape

**Built for Jira and for pull requests from GitHub and Azure DevOps; the repo-clone and session-log collectors and the Confluence page are planned.** The design is collectors feeding one SQLite store; reports read only from its metric views.

```
Jira Cloud  ─┐
GitHub, ADO ─┤                 ┌─ raw tables (never edited)
Repo clones ─┼─► sync job ─► SQLite ─┼─ derived tables (rebuilt every run)
Session logs─┘                 └─ metric views ─► Confluence page, ad hoc SQL
```

Every collector keeps a watermark, fetches only what changed since it, and upserts by the source system's own IDs, so a rerun or a missed run of `sync` is harmless. A sync removes only an issue that has left scope (one converted to an epic or a sub-task, owner decision D13); work deleted in the source stays until the next `backfill`. `backfill` is the exception to upserting: it fetches everything from the configured start date and replaces the stored issues, changes and sprints with what it fetched, in one transaction. It prints how many of each the store held before and holds now, and refuses to replace stored issues or sprints with none unless run with `--allow-empty`. Statuses are reference data and are only added and updated.

## Host and operations (laptop phase)

**Built**, except the AWS line, which is planned.

- Runs on demand or via Task Scheduler with "run as soon as possible after a missed start". A missed run costs nothing; what matters is a full run before each sprint's report. Corporate tooling may restrict scheduled tasks, so manual runs must be first-class.
- First backfill is large: run once, plugged in, overnight. Afterwards runs take minutes.
- Check proxy and root-certificate trust before anything else; a TLS failure on the first Jira call usually means this.
- AWS later: EventBridge-scheduled Lambda or ECS task, Secrets Manager, RDS Postgres. Lambda's 15-minute limit is fine for incremental syncs but not the backfill.

## Project structure

**Partly built.** Lines marked `(planned)` name projects, folders or features that do not exist yet.

```
src/
  Telltale.Core/            no dependencies; model, interval math, business-day calendar,
                           identity hashing, abstractions; Monte Carlo (planned). Medians and P85s
                           are computed in the SQL metric views, not here
  Telltale.Collectors/      Jira/, GitHub/, Ado/ (pull requests); Git/, Sessions/ (planned) (one project, folder per source)
  Telltale.Storage.Sqlite/  migrations (raw, derived, views), upserts, watermarks,
                           derived rebuild, metric views with minimum group size
  Telltale.Reporting/       queries over views, the markdown sprint report; charts (PNG) and the
                           Confluence page builder + publisher (planned)
  Telltale.Pipeline/        the steps (collect, derive, report, query) and every rule about when one may
                           run: staleness, foreign settings, the salt, the range the store covers
  Telltale.Contracts/       (planned) session summary JSON schema, versioned
  Telltale.Cli/             composition root; the only laptop-specific project
tools/                     (planned)
  SessionExporter/         single-file exe participants run
  ReplayBench/             later; writes results into the same store
eng/
  SeedStore/               fills a store from the tests' fake Jira, for trying the published executable
tests/
  Telltale.Core.Tests/     the interval, calendar and workflow rules, directly
  Telltale.Pipeline.Tests/ most of the tests: the real command tree run against a fake Jira that answers
                           with invented data in Atlassian's documented shapes (no recorded responses),
                           and a real SQLite file in a temporary folder
```

The configuration file is `config/telltale.json` in the folder the tool is run from (no secrets; the holidays are a setting in it). It is not in the repository.

CLI commands: `discover`, `secrets set`, `backfill`, `sync`, `derive`, `report`, `query`, and `run` (sync → derive → report); `publish` is planned. `discover` is run on the work laptop and writes `config/telltale.json` from the live systems: Jira site, board, project keys, a proposed role for each status on the board (taken from its Jira category), and the Sprint and story point field IDs. It lists the site's resolutions but cannot tell which mean abandoned, so it writes `abandonedResolutionIds` empty and the owner fills it in. A rerun keeps what the file already says: once the file has roles, a status new to the board is listed for the owner to give a role, and none is written for it.

Libraries: `HttpClient` (no Jira SDK, ADR-0004), `Microsoft.Data.Sqlite` + Dapper behind the repository layer, System.CommandLine for the CLI; plain `git` commands and ScottPlot for PNG charts are planned.

Secrets sit behind one secret-store interface: Windows Credential Manager on the work laptop, environment variables for development. A DPAPI-protected file, for a laptop whose policy blocks Credential Manager, is planned.

Development happens off the work laptop against synthetic fixtures only. Real Jira, GitHub and Confluence are reached only from the work laptop.

## Where metrics are computed

**Built.**

C# computes per-issue cycle times and per-sprint time in status into derived tables on every run, so business days have one implementation. The SQL metric views do only the medians, P85s (ranked with window functions; SQLite has no percentile function) and the group-size guards.

An issue counts toward its assignee at the time of the sync, hashed; assignee history is not collected, so this holds for past sprints too (owner decision D82). Unassigned issues count toward nobody.

Changelog anomalies are logged by derive (and so by run), the step that builds each issue's history, and each report's footer states how many issues had any.

## First report

**Built.** The README describes the report as it is now.

One markdown file per sprint, whole-team: delivered issue count; median and P85 cycle time by point bucket in business and calendar days; median time in each status. A baseline column is added once the baseline sprint list is pinned in config.

## Already built: Telltale.Core

**Built.**

Interval math (`Interval`), business-day calendar (`BusinessCalendar`), status timelines (`StatusTimeline`), sprint membership (`SprintMembership`), and per-sprint time in status (`TimeInStatus`), with TUnit tests on .NET 11 in `tests/Telltale.Core.Tests`.

Behaviors to preserve:

- Changelog entries are deduplicated by history ID and ordered by time, then numeric history ID.
- Inconsistent history produces anomaly messages but never stops the build of a timeline; derive logs them (a total, then each anomaly new since the last derive) and stores the full list.
- A sprint ends at its completion date; an uncompleted sprint runs to now.
- Statuses are tracked by ID, never name.
- Time in status is reported as calendar time, business time and business days.

## Jira collector

**Built**, except worklogs and the `historyMetadata` reading in the last two points, which are not collected. `docs/research/jira-cloud-rest-api.md` holds the detail, and ticket 13 the checks against a real site that are still open.

- Changed issues: JQL `updated >= "<watermark>"`, overlapping the window by 48 hours (owner decision D26: JQL reads the quoted time in the account's time zone, and account time zones span UTC-12 to UTC+14, and the search index can lag; upserts make overlap harmless).
- Changelogs: `POST /rest/api/3/changelog/bulkfetch` with `issueIdsOrKeys` (≤1000), `fieldIds` (status, the Sprint custom field and the story point field, ≤10), `maxResults` (≤10000), `nextPageToken`; stop when the token is null. Response: `issueChangeLogs[]` of `{ issueId, changeHistories[] }`; each history has `id`, `created`, `author`, `historyMetadata`, `items[]` with `field`, `fieldId`, `fieldtype`, `from`, `fromString`, `to`, `toString`. Grouped by numeric issue ID, not key.
- Changelog items: `from`/`to`/`fromString`/`toString` can be null; `fieldId` can be missing on old history (fall back to `field`); `fieldtype` is `jira` or `custom`; user fields may carry `tmpFromAccountId`/`tmpToAccountId`. Sprint items hold comma-separated sprint IDs.
- `created` timestamps use `+0000` (no colon) offsets; parse with an explicit format.
- Sprints: Agile API `/rest/agile/1.0/board/{boardId}/sprint` (start, end, complete dates, state).
- Also: `/rest/api/3/field` (find the Sprint and story point field IDs), `/rest/api/3/status` (ID → name, category), worklogs (`started` vs `created` for reliability checks).
- `historyMetadata.cause` / actor can identify automation; workflow migrations and bulk moves create status changes no human made.

### Workflow mapping

**Built.**

The board's statuses, in order: New, Approved, Committed (not started; development cannot start until Committed), Development, Review, QE, Blocked, Done, Shipped. Statuses are matched by ID, and each has a role (not started, work, blocked, done) in `statusRoles`: `discover` proposes one from the status's Jira category, and the owner corrects it in the file. There is no other override of a Jira category. The abandoned resolution IDs ("Won't Do" is expected, to be confirmed) are the owner's to fill in: `discover` writes the list empty and never changes it, and while it is empty an issue closed as abandoned in a done status counts as delivered.

- Cycle start: first entry into Development, or into Review or QE if Development was skipped. Blocked never starts a cycle, but time in Blocked after the start counts. Jira's history does not say which stage a blocked issue was in, so Blocked time is reported as Blocked.
- Cycle end: last entry into the done category, which is Done. Done → Shipped is a move inside the category and does not move the end. Time in Shipped is kept in status intervals for later use and is not analysed now.
- Abandoned: Done with an abandoned resolution. Developers cannot cancel issues themselves.
- Reopen: any move out of the done category.
- Scope: every issue type except epics and sub-tasks; no split by type (ADR-0006).
- Point bucket: the story point value at cycle start, from the story point field's history.
- One team time zone and one holiday list.

## Pull request collectors (GitHub, Azure DevOps)

**Built for pull request timing** (`.scratch/github-pr-timing/spec.md`). The GitHub collector reads each configured repository's pull requests and their submitted reviews over the REST API, and the members of the Ops team when one is configured. The Azure DevOps collector reads each configured repository's pull requests and their threads. Neither stores a title, branch, description or login; an author is stored only as a salted hash. The measures are the waits from first opening to first code review, first code approval and merge (`PullRequestTiming.Measures`), defined in `GLOSSARY.md` under "Pull request wait". `code-map.md` lists what a new host or measure touches.

**Planned** for GitHub: the figures below. This design came before the build and proposed GraphQL, one query per PR (reviews, review threads with comments, timeline items, size), instead of 4–5 REST calls per PR; the built collector uses REST. The built wait to first code review starts at first opening, not at `ready_for_review`.

- Reviewer wait vs. author wait: order of reviews and pushes on the timeline.
- Review rounds: count from timeline order (review with comments → push → review), not by comparing commit IDs (force pushes rewrite them) and not by `CHANGES_REQUESTED` state (many reviewers only comment).
- Not-fixed threads: `reviewThreads` with `isResolved`, `isOutdated`, each comment's author, time and commit. A reviewer comment in the same thread after a newer push is a likely "still not fixed". It's a heuristic: spot-check a sample before reporting it.
- Exclude bot accounts from reviewer and PR counts. AI reviews posted to the PR show as `Bot` reviews.
- PR size: `additions`, `deletions`, `changedFiles`.

## Git collector

**Planned.**

From a local clone: reverts (PR titles / commit messages starting "Revert"), and rework (lines changed again within three weeks of merge) via `git log` / `git blame`. Not practical through the API.

## Session exporter and importer

**Planned.**

The owner already has a session export script that participants will run; its output becomes the session summary format if it meets ADR-0005 and records the branch at session time and explicitly invoked skill names. The first ticket of the sessions spec brings its field list and one sanitized sample into the repo, and runs one throwaway Kiro session on the work laptop to check what is stored. Kiro (CLI 3.1.0) keeps sessions in `data.sqlite3` or under `~/.kiro/sessions` depending on the agent engine, does not store the git branch, has no confirmed stored record of skill invocations or per-session credits, and auto-deletes old sessions.

As originally sketched: participants run a small exporter that reads local Kiro session logs and writes versioned JSON summaries (`schemaVersion`) to a shared folder (OneDrive/Teams); the laptop imports them. Contents: session ID, start/end, branch, model, skills invoked, tokens where the plan reports them. No identity, prompts or code (ADR-0005). The shared folder is the permanent copy.

**Still unverified:** the stored session JSON, Windows paths, and whether model and credits can be read per session. All need a real session on the work laptop.

## Reporting

**Reading only from metric views is built; the Confluence page and the charts are planned.**

Read only from metric views. Build the Confluence page in storage format and update it through the REST API. Charts are PNGs rendered by the tool. Layout: `confluence-page.md`.

## Forecasting

**Planned.**

Service level expectation from cycle time percentiles; aging work in progress (in-progress items compared with the SLE, also per status); Monte Carlo on daily or weekly throughput (10,000 runs, items not points), simulated in business days and converted to calendar dates. Little's Law for explanation only.
