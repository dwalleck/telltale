# Telltale

Telltale is a .NET command-line tool that collects a team's delivery data into one local SQLite store and reports team-level flow metrics per sprint.

It reports on the team's work, never on individuals. Person fields are hashed before they are stored, and by default a figure that covers too few people is withheld.

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

The first rule is a hard constraint of the design. The second is a default that the configuration file can change (see [ADR-0003](docs/adr/0003-pseudonymized-aggregates-only.md), and [ADR-0008](docs/adr/0008-minimum-group-size-is-a-configurable-default.md), which amends it):

- **No per-person results.** Jira account IDs are hashed with a secret salt before storage. Names, emails and issue keys are not stored. No setting changes this.
- **Small groups are withheld by default.** A sprint needs a team of at least five, and a figure needs at least three distinct assignees behind it. Anything short of that reads `suppressed`.
- **The two numbers are a setting.** `minimumGroupSize` in the configuration file sets other numbers. A number of zero turns its check off, and with both at zero nothing is withheld. See [Minimum group size](#minimum-group-size).
- **A report written below the defaults says so.** When either number is below its default, a line under the report's heading gives the numbers in force, or says that small-group suppression is off. The method note of every report states the rule the report was written under.
- **The rule lives in the database views.** `report` and `query` both read through them, so neither can show a figure that the numbers in force withhold.
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

### Upgrading

A store written by a version from before the `minimumGroupSize` setting needs one `telltale derive` after upgrading, because the derived tables changed. Until then `report` and `query` stop with:

```
The derived data in the store was built by another version of this tool. Run derive first.
```

`derive` rebuilds the derived data from the raw data already in the store. It calls no Jira and needs no secrets, and no `backfill` is needed. `telltale run` includes `derive`, so the next `run` does the same.

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

3. **Review `config/telltale.json`.** Give every status a role (`not started`, `work`, `blocked` or `done`); at least one must be `done`. List the resolutions that mean an issue was abandoned, the company holidays, and the baseline sprint IDs. `discover` wrote `minimumGroupSize` with its default numbers; leave it, or see [Minimum group size](#minimum-group-size).

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

Drops and rebuilds every derived table and view from the raw data: issue cycles, time in each status, sprint windows and the metric views. Run it after changing status roles, holidays, the time zone, the baseline sprint list or the minimum group size. It calls no Jira and needs no secrets.

### `report [--sprint <id>]`

Writes `reports/sprint-<id>.md` from the derived data. Without `--sprint` it reports on the most recently completed sprint. It refuses to run when the derived data is older than the raw data, was built under different settings or was built by another version of the tool, and says to run `derive`.

### `run [--sprint <id>]`

Runs `sync`, `derive` and `report` in one step.

### `query "<sql>"`

Runs one `SELECT` over the metric views and prints the rows as a markdown table. Use `--file <path>` to read the SQL from a file. It reads only the metric views, so withheld figures stay withheld.

```
telltale query "SELECT * FROM delivered_per_sprint"
```

The main views are `delivered_per_sprint`, `cycle_time_per_sprint_and_bucket`, `time_in_status_per_sprint`, `baseline_cycle_time_per_bucket`, `baseline_time_in_status` and `data_quality`. `minimum_group_size` holds the two numbers the derived data was built under.

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
  "minimumGroupSize": { "team": 5, "assigneesBehindAFigure": 3 }
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
| `minimumGroupSize` | An object with the two numbers `team` and `assigneesBehindAFigure`, each a whole number of zero or more, not quoted. Optional; when it is present both numbers are required. | `{ "team": 5, "assigneesBehindAFigure": 3 }` |

Things that are refused when the file is loaded, with a message that names the file:

- **An ID written as a number** in `baselineSprintIds` or `abandonedResolutionIds` (`[41, 42]`), or `boardId` written as a string (`"42"`).
- **A sprint's name** in place of its ID. The ID is the number in the sprint's address in Jira (`sprint=41`).
- **A time zone abbreviation or offset**, such as `CST` or `UTC-6`. Neither carries the daylight-saving rules that business days depend on. US zones are `America/New_York`, `America/Chicago`, `America/Denver`, `America/Phoenix` and `America/Los_Angeles`.
- **Any other date form**, such as `"11/26/2026"` or a date with a time.
- **A `minimumGroupSize` number that is not a whole number of zero or more**: a negative number, a fraction (`2.5`, and `5.0` as well), a quoted number (`"5"`), `null`, `true` or `false`. The message names the number, for example `minimumGroupSize.team must be written as a whole number of zero or more.`
- **`minimumGroupSize` with one of its numbers missing, or written as anything but an object**, such as `null`, `false`, `0` or `"off"`. To turn the rule off, write both numbers as zero. For five and three, leave the setting out.

About `holidays`:

- List one entry per day. There is no range form and no "every year" form, so list each year you need.
- Cover the period of the baseline sprints as well as the current one; their cycle times are counted in business days too.
- Weekends are already left out. When a holiday falls on a weekend and the company observes it on a weekday, list the weekday.

### Minimum group size

`minimumGroupSize` holds the two numbers of the rule that withholds figures resting on few people:

- `team`: the fewest distinct assignees a sprint needs. A sprint with fewer reads `suppressed` for its delivered count and both tables.
- `assigneesBehindAFigure`: the fewest distinct assignees among the issues one figure is computed from. A row with fewer keeps its issue count and reads `suppressed` for its figures.

Without the setting the numbers are five and three, and writing five and three gives the same reports as leaving it out. The numbers apply to the baseline sprints and to the footer's two counts as well.

To change the rule, write other numbers. Any two whole numbers of zero or more are accepted, above the defaults as well as below, and the number behind a figure may be greater than the team number.

To turn one check off, write zero for it. With both at zero the rule is off and no report reads `suppressed` anywhere:

```jsonc
"minimumGroupSize": { "team": 0, "assigneesBehindAFigure": 0 }
```

One is not zero. With `assigneesBehindAFigure` at 1, a row made only of unassigned issues is still withheld, and so is a footer count with nobody behind it.

After changing the setting, run `telltale derive`, or `telltale run`, which includes it. Until then `report` and `query` stop with `minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.` No `backfill` is needed.

What a report then shows:

- **Either number below its default:** a line under the heading gives both numbers and says what that allows, for example `Minimum group size in this report: a minimum team of four (below the default of five) and a minimum of two assignees behind a figure (below the default of three).`
- **Both numbers zero:** the line reads `Small-group suppression is off: a figure in this report may rest on the work of one or two people.`
- **Both numbers at or above their defaults:** no line.
- **In every case:** the method note describes the rule with the numbers in force, or says that small-group suppression is off.

The setting changes only this rule. Person fields are still hashed before they are stored, and no report or view is grouped by person.

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
