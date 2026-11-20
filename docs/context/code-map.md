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
steps     src/Telltale.Pipeline/PipelineSteps.cs   what runs when, and every refusal
```

## Tests

Most tests run the real command tree in process: `tests/Telltale.Pipeline.Tests/Harness/PipelineHarness.cs`, one per test.

- **Hosts** are fakes on one shared HTTP stub (`StubHttpHandler.cs`): `FakeJira`, `FakeGitHub`, `FakeAdo`. A request with no route fails the test.
- **The clock** is fixed and moved by the test (`SkippingClock.cs`); waits pass at once.
- **`pipeline.QueryAsync`** reads any table directly. To test what the owner can see, run the `query` command (`HarnessSteps.QueryAsync`).
- **The default sprints** 41 and 42 have a half-hour gap between them; a pull request event placed there is in no sprint.
- **Privacy:** `PrivacyScan.cs` searches every file and both output streams for each fake's forbidden text. New invented names, titles or logins go into that fake's list.

## Adding things

**A setting.**
`TelltaleConfig.cs` is the file's shape and refuses an unknown key; `TelltaleSettings.From` checks it. Each host has its own record (`GitHubConfig.cs`, `AdoConfig.cs`).
A setting that changes a figure is recorded with the derived data (`TelltaleSettings.Recorded`, `DerivedSource`), so `report` and `query` refuse figures built under the old value.
A setting that changes what is collected needs an answer for a store already filled under the old value (`CODING_STANDARDS.md`).
Document it in the README under "Configuration file".

**A secret.**
`SecretName` in `ISecretStore.cs`, a row in `SecretInfo.cs`, a line in `PipelineSession.StartAsync` that asks for it only when its host is configured, and a default in `PipelineHarness.Secrets`.

**A raw table or column.**
A new file in `Sql/Migrations` with the next number. A merged migration is never edited.
The record goes into `RawData`: the raw digest is taken over it, and a fact outside it does not ask for a derive.
A person is stored only as `IdentityHash.Of(...)`, hashed inside the collector. A new hashed column joins the salt checks in `SqliteStore` and is cleared by a backfill under a new salt.

**A metric view.**
A file in `Sql/Views`. A view made by a `Sql/Derived` script is internal and `query` cannot read it.
Add its name to `tests/Telltale.Pipeline.Tests/Harness/MetricViews.cs`. One test fails on a column whose name holds a word for a person.
With no pull request host configured the report must stay byte for byte what it was: many tests pin whole sections.

**A SQL script of any kind** is LF, without a byte-order mark, with a final newline (`eng/check-sql-line-endings.ps1`). The text of the Derived and Views scripts identifies the derived data, so any edit to one makes existing stores ask for a derive.

**A pull request measure.**
A line in `PullRequestTiming.Measures`, an arm in `PullRequestTiming.EndOf`, a label in `SprintReport.PullRequestMeasureLabels`. The views and the report take whatever measures are listed.

**A pull request host.**
A config record and its entry in `TelltaleSettings.PullRequestRepositories`; a secret; a collector implementing `IPullRequestCollector` (`PullRequestCollection.cs`), yielded from `PipelineSteps.PullRequestCollectors`; its review rules beside `GitHubReviewRules.cs`, entered in the two tables at the top of `PullRequestTiming.cs`; a fake in the harness.
A collector returns a batch and writes nothing. A read that stops part-way hands over what it read whole in a `PullRequestReadStoppedException`.
`HostClient` tells a refusal of the thing asked for from the client giving up (`HostGaveUpException`); only the first is one pull request's failure.

## Rules that live in one place

- **Business days, medians, P85:** `BusinessCalendar.cs` and `Sql/Derived/duration_sample.sql` with `duration_figures.sql`. Every figure joins those two scripts.
- **Sprint windows:** `SprintWindows.cs`. An issue's delivery in overlapping sprints goes to the earliest-started (`DeliverySprints.cs`); a pull request event in overlapping sprints goes to none (`PullRequestTiming.SprintOf`).
- **The as-of time:** `PipelineSteps.AsOf`: the clock, moved just past the newest time any source reported.
- **Minimum group size:** inside the Jira metric views, from the numbers in `minimum_group_size`. The two pull request views apply none (D114).
- **Stored times:** `StoredTime.cs`, UTC text to the millisecond. A time from a host is cut to that before it is compared with a stored one.
