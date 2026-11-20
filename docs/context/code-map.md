# Code map

Where a change goes, and what else it must touch. `system-design.md` says what each project is for; this file is the list of places that depend on each other without saying so. It names files, not lines.

## The flow

```
collect   src/Telltale.Collectors          Jira, then each configured pull request host
            -> raw tables                  src/Telltale.Storage.Sqlite/Sql/Migrations
derive    src/Telltale.Core/Derive         a pure function of the raw data, the settings and the as-of time
            -> derived tables              Sql/Derived   (dropped and rebuilt at every derive)
            -> metric views                Sql/Views     (the only thing `report` and `query` read)
report    src/Telltale.Reporting/SprintReport.cs
steps     src/Telltale.Pipeline/PipelineSteps.cs   what runs when, and the order of its checks
```

Most refusals are worded in `PipelineSteps.cs`, but not all. `SqliteStore` refuses derived data built by another version and a metric view it cannot read; `PipelineSession.StartAsync` refuses a missing store ("Run backfill first"); `ConfigFile` and `TelltaleSettings.From` refuse the configuration. A message changed in one place may have a copy in another.

## Tests

Most tests run the real command tree in process: `tests/Telltale.Pipeline.Tests/Harness/PipelineHarness.cs`, one per test.

- **Hosts** are fakes on one shared HTTP stub (`StubHttpHandler.cs`): `FakeJira`, `FakeGitHub`, `FakeAdo`. A request with no route fails the test.
- **The clock** is fixed and moved by the test (`SkippingClock.cs`). A wait passes at once and moves the clock forward by the time waited, so a timeout built on the injected clock (`CancelAfter`, `new CancellationTokenSource(timeout, clock)`) fires at once and shifts the as-of time. Code under test takes timeouts from real time.
- **`pipeline.QueryAsync`** reads any table directly. To test what the owner can see, run the `query` command (`HarnessSteps.QueryAsync`).
- **The default sprints** 41 and 42 have a half-hour gap between them; a pull request event placed there is in no sprint.
- **Privacy:** each fake lists the invented personal and free text it serves (`ForbiddenText`); new names, titles or logins go into that fake's list. `PrivacyScan.cs` runs a scenario with Jira only and searches every file and both output streams for `FakeJira`'s list and no other. The lists of `FakeGitHub` and `FakeAdo` are searched by each host's own collection test (`PullRequestCollectionTests`, `AdoPullRequestCollectionTests`). A new host fake needs the same scan in its collection test.

## Adding things

**A setting.**
`TelltaleConfig.cs` is the file's shape; each host has its own record (`GitHubConfig.cs`, `AdoConfig.cs`). `src/Telltale.Cli/ConfigFile.cs` reads the file and refuses an unknown key. A setting whose null or malformed value is refused by name has its own converter, registered in `ConfigFile.Options`, and `discover` checks an existing file's copy of it through `ConfigFile` too. `TelltaleSettings.From` checks the values.
A setting that changes a figure is recorded with the derived data, so `report` and `query` refuse figures built under the old value. That takes a field in `DerivedSource`, filled in `TelltaleSettings.Recorded` or `DerivedFrom`; a column in `Sql/Derived/run.sql`; the INSERT in `SqliteStore.ReplaceDerivedAsync` and the SELECT in `ReadDerivedSourceAsync`; and a check in `PipelineSteps.RequireCurrentDerivedDataAsync` whose message names the setting. Without the column, the stored record never equals the current one, and every report refuses with "Run derive first" however often derive runs.
A setting that changes what is collected needs an answer for a store already filled under the old value (`CODING_STANDARDS.md`).
Document it in the README under "Configuration file".

**A secret.**
`SecretName` in `ISecretStore.cs`, a row in `SecretInfo.cs`, a property and its `With...Token` method in `src/Telltale.Core/Secrets/CollectionSecrets.cs`, a line in `PipelineSession.StartAsync` that calls that method only when its host is configured, and a default in `PipelineHarness.Secrets`. The collector takes the token from `CollectionSecrets` in `PipelineSteps.PullRequestCollectors`. The README's `secrets set <...>` heading lists every secret's name.

