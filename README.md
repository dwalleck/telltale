# Telltale

Telltale is a .NET command-line tool that collects a team's delivery data into one local SQLite store and reports team-level flow metrics per sprint.

It reports on the team's work, never on individuals. Person fields are hashed before they are stored, and by default a figure that covers too few people is withheld.

## Status

The Jira path works end to end: collect a Scrum board, derive the metrics, write a sprint report. Pull request timing from GitHub and from Azure DevOps (the waits for a first code review, for a first code approval and for merge, and the pull requests left without a first code review) is built and has run against synthetic data only. Other review metrics, AI agent session logs, quality measures and Confluence publishing are planned and not built.

The first owner-assisted run on the managed Windows laptop produced a real Jira sprint report. The Windows test suite passes; detailed real-site edge-case checks remain open in [ticket 13](.scratch/jira-flow-report/issues/13-first-real-run-on-work-laptop.md).

## What a report shows

One markdown file per sprint, with:

- **Delivered issues:** how many issues the sprint delivered.
- **Cycle time by point bucket:** median and 85th percentile, in business days, for issues grouped by story points.
- **Time in status:** how many issues spent time in each workflow status, their median time and total business days. Totals sum issue time within the sprint and membership windows, not effort.
- **Baseline columns:** cycle-time medians and P85s and time-in-status medians pooled over a fixed list of earlier sprints, for comparison.
- **Pull request timing:** when the configuration has the `github` setting, the `ado` setting (Azure DevOps) or both, how many pull requests of the listed repositories got their first code review in the sprint, how many their first code approval and how many were merged in it, each with the median and the P85 of the wait since the pull request was opened, in elapsed days and in business days; and under that, how many pull requests had no first code review when the sprint closed. With both settings each host has its own rows, beside combined ones. These figures are not subject to the minimum group size. See [Pull requests](#pull-requests).
- **Method note and data quality footer:** how the figures were computed and what was left out.
- **Brief visit lines in the footer:** when the configuration lists statuses under `briefVisitThresholds`, one line for each listed status that has a row in the sprint's time in status table, or had one before any brief visit was left out, says how many of the issues in the row before anything was left out had a visit shorter than the status's threshold, or reads `suppressed`. A listed status that had no such row has no line, and a sprint whose tables are withheld has none. No figure is changed because of a brief visit unless `briefVisitTreatment` says so. See [Brief visit thresholds](#brief-visit-thresholds).
- **A cycle time line in the footer:** when a listed status has the role `work`, the footer's last line says how many of the sprint's delivered issues have a cycle that starts with a brief visit, or reads `suppressed`.
- **What leaving brief visits out adds:** when `briefVisitTreatment` sets `exclude` for time in status or for cycle time, a line under the heading says which of the two were adjusted, and the method note says what was left out, why, and what that does not do. With `exclude` for time in status, the row of every listed status is marked `(adjusted: see the footer)` and each brief visit line goes on to the number of issues left out of the row. With `exclude` for cycle time, the cycle time table has a note under it. See [Brief visit treatment](#brief-visit-treatment).

Time is counted in business days in the team's time zone, skipping weekends and the configured holidays. The definitions are in [`GLOSSARY.md`](GLOSSARY.md).

## Privacy rules

Reporting no per-person results is a hard constraint of the design. Withholding small groups is a default that the configuration file can change (see [ADR-0003](docs/adr/0003-pseudonymized-aggregates-only.md), and [ADR-0008](docs/adr/0008-minimum-group-size-is-a-configurable-default.md), which amends it):

- **No per-person results.** Jira account IDs, and the account IDs of pull request authors, are hashed with a secret salt before storage. Names, emails, logins and issue keys are not stored, and nothing is stored of who reviewed a pull request. No setting changes this.
- **Small groups are withheld by default.** A sprint needs a team of at least five, and a figure needs at least three distinct assignees behind it. Anything short of that reads `suppressed`.
- **The two numbers are a setting.** `minimumGroupSize` in the configuration file sets other numbers. A number of zero turns its check off, and with both at zero nothing is withheld. See [Minimum group size](#minimum-group-size).
- **With `assigneesBehindAFigure` at zero or one, a figure in a report can be the work of a single person.** The people are still not named, but anyone who knows who did the work can tell whose figure it is.
- **A report written below the defaults says so.** When either number is below its default, a line under the report's heading gives the numbers in force, or says that small-group suppression is off. The method note of every report states the rule the report was written under.
- **The rule lives in the database views.** `report` and `query` both read through them, so neither can show a figure that the numbers in force withhold.
- **Pull request figures are not withheld for group size.** The two pull request views apply neither number, whatever `minimumGroupSize` says, and still give nothing per person. See [Pull requests](#pull-requests).
- **Opening the database file in another program bypasses the rule.** That program reads every table.
- **Whoever edits the configuration file is trusted with it.** The rule is applied to each brief visit count on its own. Someone who derives twice under different `briefVisitThresholds` can subtract one shown count from the other, and the difference can rest on fewer people than the numbers in force allow. Someone who derives twice under different values of `briefVisitTreatment` can set a figure under `count` beside the same figure under `exclude`, which says something about the issues left out even when their count is withheld. See [Brief visit thresholds](#brief-visit-thresholds) and [Brief visit treatment](#brief-visit-treatment).

## Requirements

- .NET 11 SDK (`global.json` pins 11.0.100 or a later feature band).
  If an installed release SDK is not found, check `where.exe dotnet`: a user-level toolchain can shadow the system installation. In PowerShell, use `& 'C:\Program Files\dotnet\dotnet.exe' build` (or `test`) when that is where the release SDK is installed; do not lower the repository's SDK pin.
- A Jira Cloud site with a Scrum board, and an API token for an account that can read it.
- Windows for real use: secrets are kept in Windows Credential Manager. On macOS and Linux the tool reads secrets from environment variables, which is meant for development only.

## Install

Clone the repository and publish the command-line project:

```
git clone https://github.com/dwalleck/telltale.git
cd telltale
dotnet publish src/Telltale.Cli -c Release -o <install folder>
```

That writes a `telltale` executable (`telltale.exe` on Windows) into the install folder. Add the folder to your `PATH`, or call the executable by its full path.

To run from source without publishing, use `dotnet run --project src/Telltale.Cli -- <command>` in place of `telltale <command>`.

### Upgrading

A store needs one `telltale derive` after upgrading to any version whose derived tables, metric views or rules that fill them differ from the ones that built it. The tool tells the two apart by the text of its SQL scripts and by the compiled code of the rules (the `Telltale.Core` and store assemblies), so this includes a version that changed only a comment in one of them, or a part of that code `derive` does not use. Until then `report` and `query` stop with:

```
The derived data in the store was built by another version of this tool. Run derive first.
```

`derive` rebuilds the derived data from the raw data already in the store. It calls no Jira, and no `backfill` is needed. `telltale run` includes `derive`, so the next `run` does the same.

### Where files go

Telltale reads and writes files under the directory you run it from:

| Path | Content |
| --- | --- |
| `config/telltale.json` | The configuration file. It holds no secrets. |
| `data/telltale.db` | The SQLite store. |
| `reports/sprint-<id>.md` | One report per sprint. |

Run it from a working folder outside your clone of this repository, so the store and the reports cannot be committed by accident.

## First run

1. **Store the secrets.** Each value is typed at a prompt and never shown.

   ```
   telltale secrets set jira-email
   telltale secrets set jira-api-token
   telltale secrets set hashing-salt --generate
   ```

2. **Write the configuration file.** `discover` reads the Jira site and fills in the board's projects, field IDs and statuses.

   ```
   telltale discover --site-url https://example.atlassian.net/ --board 42 --time-zone America/Chicago --backfill-start 2026-07-01
   ```

3. **Review `config/telltale.json`.** Give every status a role (`not started`, `work`, `blocked` or `done`); at least one must be `done`. List the resolutions that mean an issue was abandoned, the company holidays, and the baseline sprint IDs. `discover` wrote `minimumGroupSize` with its default numbers; leave it, or see [Minimum group size](#minimum-group-size). To count brief visits, add `briefVisitThresholds` by hand; see [Brief visit thresholds](#brief-visit-thresholds). To leave them out of time in status or cycle time, add `briefVisitTreatment` as well; see [Brief visit treatment](#brief-visit-treatment).

4. **Fetch the history.**

   ```
   telltale backfill
   ```

5. **Write a report.**

   ```
   telltale run
   ```

After that, `telltale run` is the only command needed day to day.

## Commands

| Command | What it does | Calls Jira |
| --- | --- | --- |
| `discover` | Reads the Jira site and writes the configuration file. | yes |
| `secrets set` | Stores one secret. | no |
| `backfill` | Fetches everything since the backfill start date and replaces what was stored. | yes |
| `sync` | Fetches what changed since the last sync or backfill. | yes |
| `derive` | Rebuilds all derived data from the stored raw data. | no |
| `report` | Writes one sprint's report from the derived data. | no |
| `run` | `sync`, then `derive`, then `report`. | yes |
| `query` | Runs one SQL `SELECT` over the metric views. | no |

`telltale <command> --help` lists every option.

### `discover`

Writes `config/telltale.json` from what the Jira site says about the board. The first run needs `--site-url`, `--board`, `--time-zone` and `--backfill-start`. `--board` takes the board's numeric ID, the number after `/boards/` in its address, and `--time-zone` takes an IANA ID such as `America/Chicago`. Pass `--story-point-field` or `--sprint-field` when Jira lists none or several candidates.

A rerun keeps what the file already says, and saves a backup of the file before rewriting it.

A new file gets `minimumGroupSize` with five and three. A rerun never changes the setting: it keeps the numbers the file has, and does not add the setting to a file without it.

`discover` never writes `briefVisitThresholds`. A rerun keeps the setting as the file has it and never changes or removes it. A rerun on a file whose `briefVisitThresholds` would be refused writes nothing and leaves the file untouched. It prints `discover did not write <file>, because run would refuse it:` followed by the message `run` would give.

`discover` never writes `briefVisitTreatment` either. A rerun keeps the setting as a value and in its place in the file, and never changes or removes it. It does not keep the file byte for byte: a rerun that changes anything else rewrites the file in the tool's layout and drops comments, and says so. A rerun on a file whose `briefVisitTreatment` would be refused writes nothing, leaves the file untouched and prints the same line, followed by the message `run` would give. It prints no part of a key written under the setting, and no part of a key written twice anywhere in the file: what was typed as a key could be a secret pasted into the wrong place. The one name it gives is the tool's own: a part of the setting written twice is refused as `run` refuses it, by the part's name. When a file has more than one fault, `discover` and `run` may each report a different one: each checks in its own order and stops at the first. Each message is true of the file, and correcting one fault brings the next.

### `secrets set <jira-email|jira-api-token|hashing-salt|github-token|ado-token>`

Stores one secret in Windows Credential Manager. The value is asked for at a prompt, or piped in with `--stdin`; it is never taken as an argument.

`github-token` is needed only when the configuration file has the `github` setting. It is a token that can read the pull requests of the listed repositories and the members of the Ops team; the tool only reads. Without the setting no command asks for it.

`ado-token` is needed only when the configuration file has the `ado` setting. It is an Azure DevOps personal access token with the Code (Read) scope for the listed repositories; the tool only reads. Without the setting no command asks for it. Neither host's token is needed for the other host.

- `--generate` stores a new random hashing salt.
- `--replace` is required to change a salt once people have been hashed with it. Every stored person hash stops matching, so run `backfill` afterwards.

Off Windows, `secrets set` cannot store anything. Set `TELLTALE_JIRA_EMAIL`, `TELLTALE_JIRA_API_TOKEN` and `TELLTALE_HASHING_SALT` in the environment, and `TELLTALE_GITHUB_TOKEN` when the `github` setting is there. Set `TELLTALE_ADO_TOKEN` when the `ado` setting is there.

### `backfill`

Fetches every issue in the board's projects updated on or after `backfillStart`, plus the board's sprints, and replaces the stored raw data with the result. Use it for the first fetch, after changing a collection setting, or to drop issues that were deleted or moved in Jira.

It refuses to replace a non-empty store with an empty fetch, because that usually means a wrong setting or lost access. `--allow-empty` overrides this.

With the `github` setting, `backfill` and `sync` then read pull requests: the list of each listed repository's pull requests, open, merged or closed, most recently updated first, and the submitted reviews of each pull request that is new to the store or whose list entry changed (its merge, its close or its last update). A pull request listed exactly as it is stored costs no request of its own. `sync` stops reading the list at the first page on which nothing is new; `backfill` reads the list to its end. So the first read costs one request per pull request, bounded by `github.collectFrom`, and a later `sync` costs one per pull request that changed. Stored pull requests are updated and never removed, by `backfill` either, with one exception: after `github.apiUrl` changed, a `backfill` removes what was read from the old address (see "Where a host's pull requests were read from" under [Pull requests](#pull-requests)). If GitHub cannot be read the command fails after the Jira data is stored, and the pull requests already in the store stay as they were. When the failure comes after the reviews of some pull requests were read, those pull requests are stored with their reviews, the message says how many, and GitHub is recorded as a host whose last read failed, so its figures are not shown as resting on a full read. The next `sync` or `backfill` reads the whole list and asks only for the reviews it does not have yet, so a large first read can be finished over several runs. A single pull request whose reviews GitHub refuses (403 or 404), or gives in a form the tool cannot read, does not stop the read: it is stored without its reviews and asked for again at every sync (see "Pull requests without a first code review"). When GitHub's rate limit is used up (it answers 403 or 429 with `x-ratelimit-remaining: 0`), the command waits if requests are taken again within five minutes; otherwise it stops and names the time to run it again. The time is the one in `x-ratelimit-reset`, or the end of the wait the answer's `Retry-After` asks for when it has a readable one. This rule is GitHub's alone: Azure DevOps sends headers of the same names with another meaning, so its throttled answers (429), like Jira's, are waited out for as long as `Retry-After` says (see `sync`). See [Pull requests](#pull-requests).

With the `ado` setting, `backfill` and `sync` read Azure DevOps the same way, with these differences. The list of a repository's pull requests does not say when one last changed, so both commands read the whole list, and both ask for the threads of every pull request that is still open, at every run. A completed or abandoned pull request is asked for once after it closed and never again. `ado.collectFrom` leaves out pull requests closed before the date. If Azure DevOps cannot be read the command fails after the Jira data is stored, and what is already in the store stays as it was. After `ado.organization` or `ado.apiUrl` changed, a `backfill` removes what was read from the old organization or address, as it does for GitHub.

### `sync`

Fetches what changed in Jira since the last `sync` or `backfill` and adds it to the store. It removes one thing: a stored issue that has since become an epic or a sub-task, which is out of scope. Work deleted or moved in Jira stays until the next `backfill`.

A request that fails in a way that can pass by itself is sent again up to five times: Jira answers 408, 500, 502, 503 or 504, or, once Jira has answered some request of the same command, the connection drops, is reset or cannot be made (three times when no answer comes in time, since each try first waits out the timeout). The wait is what the answer's `Retry-After` asks for, or else starts at 5 seconds and doubles. A failure on the way to Jira before it has answered anything is most likely the proxy or the certificate, so it ends the command at once. A rate-limited request (429) is sent again up to five times, after the wait Jira asks for, counted apart from the others. This applies to `backfill` and `discover` too. A command spends at most 15 minutes on failures on each host it reads (Jira, GitHub, Azure DevOps), the failed requests included, so up to 45 minutes when all three are configured and all three fail that way, and a run that stops at a limit ends with "Run the command again later." A fetch from Jira is stored whole or not at all, so a command that fails on Jira has changed nothing. A pull request host is read after Jira's fetch is stored: when one fails, the Jira data is in the store, the store records that the host's last read failed, and a GitHub read that stopped part-way keeps the pull requests it had read whole (see `backfill`).

### `derive`

Drops and rebuilds every derived table and view from the raw data: issue cycles, time in each status, sprint windows and the metric views. Run it after changing `statusRoles`, `abandonedResolutionIds`, `holidays`, `timeZone`, `baselineSprintIds`, `minimumGroupSize`, `briefVisitThresholds` or `briefVisitTreatment`. It calls no Jira.

### `report [--sprint <id>]`

Writes `reports/sprint-<id>.md` from the derived data. Without `--sprint` it reports on the most recently completed sprint. It refuses to run when the derived data is older than the raw data, was built under different settings or was built by another version of the tool, and says to run `derive`.

### `run [--sprint <id>]`

Runs `sync`, `derive` and `report` in one step. When Jira cannot be read the run stops there and writes no report. When only a pull request host cannot be read, the run still derives and writes the report, which marks that host's rows as resting on a read that failed, and then ends with the host's message and exit code 1.

### `query "<sql>"`

Runs one `SELECT` over the metric views and prints the rows as a markdown table. Use `--file <path>` to read the SQL from a file. It reads only the metric views, so withheld figures stay withheld. The two pull request views, `pr_timing_per_sprint` and `pr_without_review_per_sprint`, withhold nothing for group size: their figures are not subject to `minimumGroupSize` (see [Pull requests](#pull-requests)), and they give no figure per person.

```
telltale query "SELECT * FROM delivered_per_sprint"
```

The main views are `delivered_per_sprint`, `cycle_time_per_sprint_and_bucket`, `time_in_status_per_sprint`, `baseline_cycle_time_per_bucket`, `baseline_time_in_status`, `data_quality`, `brief_visits_per_sprint` and `cycle_starts_with_brief_visit_per_sprint`. `minimum_group_size` holds the two numbers the derived data was built under, and `brief_visit_treatment` the treatment of brief visits it was built under.

`brief_visits_per_sprint` has one row per sprint and status listed under `briefVisitThresholds` that had a row in the sprint's time in status table before any brief visit was left out: the threshold in minutes, the count, and a `suppressed` flag. A withheld count is `NULL` with `suppressed` at 1. While brief visits are counted, that is every listed status with a row in the time in status table. Under `exclude` for time in status, a status whose every issue was left out has a row here and none in `time_in_status_per_sprint`. Without `briefVisitThresholds` the view has no rows.

The view has two more columns, `left_out_issue_count` and `left_out_suppressed`: the number of issues left out of the status's time in status row, and its flag. Under `exclude` for time in status, a withheld number is `NULL` with `left_out_suppressed` at 1. Under `count` nothing is left out: the number is empty (`NULL`) beside a flag of 0.

`cycle_starts_with_brief_visit_per_sprint` has one row per sprint when a listed status has the role `work`, and no rows otherwise: `issue_count`, the number of the sprint's delivered issues whose cycle starts with a brief visit, and a `suppressed` flag. The number is the same under either treatment.

`brief_visit_treatment` has one row with two columns, `time_in_status` and `cycle_time`, each `count` or `exclude`. A figure of `time_in_status_per_sprint` or `cycle_time_per_sprint_and_bucket` looks the same under either treatment, so read this view beside it to know which one it was measured under.

## Configuration file

`config/telltale.json` is JSON with comments and trailing commas allowed. A setting the tool does not know is refused, which catches typos and a secret pasted in by mistake.

```jsonc
{
  "jira": {
    "siteUrl": "https://example.atlassian.net/",
    "boardId": 42,
    "projectKeys": ["PAY"],
    "sprintFieldId": "customfield_10020",
    "storyPointFieldId": "customfield_10016",
    "backfillStart": "2026-07-01"
  },
  // Status ID to role, in board order. Reports list statuses in this order.
  "statusRoles": {
    "10000": "not started",
    "10001": "work",
    "10002": "blocked",
    "10003": "done"
  },
  // Resolutions that mean an issue was closed without being done.
  "abandonedResolutionIds": ["10004"],
  "timeZone": "America/Chicago",
  "holidays": ["2026-11-26", "2026-12-25"],
  // The earlier sprints to compare against. Choose them once and leave the list alone.
  // Optional; without it reports show no baseline.
  "baselineSprintIds": ["41", "42", "43"],
  // The smallest team a sprint needs, and the fewest distinct assignees a figure needs behind it.
  // Zero turns that check off. Optional; without it the numbers are five and three.
  "minimumGroupSize": { "team": 5, "assigneesBehindAFigure": 3 },
  // Status ID to the shortest believable stay, in minutes. Optional; without it no status is checked.
  "briefVisitThresholds": {
    "10001": 5,
    "10002": 0.5
  },
  // What is done with brief visits. Optional. Each part is "count" or "exclude"; a part left out is "count".
  "briefVisitTreatment": {
    "timeInStatus": "exclude",
    "cycleTime": "count"
  },
  // Where pull requests are read from. Optional; without it nothing is asked of GitHub.
  "github": {
    "repositories": ["example-org/payments"],
    "opsTeam": "example-org/ops",
    "apiUrl": "https://github.example.com/api/v3/",
    "collectFrom": "2026-07-01"
  },
  // Where pull requests are read from on Azure DevOps. Optional; without it nothing is asked of Azure DevOps.
  "ado": {
    "organization": "example-org",
    "repositories": ["Payments/payments-api"],
    "automationIdentities": ["00000000-0000-0000-0000-000000000000"],
    "collectFrom": "2026-07-01"
  }
}
```

### Setting formats

| Setting | Format | Example |
| --- | --- | --- |
| `jira.siteUrl` | An absolute `https://` address. | `"https://example.atlassian.net/"` |
| `jira.boardId` | A number, not quoted. It is the number in the board's address, after `/boards/` (or `rapidView=` on older pages). It is not the project key. | `42` |
| `jira.projectKeys` | A list of project keys as strings. `discover` fills it in from the board. | `["PAY"]` |
| `jira.sprintFieldId`, `jira.storyPointFieldId` | A Jira field ID as a string. | `"customfield_10020"` |
| `jira.backfillStart` | A date as `"yyyy-MM-dd"`. | `"2026-07-01"` |
| `statusRoles` | Status ID (a string of digits) to role: `"not started"`, `"work"`, `"blocked"` or `"done"`. | `"10001": "work"` |
| `abandonedResolutionIds` | A list of resolution IDs, each a quoted string of digits. May be empty. | `["10004"]` |
| `timeZone` | An IANA time zone ID of the form `Area/City`. | `"America/Chicago"` |
| `holidays` | A list of dates, each as `"yyyy-MM-dd"`. May be empty. | `["2026-12-25"]` |
| `baselineSprintIds` | A list of sprint IDs, each a quoted string of digits. Optional. | `["41", "42"]` |
| `minimumGroupSize` | An object with the two numbers `team` and `assigneesBehindAFigure`. Each is a whole number of zero or more, written in plain digits, not quoted, with no fraction and no exponent, and no greater than 2,147,483,647. Optional; when it is present both numbers are required. | `{ "team": 5, "assigneesBehindAFigure": 3 }` |
| `briefVisitThresholds` | An object of status ID (a string of digits, as in `statusRoles`) to threshold: a number of minutes greater than zero, not quoted. A fraction is allowed. Each status must be in `statusRoles` with a role other than `done`, and may be listed once. Optional; may be empty. | `{ "10001": 5, "10002": 0.5 }` |
| `briefVisitTreatment` | An object with the parts `timeInStatus` and `cycleTime`, each `"count"` or `"exclude"`. Optional, and so is each part: a part left out is `"count"`. `"count"` changes no figure. `"exclude"` for `timeInStatus` leaves the time of every brief visit to a listed status out of time in status. `"exclude"` for `cycleTime` gives a delivered issue whose cycle starts with a brief visit no cycle time. `"exclude"` needs a status listed in `briefVisitThresholds`, and for `cycleTime` one with the role `work`. | `{ "timeInStatus": "exclude", "cycleTime": "count" }` |
| `github` | An object. Optional. `repositories` is required: a list of repositories, each as `"owner/name"`, and only these are read. `opsTeam` is optional: the team whose reviews are not code reviews, as `"organization/team-slug"`. `apiUrl` is optional: the absolute `https://` address of the REST API, for GitHub Enterprise Server the host's address followed by `/api/v3/`; without it the address is `https://api.github.com/`. After changing the address, run `backfill`: every other command refuses until then. `collectFrom` is optional: a date as `"yyyy-MM-dd"`; a pull request last updated before the start of that day (UTC) is not collected. It bounds last activity, not opening, so a pull request opened earlier and reviewed or merged since is still read. Without it there is no bound. The date says what is brought into the store, not what is kept up to date: after the date is moved forward, a pull request the store already holds is still compared with the list and updated, however old, so one stored as open that closed before the new date is stored as closed. To reach such pull requests a read goes on past a page of pull requests too old to collect while stored ones are still to come, which can cost list pages and no review requests. After moving the date back, run `backfill`: a `sync` stops at the first page of unchanged pull requests and would not reach the older ones. A sprint that began before the date is covered only in part and is marked so (see "Coverage" under [Pull requests](#pull-requests)). The token is a secret and has no place here. | `{ "repositories": ["example-org/payments"] }` |
| `ado` | An object. Optional, and independent of `github`: either may be there without the other. `organization` is required: the organization's name as it stands in its address. `repositories` is required: a list of repositories, each as `"project/repository"`, and only these are read. `automationIdentities` is optional: a list of identity IDs (GUIDs) of accounts that act only as automation, such as a code scanner's; every comment and vote of such an account is automation. `apiUrl` is optional: the absolute `https://` address the organization is under, for Azure DevOps Server; without it the address is `https://dev.azure.com/`. After changing `organization` or `apiUrl`, run `backfill`: every other command refuses until then. The tool asks for version 7.1 of the REST API, which Azure DevOps Services has and Azure DevOps Server has from release 2022.1 on: an older Server refuses every request, and the command fails saying so. `collectFrom` is optional: a date as `"yyyy-MM-dd"`; a pull request closed before the start of that day (UTC) is not collected, and one that is still open is collected whenever it was opened. As on GitHub, a pull request the store already holds is kept up to date after the date is moved forward past its closing. A sprint that began before the date is covered only in part and is marked so. | `{ "organization": "example-org", "repositories": ["Payments/payments-api"] }` |

Things that are refused when the file is loaded, with a message that names the file:

- **An ID written as a number** in `baselineSprintIds` or `abandonedResolutionIds` (`[41, 42]`), or `boardId` written as a string (`"42"`).
- **A sprint's name** in place of its ID. The ID is the number in the sprint's address in Jira (`sprint=41`).
- **A time zone abbreviation or offset**, such as `CST` or `UTC-6`. Neither carries the daylight-saving rules that business days depend on. US zones are `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix` and `America/Los_Angeles`.
- **Any other date form**, such as `"11/26/2026"` or a date with a time.
- **A `minimumGroupSize` number that is not a whole number of zero or more in plain digits**: a negative number, a fraction (`2.5`, and `5.0` as well), an exponent (`1e1`), a number greater than 2,147,483,647, a quoted number (`"5"`), `null`, `true` or `false`. The message names the number, for example `minimumGroupSize.team must be written as a whole number of zero or more.`
- **`minimumGroupSize` with one of its numbers missing, or written as anything but an object**, such as `null`, `false`, `0` or `"off"`. To turn the rule off, write both numbers as zero. For five and three, leave the setting out.
- **Any other name inside `minimumGroupSize`**, including `team` with another capitalization: `minimumGroupSize.cohort is not a setting the tool knows.`
- **A `minimumGroupSize` number written twice**: `minimumGroupSize.team is written twice.`
- **A status under `briefVisitThresholds` that is not in `statusRoles`**, which catches a mistyped status ID: `briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.` The messages of the setting's own rules, the six listed here, never quote a status ID or a threshold. A file that is not valid JSON (a status ID written without quotes, for example) is refused by every command, and by `discover`, with the line and the position of the fault and no text from the file: `it is not JSON the tool can read, and the fault is at line 12, position 5`. `discover` does not quote a key that is written twice, wherever in the file it is: it says that a key is written twice, and under which setting when the setting is one the tool knows. A part of `briefVisitTreatment` written twice gets the message `run` gives for it.
- **A status with the role `done` under `briefVisitThresholds`**: `briefVisitThresholds: a status with the role done cannot be listed, and one is. A done status has no time in status row.`
- **A threshold that is not a number greater than zero**: zero, a negative number, a quoted number (`"5"`), `null`, `true` or `false`, a list or an object. The message is `briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.`
- **A threshold too large or too small to be a length of time**: `1e300` is refused as too large, and `1e-9` because it `rounds to no time at all`. The shortest length of time the tool measures is a ten-millionth of a second.
- **`briefVisitThresholds` written as anything but an object**, such as `null`, `false`, `5`, `"off"` or a list: `briefVisitThresholds must be an object of status IDs, each with its threshold in minutes; leave the setting out to check no status.`
- **A status listed twice under `briefVisitThresholds`**: `briefVisitThresholds lists a status twice.`
- **`briefVisitTreatment` written as anything but an object**, such as `null`, `false` or `"exclude"`: `briefVisitTreatment must be an object with the parts timeInStatus and cycleTime; leave the setting out to count brief visits and change no figure.` The messages of this setting's rules, the seven listed here, never quote what the file holds.
- **Any other name inside `briefVisitTreatment`**, including a part with another capitalization: `briefVisitTreatment has a part the tool does not know. The parts are timeInStatus and cycleTime.`
- **A part of `briefVisitTreatment` written twice**: `briefVisitTreatment.timeInStatus is written twice.`
- **A part that is not `"count"` or `"exclude"`**, including `null`, `true` or a number: `briefVisitTreatment.cycleTime must be written as "count" or "exclude".`
- **`timeInStatus` as `exclude` with no status listed in `briefVisitThresholds`**: `briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.`
- **`cycleTime` as `exclude` with no status listed in `briefVisitThresholds`**: `briefVisitTreatment.cycleTime is exclude, and no status is listed in briefVisitThresholds, so no cycle would be left out. List a status with the role work there, or write count.`
- **`cycleTime` as `exclude` when no listed status has the role `work`**: `briefVisitTreatment.cycleTime is exclude, and no status listed in briefVisitThresholds has the role work, so no cycle could start with a brief visit and none would be left out. List a status with the role work there, or write count.`

About `holidays`:

- List one entry per day. There is no range form and no "every year" form, so list each year you need.
- Cover the period of the baseline sprints as well as the current one; their cycle times are counted in business days too.
- Weekends are already left out. When a holiday falls on a weekend and the company observes it on a weekday, list the weekday.

### Minimum group size

`minimumGroupSize` holds the two numbers of the rule that withholds figures resting on few people:

- `team`: the fewest distinct assignees a sprint needs. A sprint with fewer reads `suppressed` for its delivered count and both tables.
- `assigneesBehindAFigure`: the fewest distinct assignees among the issues one figure is computed from. It governs two things:
  - A row of either table with fewer assignees behind it keeps its issue count and reads `suppressed` for its figures.
  - A sprint with fewer assignees among its delivered issues reads `suppressed` for its delivered count, and its cycle time table reads `suppressed` with no rows and no counts. Its time in status table keeps its rows.

Without the setting the numbers are five and three, and writing five and three gives the same reports as leaving it out. The numbers apply to the baseline sprints, to the footer's two counts of the whole store and to its brief visit counts as well.

To change the rule, write other numbers. Any two whole numbers from zero to 2,147,483,647 are accepted, above the defaults as well as below, and the number behind a figure may be greater than the team number.

To turn one check off, write zero for it. With both at zero the rule is off and no report reads `suppressed` anywhere:

```jsonc
"minimumGroupSize": { "team": 0, "assigneesBehindAFigure": 0 }
```

One is not zero. With `assigneesBehindAFigure` at 1, a row made only of unassigned issues is still withheld, and so is a footer count with nobody behind it.

After changing the setting, run `telltale derive`, or `telltale run`, which includes it. Until then `report` and `query` stop with `minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.` No `backfill` is needed.

What a report then shows:

- **Either number below its default:** a line under the heading gives both numbers and says what that allows. With four and two the whole line is `Minimum group size in this report: a minimum team of four (below the default of five) and a minimum of two assignees behind a figure (below the default of three). A sprint with a team smaller than five can be reported, and a figure may rest on the work of fewer people than the default allows.`
- **Both numbers zero:** the line reads `Small-group suppression is off: a figure in this report may rest on the work of one or two people.`
- **Both numbers at or above their defaults:** no line.
- **In every case:** the method note describes the rule with the numbers in force, or says that small-group suppression is off.

The setting changes only this rule. Person fields are still hashed before they are stored, and no report or view is grouped by person.

### Pull requests

With the `github` setting, each sprint report has a **Pull request timing** section, and the metric view `pr_timing_per_sprint` has one row per sprint and measure. Azure DevOps is read with the `ado` setting, which has its own subsection below; with both, the view has a row per sprint, measure and host scope (see "More than one host"). With neither setting the report has no such section and the view has no rows.

Each row has the number of pull requests (`sample_count`) and four figures: the median and the P85 of the waits in elapsed days (`median_elapsed_days`, `p85_elapsed_days`) and in business days (`median_business_days`, `p85_business_days`). The median and the nearest-rank P85 are picked as for every other figure of the report, for each clock on its own, from the waits before any rounding; only the report rounds, to one decimal. A row with no pull request has no figure: the report says `none` and the view gives `NULL`, never zero. The view has one more row per measure with no sprint (`sprint_id` is `NULL`): the waits counted in no sprint. The report gives their number under the table when there are any.

- **What is counted.** Every pull request of the listed repositories, whether or not it is linked to a Jira issue and whoever opened it. In a shared repository that includes other teams' pull requests; the tool does not guess which are the team's.
- **The waits.** `first_code_review` is the wait from the pull request's first opening to its first code review, and `merge` the wait from opening to merge. Each is counted once, at its full length, in the sprint whose window holds the review or the merge, so a pull request can be counted for its review in one sprint and for its merge in a later one. An event that falls between sprint windows, or in more than one, is counted in no sprint. A pull request that has not been reviewed or merged has no wait: it is not counted as zero.
- **What a code review is.** A submitted review by someone other than the author, made before the pull request closed: a comment-only review, a request for changes or an approval, also one dismissed later. Reviews by the author, by an account GitHub marks as a bot and by a member of `opsTeam` are not code reviews, and neither are ordinary comments.
- **The Ops team is read at each sync.** A review is classified once, by the team's members at the sync that first stores it, and keeps that class. The store keeps no reviewer, so a review cannot be classified again later. Reviews collected long after they were made are therefore judged by the team as it is at that first collection.
- **What a code approval is.** `first_code_approval` is the wait from the first opening to the first approving review by someone who is neither the author nor a member of `opsTeam`, made before the pull request closed. It is counted in the sprint of that approval and needs no merge. An approval that is also the first review ends both waits; a comment-only review or a request for changes ends only the wait for review. An approval by the Ops team is stored as its own class and is never a code approval.
- **Dismissed reviews.** GitHub does not say what a dismissed review had been, and a review keeps the outcome it had when it was first collected. An approval collected before it was dismissed stays an approval. A review already dismissed when first collected is a code review and not an approval, and because it may have been one, a pull request with such a review before its first known approval has no first code approval in any figure.
- **When the Ops team cannot be read.** That is when GitHub refuses the team (it answers 403 or 404) or gives an answer the tool cannot read as a list of members. The sync goes on and says so on standard error. It reads the pull requests and no review, since a review cannot be classified without the team. Until a later sync reads the team, the GitHub rows of `first_code_review` and `first_code_approval` in `pr_timing_per_sprint` have `coverage` `reviews not read` and no count, for every sprint, and the report says they are not shown; `merge` is unaffected. The next sync that reads the team asks for the reviews it could not read. A failure to get an answer at all (the connection, the proxy, a rate limit, a token GitHub does not take) says nothing about the team: it ends the sync as a failed read of GitHub, to be run again.
- **Elapsed days.** The figure is the wait divided by 24 hours, weekends and holidays included.
- **Privacy.** The store keeps when a pull request was opened, merged and closed, its author as a salted hash, and for each review its time and its class. It keeps no title, branch name, login or comment text, and nothing of who reviewed. Pull request figures are not subject to the minimum group size: `minimumGroupSize` withholds none of them, and they are never grouped by person.
- **A changed list.** After changing `repositories`, run `sync` to read a newly listed repository and `derive` to recount, or `run`, which does both. A GitHub repository that the last finished read of the host did not go through has its whole list read by that `sync`, not only as far as the first page with nothing new: that covers a repository listed for the first time and one listed again after it was taken out, of which the store may hold some pull requests and not others. Until a sync has read a newly listed repository, a `derive` alone gives the host's rows the `coverage` `repository not read`: what the store lacks of that repository is not counted as zero, and the report says so. Pull requests of a repository taken off the list stay in the store and are in no figure.

#### Azure DevOps

With the `ado` setting the same section and view count the pull requests of the listed Azure DevOps repositories, with that host's own rules. With both settings, each host has its own rows and the two have combined rows: see "More than one host" below.

- **What a code review is.** The first vote other than a reset, or the first text comment on a file, by someone other than the author, made before the pull request closed. A vote against (waiting for the author, rejected) is a review. Comments on the pull request as a whole are general conversation and are not reviews.
- **What a code approval is.** `first_code_approval` is the wait to the first vote in favour, approved or approved with suggestions, by someone other than the author, made before the pull request closed.
- **A vote's time and voter.** Both come from the record Azure DevOps keeps of the vote itself. The voter is the reviewer that record names, not the account the record was written under. A reviewer's current vote has no time and is never used: a vote whose record cannot be read is not dated from it.
- **Merge.** Only a completed pull request has a merge time, its closing time. An abandoned pull request has none, and keeps the reviews it got before it was closed.
- **Automation.** The accounts listed in `automationIdentities`, by identity ID, are automation whatever they write. Azure DevOps does not mark automation itself, so any other account that is not the author is taken for a person. No Ops team is set apart on Azure DevOps. A comment or vote is classified once, by the list at the sync that first stores it, and keeps that class: a change to the list applies to what is collected afterwards.
- **What cannot be told.** A vote whose voter or value cannot be read may have been the first review or approval, so a pull request with such a vote before any other qualifying one has no figure for that wait. A deleted comment is passed over.
- **Privacy.** For a vote or a file comment the store keeps its time, whether it was in favour, against or a comment, and its class. It keeps no comment text, file path, title, branch name, identity ID or name, and nothing of who reviewed. The author is kept as a salted hash.

#### Pull requests without a first code review

Under the pull request table the report has a second table, **Without a first code review**, and the metric view `pr_without_review_per_sprint` has the same numbers: one row per sprint and `host_scope` (`github`, `ado`, and, when both hosts are configured, `combined` for the two together). They say which pull requests are not behind the first code review figure, and why. They are numbers of pull requests and never waits: none is counted as a wait of zero.

- **When.** Each sprint is looked at as of its close, or as of the run for a sprint that is still running. A review made after the close does not change the sprint's numbers: the pull request was waiting when the sprint closed, and its wait is counted in the sprint of the review. A pull request is looked at when it was opened before that moment and was still open then or closed during the sprint.
- **A pull request that was reopened.** The host lists a reopened pull request (on Azure DevOps, a reactivated one) with no closing time, and does not say that it was ever closed. The store keeps the closing it had seen: when a `sync` or `backfill` finds a stored pull request listed as open again, or closed at another time, the earlier closing is kept with the time of that collection. A sprint that closed while the pull request was closed is still told so: it stays in `closed_without_review` in the sprint it was closed in, and it is not added to the `waiting` number of the sprints it was closed through. This has two limits. A closing is kept only when one collection saw the pull request closed and a later one saw it open; one that was closed and reopened between two collections was never seen, and the pull request counts as open all through. And the time of the reopening is known only as "before the collection that saw it": the pull request counts as closed up to that collection, so a sprint that closed after the reopening and before that collection still has it as closed. Collecting more often narrows both. The waits are not affected: a wait runs from the first opening, and a reopening does not restart it.
- **Four groups, each pull request in one.** `waiting`: open, with no first code review yet. `closed_without_review`: closed during the sprint, merged or not, with none before it closed; a merge is still counted as a merge. `unclassifiable`: something that could have been the first code review cannot be told to be one. `history_not_read`: its reviews or threads could not be read at the last collection and the store holds no first code review of it from an earlier one, so whether it was reviewed is not known.
- **What had been submitted.** `no_activity`, `automation_only` and `ops_only` split `waiting` and `closed_without_review` together and add up to them; they are not further pull requests. `ops_only` is something by the Ops team, whatever automation did besides, and is null for `ado`, which has no Ops team. `automation_only` is something by automation and nothing by the Ops team. `no_activity` is nothing, or only the author's own: the history was read and holds nothing else.
- **A history that was not read.** On either host it is a single pull request whose reviews (on Azure DevOps: threads) were refused (with a 403 or 404), did not come to an end or were not understood. Any other failure of such a request is the host or the route to it and stops the read of the host at once, on either host: no answer in time or a 5xx after the retries, a throttle that asks for too long a wait, the 15 minutes a command may spend on failures used up, the proxy, a token the host does not take (401). For the single pull request the sync says so on standard error and goes on, stores the pull request without them, gives no first code review or first code approval wait for that pull request alone unless an earlier collection already stored the review or approval that ends it (what was not read is later, so it cannot come first), and asks for them again at every sync until they are read. Three such pull requests one after another stop the sync as a failed read of the host; one already stored this way is not counted among the three. On GitHub it is also every pull request whose reviews were not read while the Ops team could not be.
- **Coverage.** The view has the `host_scope`, `host_scope_position` and `coverage` of `pr_timing_per_sprint` for `first_code_review` (see "More than one host"). Under any coverage but `read` and `complete` the numbers are of what the store holds, and one that would be 0 is `NULL`, printed as `not known`: not a count of zero. That includes a host's row of a sprint that began before its `collectFrom` date, which is `partial`. A later sprint is `read`, with one gap on GitHub: a pull request left open and not updated since before `github.collectFrom` is not collected, so it is in no sprint's `waiting` number. An Azure DevOps pull request whose own threads were not read does not change the host's coverage; it is counted under `history_not_read`.
- **Not withheld.** Like the pull request figures, these numbers are not subject to the minimum group size, and none is per person.

#### More than one host

Every row of `pr_timing_per_sprint` says whose pull requests it is of, in `host_scope`: `github`, `ado`, or `combined`. Each configured host has its own rows. The `combined` rows exist only when more than one host is configured; with one host its rows are all there is, and a query needs no filter. `host_scope_position` orders the scopes, hosts first and `combined` last. With several hosts the report's table has a **Host** column.

- **Combined figures.** A combined median or P85 is taken of the two hosts' waits pooled, by the same picking as every other figure. It is not the mean of the hosts' medians or P85s, and it can differ from it when the hosts have different numbers of pull requests.
- **One pull request.** A pull request is one by host, repository and number. The same repository name and number on GitHub and on Azure DevOps are two pull requests.
- **Where a host's pull requests were read from.** The store records, with each host's pull requests, the address they were read from: for GitHub the address of the API (`github.apiUrl`, or the public one), for Azure DevOps the address of the organization, which `ado.apiUrl` and `ado.organization` decide together. That address is not part of what makes a pull request one, so the store holds each host's pull requests from one address only, and pointing a host's setting at another server or organization does not continue the same rows. After such a change `sync`, `run`, `derive`, `report` and `query` refuse, with a message that names the setting (`github.apiUrl`, or `ado.organization or ado.apiUrl`) and says to run `backfill`; a refused `sync` or `run` asks nothing of any host and changes nothing. A `backfill` then removes that host's stored pull requests with their reviews, votes, comments and past closings, reads the host whole from the new address, records it, and says how many pull requests it removed. The other host's pull requests and the Jira data are not removed for it. If that read fails outright, the old rows stay and the commands go on refusing until a `backfill` succeeds; if it stops part-way, the old rows are gone and what was read is kept, as for any read that stops part-way. Changing the setting back before a `backfill` ends the refusal, since the store is still of the old address. The comparison is of the address as written: a different spelling of the same server or organization (other capitals in `ado.organization`, say) counts as a change. A store filled before the address was recorded takes the configured one as the one it was read from, and records it at the next `sync` or `backfill`.
- **Coverage.** `coverage` says what a row's count rests on. For a host's row: `read` (the last collection of the host finished, so a count of 0 means the host has no such wait), `failed read` (the last collection of the host failed; the row holds what the store has, from earlier collections and from what the failed one had read whole when it stopped part-way), `not read` (the host is configured and no collection of it has finished or failed yet) `reviews not read` (see "When the Ops team cannot be read") or `repository not read` (the last collection of the host finished, and a repository that is listed now was added to the configuration after it, so nothing was asked of that repository; the row holds the other repositories' pull requests only, the report marks it partial and says to run `sync`, which reads it). A host's row is `partial` in place of `read` for a sprint that began before the host's start date of collection (`collectFrom`): pull requests last updated before that date, on Azure DevOps closed before it, are not in the store, so the row holds only part of what the host has. The date that counts is the one the stored pull requests were collected under: a date moved back, or taken out, counts once a `backfill` has read the whole list (on Azure DevOps, at the next `sync`). The rows with no sprint are `partial` whenever the host has such a date. For a combined row: `complete` when every configured host's row is `read` for the measure, otherwise `partial`. A host that is not configured has no rows at all.
- **No count is not zero.** Under any coverage but `read` and `complete`, `sample_count` is `NULL` where the store holds no wait for the row, and the report prints `not known`. A combined row marked partial holds the waits the store has and leaves out whatever the missing host would add; a host's row marked partial holds what was collected from the start date on. The report marks such rows in the Host column when it has one, and says why under the table.
- **A host that cannot be read.** When one host fails, the sync still reads the others, records the failure and then ends with an error. The hosts that were read have their own figures. A missing token stops the command before it asks anything of any host and changes nothing in the store: a host that was never read stays `not read`.

### Brief visit thresholds

`briefVisitThresholds` lists the statuses to check for brief visits, each with its threshold: the shortest believable stay in that status, in minutes. A brief visit is one unbroken stay in a status that ended and was shorter than the threshold; the full definition is the "Brief visit" entry of [`GLOSSARY.md`](GLOSSARY.md). The setting exists because issues are sometimes moved through several statuses at once, long after the work happened, and such a stay of a few seconds still counts toward the status's time in status.

- A threshold is clock time, not business time: a stay across a weekend is as long as the clock says.
- A status that is not listed is not checked. Without the setting, or with an empty one (`{}`), nothing is checked and reports are what they were before the setting existed.
- A threshold is applied to the nearest ten-millionth of a second, and one that rounds to less than that is refused. The report prints the threshold as applied, in plain digits. `0.5` is thirty seconds exactly.
- A stay exactly as long as the threshold is not brief.

What a report then shows. A status's row, here and in the next list, is its row in the sprint's time in status table before any brief visit was left out. While brief visits are counted, the default, that is the row the table shows. Under `exclude` for time in status the table shows what remains, which can be fewer issues or no row at all; see [Brief visit treatment](#brief-visit-treatment).

- **One line per listed status at the end of the footer**, in board order, for each listed status that has such a row. A shown count reads `Development: 4 issues had a visit shorter than 5 minutes`. A withheld one reads `Development: issues with a visit shorter than 5 minutes: suppressed`.
- **The count is of issues in that row**, each counted once, so it is never larger than that row's issue count. Under `exclude` for time in status it can be larger than the issue count the table shows.
- **One more line when a listed status has the role `work`**, after those: `Delivered issues whose cycle starts with a brief visit, which are in the cycle time figures: 4`. That is its wording while brief visits are counted for cycle time, the default; under `exclude` for cycle time it says the issues have no cycle time. A cycle starts with a brief visit when the stay that begins where the cycle starts is one. The line is there for every sprint, also one with a team smaller than `team`, where it reads `suppressed`.
- **A paragraph at the end of the method note** says what a brief visit is, what the footer's last line counts and that the line was added on 2026-11-16, and that the rule was added on 2026-11-12, after the first run on real data. While brief visits are counted for both metrics, the default, it says that they are counted and no figure was excluded or corrected; when either metric leaves them out it says what was left out and why instead.
- **No other figure changes**, unless `briefVisitTreatment` says so (see [Brief visit treatment](#brief-visit-treatment)). The medians, totals and counts are what they are without the setting, and so is the footer's changelog anomaly count: a brief visit is not a changelog anomaly, and `derive` and `run` print nothing about it.
- **There is no baseline count.** To see a baseline sprint's counts, run `telltale report --sprint <id>` on that sprint.

How the minimum group size applies:

- A count is withheld when that row has, or with nothing left out would have, its figures withheld, or when fewer than `assigneesBehindAFigure` distinct assignees (three by default) are among the issues counted.
- A count of zero is withheld too, so a zero and a withheld count both read `suppressed`.
- A sprint with a team smaller than `team` (five by default) has no brief visit lines.
- With `assigneesBehindAFigure` at zero no count is withheld, and a count of zero reads `0 issues had a visit shorter than 5 minutes`.
- The number in the cycle time line is withheld when the sprint's delivered count is withheld, when fewer than `assigneesBehindAFigure` distinct assignees are among the issues it counts, and when it is zero. With `assigneesBehindAFigure` at zero a number of zero reads `0`.
- A shown count can be subtracted from that row's issue count, which gives the number of issues with no brief visit. That number is not checked against the minimum group size. Under `exclude` for time in status the table shows the issue count of what remains and not of that row, so the subtraction there gives another number; see [Brief visit treatment](#brief-visit-treatment).
- The number in the cycle time line can be combined with shown brief visit counts to say something about a withheld one: the issues it counts are often among those the per-status counts count. The number is not withheld for it.
- Each derive's counts are checked on their own. Deriving under one threshold and then under another gives two counts whose difference is the number of issues with a stay between the two thresholds, and that difference can rest on one or two people. The tool does not prevent this: whoever edits the configuration file is trusted with it, as whoever opens the database file is.

After changing the setting, run `telltale derive`, or `telltale run`, which includes it. Until then `report` and `query` stop with `briefVisitThresholds in the configuration file changed since the derived data was built. Run derive first.` No `backfill` is needed. Writing the same length of time another way (`6`, `6.0`) is not a change.

### Brief visit treatment

`briefVisitTreatment` says what is done with brief visits when a metric is measured. It has two parts, `timeInStatus` and `cycleTime`, and each is `count` or `exclude`. Without the setting, or without a part, the treatment is `count`, and reports are what they are without the setting. The definition is the "Treatment" entry of [`GLOSSARY.md`](GLOSSARY.md).

What each value does to each figure:

| Part | `count` | `exclude` |
| --- | --- | --- |
| `timeInStatus` | Nothing. A brief visit counts toward the status's time in status as any stay does. | The time of every brief visit to a listed status is left out of the issue's time in that status. An issue left with no business time in the status is not in the status's row. The row's issue count, median and total, and the baseline median, are of what remains. The time left out is not added to any other status. |
| `cycleTime` | Nothing. A cycle that starts with a brief visit is in the cycle time figures. | A delivered issue whose cycle starts with a brief visit has no cycle time. It is still delivered: the delivered count does not change, and the issue keeps its point bucket. The bucket's issue count, median and P85, and the baseline's, are of the cycles that remain. A brief visit later in the cycle changes nothing. |

- A treatment applies to every listed status and to every sprint, the baseline sprints included. There is no treatment per status: a status that should not be adjusted is one that is not listed, which also removes its count.
- A status that is not listed under `briefVisitThresholds` is untouched.
- The brief visit counts and the number in the footer's cycle time line are the same under either treatment. Under `exclude` for time in status a count is of the issues that were in the status's row before anything was left out.
- Leaving out is not a correction. No length is estimated or replaced, and the adjusted figures describe the issues that were moved on time, which may not be typical.
- Samples get smaller. More rows and buckets read `suppressed` than under `count`, and a bucket's P85 can be its largest value.
- The raw history is not changed by a treatment. Change the setting and derive again to measure the other way.

What a report then shows, with either part as `exclude`:

- **A line under the heading** that names the metrics adjusted. With `exclude` for time in status alone the whole line is `Brief visits are left out of time in status in this report. A row of that table marked "adjusted" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit and the number left out of the row.`
- **A mark on the row of every listed status**, with `exclude` for time in status: `Development (adjusted: see the footer)`. The mark is there whether or not anything was left out of the row in this sprint.
- **A second number on each brief visit line**, with `exclude` for time in status: `Development: 4 issues had a visit shorter than 5 minutes; those visits are left out of the status's time in status row, and issues that had no other business time in the status and so are not in the row's count: 3`. A listed status whose every issue was left out has its line and no row in the table. A time in status table with no row at all then reads `No issue of this sprint has time in a status that is not done once brief visits are left out (adjusted: see the footer).`, whether or not anything was left out.
- **One note under the cycle time table**, with `exclude` for cycle time, whatever the table holds: `A delivered issue whose cycle starts with a brief visit has no cycle time and is not in this table (adjusted: see the footer).` The footer's cycle time line then reads `Delivered issues whose cycle starts with a brief visit, which have no cycle time: 4`.
- **The method note's paragraph on brief visits** states the treatment of each metric, what was left out, why, what leaving out does not do, and that leaving out was added on 2026-11-16, after results had been seen.

How the minimum group size applies:

- Every figure is checked on what remains. A row or a bucket left with fewer than `assigneesBehindAFigure` distinct assignees behind it reads `suppressed` for its figures, and a baseline figure reads `suppressed` when any baseline sprint's adjusted row does.
- A brief visit count is checked against the row before anything was left out, so it can be shown beside a row that reads `suppressed`.
- The number of issues left out of a row is withheld when fewer than `assigneesBehindAFigure` distinct assignees are among the issues left out, and when it is zero, so a zero and a withheld number both read `suppressed`. With `assigneesBehindAFigure` at zero it is not withheld, and a number of zero reads `0`.
- Shown figures can give a withheld number of a listed status, or a bound on it. A footer line with no row in the table shows that the status's left-out number and its brief visit count are each at least one. A shown brief visit count larger than the row's issue count shows that the left-out number is at least their difference. A brief visit count shown beside a row that reads `suppressed` shows that the left-out number is at least one. A withheld left-out number is less than a shown brief visit count, so with one issue in the row it is the number itself. No count is withheld for any of these.
- A shown brief visit count less a shown left-out number is the number of issues that had a brief visit and are still in the row. That number is not checked against the minimum group size.
- Under `exclude` for cycle time, the delivered count less the sum of the bucket counts is the number of delivered issues with no cycle time: those that never entered a work status plus those left out. When no issue of the first kind exists, that gives the second number also when the footer withholds it.
- Each derive's figures are checked on their own. Deriving under `count` and then under `exclude` gives two figures that can be set side by side, which says something about the issues left out even when their count is withheld. The tool does not prevent this, as it does not prevent subtracting the counts of two derives under different thresholds: whoever edits the configuration file is trusted with it.

After changing the setting, run `telltale derive`, or `telltale run`, which includes it. Until then `report` and `query` stop with `briefVisitTreatment in the configuration file changed since the derived data was built. Run derive first.` No `backfill` is needed.

## Development

```
dotnet build
dotnet test
```

Tests use TUnit and run against a fake Jira, so they need no network and no secrets. The build treats warnings as errors.

Report and query assertions normalize both actual output and multiline expected strings to LF. C# raw strings otherwise inherit the checkout's line endings, so normalizing only the actual output makes a CRLF checkout fail despite matching content.

| Project | Role |
| --- | --- |
| `src/Telltale.Core` | Domain rules: business time, status roles, cycle time, settings. |
| `src/Telltale.Collectors` | The Jira client and collector. |
| `src/Telltale.Storage.Sqlite` | The store, migrations, derived tables and metric views. |
| `src/Telltale.Reporting` | The markdown sprint report. |
| `src/Telltale.Pipeline` | The steps the commands run, and their rules. |
| `src/Telltale.Cli` | The `telltale` executable. |
| `eng/SeedStore` | A developer tool that writes a synthetic store; see below. |

### Trying the executable without Jira

`eng/SeedStore` writes a working folder that a published `telltale` can `derive`, `report` and `query`, with no Jira site and no secrets (and only with no hashing salt stored: see the last point below). Use it to try a build by hand or to check what this README says against the executable.

```
dotnet run --project eng/SeedStore -- <working folder>
cd <working folder>
telltale derive
telltale report --sprint 42
telltale query "SELECT * FROM delivered_per_sprint"
```

- **Everything it writes is synthetic.** `config/telltale.json` and `data/telltale.db` are filled by the real `backfill` from the tests' fake Jira: `FAKE-` issues, fake people and fake statuses. No real Jira data is involved.
- **The history** is two completed sprints, 41 (listed as the baseline) and 42, with a team of six. Each has delivered and unfinished issues and four issues that passed through Development in twenty seconds; sprint 42 has a reopened issue and a row that reads `suppressed`.
- **`--minimum-group-size <team>,<assignees behind a figure>`** writes `minimumGroupSize`, for example `0,0`.
- **`--brief-visit-threshold <status>=<minutes>`** writes an entry of `briefVisitThresholds` and may be repeated, for example `development=5`. A status is its ID or its name without "Fake"; `--help` lists them.
- **The working folder must be empty or new**, unless `--force` is given, which replaces the configuration file and the store and leaves other files alone. A folder inside a checkout of this repository is always refused.
- `backfill`, `sync`, `run` and `discover` call Jira, so they do not work on a seeded store. To change a setting afterwards, edit the configuration file and run `telltale derive`.
- **A laptop that has a hashing salt stored refuses a seeded store.** On Windows the published executable reads the hashing salt from Windows Credential Manager and from nowhere else. The seeded store's fake people are hashed with the tests' salt, so where a salt has been stored, as on the laptop that runs against real data, `derive`, `report` and `query` stop with the message that the salt in the secret store is not the one the people in the database were hashed with. That message's advice does not fit a seeded store: `backfill` calls Jira, and the stored salt must stay as it is, because the real store's people are hashed with it. Today a seeded store can be tried only where no hashing salt is stored: on a machine or account that has none in Credential Manager, or on macOS or Linux with no salt in the environment.

## Further reading

- [`docs/context/handoff.md`](docs/context/handoff.md): where the project came from and what is open.
- [`GLOSSARY.md`](GLOSSARY.md): the project's terms and metric definitions.
- [`docs/adr/`](docs/adr/): the design decisions and their trade-offs.
- [`docs/context/system-design.md`](docs/context/system-design.md): the technical design.
