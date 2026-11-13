# Telltale

Telltale is a .NET command-line tool that collects a team's delivery data into one local SQLite store and reports team-level flow metrics per sprint.

It reports on the team's work, never on individuals. Person fields are hashed before they are stored, and a figure that covers too few people is withheld.

## Status

The Jira path works end to end: collect a Scrum board, derive the metrics, write a sprint report. GitHub review metrics, AI agent session logs, quality measures and Confluence publishing are planned and not built.

The first owner-assisted run on the managed Windows laptop produced a real Jira sprint report. The Windows test suite passes; detailed real-site edge-case checks remain open in [ticket 13](.scratch/jira-flow-report/issues/13-first-real-run-on-work-laptop.md).

## What a report shows

One markdown file per sprint, with:

- **Delivered issues:** how many issues the sprint delivered.
- **Cycle time by point bucket:** median and 85th percentile, in business days, for issues grouped by story points.
- **Time in status:** how many issues spent time in each workflow status, their median time and total business days. Totals sum issue time within the sprint and membership windows, not effort.
- **Baseline columns:** cycle-time medians and P85s and time-in-status medians pooled over a fixed list of earlier sprints, for comparison.
- **Method note and data quality footer:** how the figures were computed and what was left out.

Time is counted in business days in the team's time zone, skipping weekends and the configured holidays. The definitions are in [`GLOSSARY.md`](GLOSSARY.md).

## Privacy rules

These are hard constraints of the design (see [ADR-0003](docs/adr/0003-pseudonymized-aggregates-only.md)):

- **No per-person results.** Jira account IDs are hashed with a secret salt before storage. Names, emails and issue keys are not stored.
- **Small groups are withheld.** A sprint needs a team of at least five, and a figure needs at least three distinct assignees behind it. Anything short of that reads `suppressed`.
- **The rule lives in the database views.** `report` and `query` both read through them, so neither can show a withheld figure.
- **Opening the database file in another program bypasses the rule.** That program reads every table.

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

3. **Review `config/telltale.json`.** Give every status a role (`not started`, `work`, `blocked` or `done`); at least one must be `done`. List the resolutions that mean an issue was abandoned, the company holidays, and the baseline sprint IDs.

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

Writes `config/telltale.json` from what the Jira site says about the board. The first run needs `--site-url`, `--board`, `--time-zone` and `--backfill-start`. Pass `--story-point-field` or `--sprint-field` when Jira lists none or several candidates.

A rerun keeps what the file already says, and saves a backup of the file before rewriting it.

### `secrets set <jira-email|jira-api-token|hashing-salt>`

Stores one secret in Windows Credential Manager. The value is asked for at a prompt, or piped in with `--stdin`; it is never taken as an argument.

- `--generate` stores a new random hashing salt.
- `--replace` is required to change a salt once people have been hashed with it. Every stored person hash stops matching, so run `backfill` afterwards.

Off Windows, `secrets set` cannot store anything. Set `TELLTALE_JIRA_EMAIL`, `TELLTALE_JIRA_API_TOKEN` and `TELLTALE_HASHING_SALT` in the environment.

### `backfill`

Fetches every issue in the board's projects updated on or after `backfillStart`, plus the board's sprints, and replaces the stored raw data with the result. Use it for the first fetch, after changing a collection setting, or to drop issues that were deleted or moved in Jira.

It refuses to replace a non-empty store with an empty fetch, because that usually means a wrong setting or lost access. `--allow-empty` overrides this.

### `sync`

Fetches what changed in Jira since the last `sync` or `backfill` and adds it to the store. It never removes anything; `backfill` does that.

### `derive`

Drops and rebuilds every derived table and view from the raw data: issue cycles, time in each status, sprint windows and the metric views. Run it after changing status roles, holidays, the time zone or the baseline sprint list.

### `report [--sprint <id>]`

Writes `reports/sprint-<id>.md` from the derived data. Without `--sprint` it reports on the most recently completed sprint. It refuses to run when the derived data is older than the raw data or was built under different settings, and says to run `derive`.

### `run [--sprint <id>]`

Runs `sync`, `derive` and `report` in one step.

### `query "<sql>"`

Runs one `SELECT` over the metric views and prints the rows as a markdown table. Use `--file <path>` to read the SQL from a file. It reads only the metric views, so withheld figures stay withheld.

```
telltale query "SELECT * FROM delivered_per_sprint"
```

The main views are `delivered_per_sprint`, `cycle_time_per_sprint_and_bucket`, `time_in_status_per_sprint`, `baseline_cycle_time_per_bucket`, `baseline_time_in_status` and `data_quality`.

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
  "baselineSprintIds": ["41", "42", "43"]
}
```

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

## Further reading

- [`docs/context/handoff.md`](docs/context/handoff.md): where the project came from and what is open.
- [`GLOSSARY.md`](GLOSSARY.md): the project's terms and metric definitions.
- [`docs/adr/`](docs/adr/): the design decisions and their trade-offs.
- [`docs/context/system-design.md`](docs/context/system-design.md): the technical design.