**A raw table or column.**
A new file in `Sql/Migrations` with the next number. A merged migration is never edited.
The record goes into `RawData`: the raw digest is taken over it, and a fact outside it does not ask for a derive.
A person is stored only as `IdentityHash.Of(...)`, hashed inside the collector. A new hashed column joins the salt checks in `SqliteStore` and is cleared by a backfill under a new salt.

**A metric view.**
A file in `Sql/Views`. A view made by a `Sql/Derived` script is internal and `query` cannot read it.
Add its name to `tests/Telltale.Pipeline.Tests/Harness/MetricViews.cs`. One test fails on a column whose name holds a word for a person.
A column whose name contains `suppressed` gets a row in the flags table of `SuppressionOffTests`, which states what each flag holds.
Describe the view in the README under `query`.
With no pull request host configured the report must stay byte for byte what it was: many tests pin whole sections.

**A SQL script of any kind** is LF, without a byte-order mark, with a final newline (`eng/check-sql-line-endings.ps1`). The derived data is identified by the text of the Derived and Views scripts and by the compiled code of Telltale.Core and Telltale.Storage.Sqlite (`SqliteStore.DerivingCode`). Any edit to one of them, even a comment in that C#, makes existing stores ask for a derive.

**A pull request measure.**
A line in `PullRequestTiming.Measures`, an arm in `PullRequestTiming.EndOf`, a label in `SprintReport.PullRequestMeasureLabels`. The views and the report take whatever measures are listed.
A measure is a metric definition: its definition goes into `GLOSSARY.md` and `docs/context/pilot-plan.md`, in a commit that says why (CLAUDE.md).

**A pull request host.**
- a config record, with its entries in `TelltaleSettings.PullRequestRepositories` and `TelltaleSettings.PullRequestSources`. Without the second, `PipelineSteps.RequireConfiguredPullRequestSourcesAsync` never checks that the host's stored rows were read from the configured server or organization;
- its key in `PullRequestHosts` (`src/Telltale.Core/Storage/RawPullRequests.cs`);
- a secret (above), `CollectionSecrets` included;
- a collector implementing `IPullRequestCollector` (`PullRequestCollection.cs`), yielded from `PipelineSteps.PullRequestCollectors`;
- its review rules beside `GitHubReviewRules.cs`, entered in the two tables at the top of `PullRequestTiming.cs`;
- each `host_scope = '...'` in `Sql/Views` (`pr_without_review_per_sprint.sql` names `'ado'`);
- its name in `SprintReport.PullRequestHostLabels`, and a reading of the report's text that is said of one host only (GitHub's Ops group, for one);
- a fake in the harness, with its own privacy scan (see Tests).

A collector returns a batch and writes nothing. A read that stops part-way hands over what it read whole in a `PullRequestReadStoppedException`.
`HostClient` tells a refusal of the thing asked for from the client giving up (`HostGaveUpException`); only the first is one pull request's failure.

## Rules that live in one place

- **Business days, medians, P85:** `BusinessCalendar.cs` and `Sql/Derived/duration_sample.sql` with `duration_figures.sql`. Every figure joins those two scripts.
- **Sprint windows:** `SprintWindows.cs`. An issue's delivery in overlapping sprints goes to the earliest-started (`DeliverySprints.cs`); a pull request event in overlapping sprints goes to none (`PullRequestTiming.SprintOf`).
- **The as-of time:** `PipelineSteps.AsOf`: the clock, moved just past the newest time any source reported.
- **Minimum group size:** inside the Jira metric views, from the numbers in `minimum_group_size`. The two pull request views apply none (D114).
- **Stored times:** `StoredTime.cs`, UTC text to the millisecond. A time from a host is cut to that before it is compared with a stored one.
