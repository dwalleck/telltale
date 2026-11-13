# Owner decisions: the Jira flow report

Code comments, tests, tickets and the spec cite the owner's decisions by number ("owner decision D49"). This is the list. D1 to D69 were made on 2026-11-12: D1 to D57 while the first spec was built, D64 to D69 afterwards, for the optional suppression spec (`.scratch/optional-suppression/spec.md`). D70 was made on 2026-11-13, while that spec was built. Each line gives the decision and where its full wording is; the tickets are in `.scratch/jira-flow-report/issues/`.

A decision that changes a metric definition is also in `GLOSSARY.md`, in a commit that gives the reason.

Some numbers are missing because the decision was about the build run itself and nothing in the repository cites it (D1, D10, D11), because it was withdrawn (D46), or because it belongs to a spec that is not built yet (D58 to D63, the brief status visits spec, `.scratch/brief-status-visits/`).

## Collection

| # | Decision | Full wording |
| --- | --- | --- |
| D2 | The board's project keys come from the `jira.projectKeys` setting, written by `discover`. | ticket 01 |
| D3 | The sprint name is stored and shown. The sprint goal is free text and is never stored. | ticket 01 |
| D4 | Rows that left Jira are cleared by `backfill`, which rebuilds the raw tables. `sync` only adds and updates. | ticket 14 |
| D12 | `abandonedResolutionIds` is a required setting and may be empty. | ticket 02 |
| D13 | Epics, sub-tasks and types above epic are left out at collection. A stored issue that becomes one is deleted when a sync sees it. | ticket 02 |
| D14 | Hierarchy levels of 2 and higher are left out like epics. | ticket 02 |
| D15 | An issue type with neither a sub-task flag nor a hierarchy level stops the run. | ticket 02 |
| D22 | The issue's current sprint IDs are stored. | ticket 05 |
| D26 | `sync` asks for issues updated since the watermark less 48 hours, as a quoted JQL date in UTC. | ticket 09 |
| D27 | `sync` on an empty store fetches from `backfillStart`. | ticket 09 |
| D38 | A fetch is one batch: a run that fails midway stores nothing and leaves the watermark where it was. | ticket 10 |
| D52 | The issue key is not collected or stored. The numeric Jira issue ID stays. | spec |

## Metric rules

| # | Decision | Full wording |
| --- | --- | --- |
| D7 | A closed sprint with no completion time has a window ending at its planned end. | ticket 01 |
| D8 | An issue in a done status whose history ends in a status that is not done is not delivered. It is a changelog anomaly. | ticket 01 |
| D9 | An issue in a done status with no move into done in its history is not delivered. It is a changelog anomaly. | ticket 01 |
| D16 | A cycle-time row's count is the number of cycle times behind it; the column is headed "Issues with a cycle time". | ticket 03 |
| D17 | Calendar days are elapsed time divided by 24 hours. | ticket 03 |
| D18 | Days print to one decimal. | ticket 03 |
| D19 | Board order is the order `statusRoles` lists the statuses in the configuration file. | ticket 05 |
| D20 | Done-role statuses are left out of the time in status table. | ticket 05 |
| D21 | An issue counts toward a status only if it spent more than zero business days there. | ticket 05 |
| D23 | A status no issue spent business time in has no row. | ticket 05 |
| D33 | Every report prints the as-of time and `derive` stores it. "Identical" reports are identical apart from it. | tickets 07, 09 |
| D35 | A delivered issue that never entered a work status has no cycle start; its point bucket is its estimate at the delivery moment. | ticket 04 |
| D37 | `derive` and `run` log changelog anomalies; `sync` alone logs nothing. | ticket 07 |
| D39 | Abandoned issues still count in time in status. | ticket 06 |
| D51 | A closed sprint with neither a completion time nor a planned end has no window. The run names it and carries on. Such a sprint cannot be reported or used as a baseline sprint. | this list |
| D57 | Add total business days to each time-in-status row, summing the existing clipped issue durations before rounding. This complements the median with accumulated issue time, not effort, and uses the same privacy suppression. Baseline status columns remain medians. | Owner request, 2026-11-12 |

## Suppression (minimum group size)

The current rule, in one place, is the "Minimum group size" entry of `GLOSSARY.md`. The decisions are listed in the order they were made, because later ones narrow or replace earlier ones.

D5 to D56 describe the rule with its default numbers, five and three. They apply with whatever numbers are configured (D64 to D66). D64 to D69 were made on 2026-11-12, after the first run on real data; their reason is in ADR-0008.

| # | Decision | Full wording |
| --- | --- | --- |
| D5 | A suppressed row still shows its issue count; only the figures are withheld. Narrowed by D41 and D54. | ticket 06 |
| D6, D36 | The five-person check counts the distinct assignees of every issue that was a member of the sprint, plus every issue delivered in its window. | ticket 06 |
| D41 | A sprint that fails the five-person check has its issue counts withheld too. | ticket 06 |
| D42 | A bot or a person's second account counts as a person. The method note says so. | tickets 06, 07 |
| D44 | A pooled baseline row is shown only when the pooled group passes the rule and no baseline sprint has that row suppressed on its own. | ticket 08 |
| D45 | A sprint under five shows one "suppressed" line per table and no rows. | ticket 07 |
| D47 | Replaced by D50. (A sprint under five could not be a baseline sprint; the run refused the list.) | ticket 08 |
| D48 | The `query` command runs SQL through the same guard as the report. The help text says that opening the database file elsewhere bypasses the rule. | spec |
| D49 | A sprint's delivered count is also withheld unless at least three distinct assignees are among its delivered issues. | spec, `GLOSSARY.md` |
| D50 | A baseline sprint under five withholds every baseline figure. The console and the report name the sprint; nothing stops. | ticket 08, `GLOSSARY.md` |
| D53 | Replaced by D54. (Bucket counts were withheld but the rows stayed.) | spec |
| D54 | A sprint whose delivered count is withheld under D49 shows its cycle time table as one "suppressed" line with no bucket rows. | spec, `GLOSSARY.md` |
| D55 | That sprint's time in status counts stay. The method note says a late status's count can approximate the withheld count. | spec, `GLOSSARY.md` |
| D56 | A baseline sprint whose delivered count is withheld under D49 withholds the whole baseline, as D50 does. | spec, `GLOSSARY.md` |
| D64 | The minimum group size is one optional configuration setting, `minimumGroupSize`, with two whole numbers: `team` and `assigneesBehindAFigure`. Absent means five and three. | optional suppression spec, ADR-0008, `GLOSSARY.md` |
| D65 | A number of zero turns that check off. With both zero, no figure or count is withheld for the size of the group behind it. This covers every rule in this section and the data quality footer's counts. | optional suppression spec, ADR-0008 |
| D66 | Every existing rule applies with the configured numbers in place of five and three. The rules themselves (what is withheld when a check fails) do not change. Any two whole numbers of zero or more are accepted. (Changed on 2026-11-13: a first form refused a number behind a figure greater than a non-zero team number, for a reason that did not hold.) | optional suppression spec |
| D67 | Every report's method note states the numbers in force. A report written with either number below its default also says so in a line under its heading. | optional suppression spec |
| D68 | Hashing of person fields, the fields that are never stored, and the absence of any per-person view do not depend on the setting. | optional suppression spec, ADR-0008 |
| D69 | `discover` writes the setting with five and three into a new configuration file and never changes it afterwards. | optional suppression spec |

## Commands, configuration and secrets

| # | Decision | Full wording |
| --- | --- | --- |
| D24 | Replacing a stored hashing salt needs `--replace`. | ticket 12 |
| D25 | A secret is typed without echo or piped with `--stdin`. It is never a command-line argument. | ticket 12 |
| D28 | `run` without `--sprint` reports the most recently completed sprint. When none has completed it updates the store, says so and exits 0. | ticket 09 |
| D29 | `discover` writes only the board's statuses, in column order, and never re-adds a status the owner removed. | ticket 11 |
| D30 | A status whose name contains "block" is proposed as blocked. | ticket 11 |
| D31 | The first `discover` needs the site, the board, the time zone and the backfill start; later runs take them from the file. | ticket 11 |
| D32 | A control character in a secret refuses the value. | ticket 12 |
| D40 | On a board with no done status, `discover` still writes the file and says no status has the done role; `run` refuses the file until one is set. | ticket 11 |
| D43 | `run`'s messages never quote a refused status role or project key. They still quote a refused time zone. | this list |
| D70 | `discover` keeps its rule of never writing a file `run` would refuse for `minimumGroupSize` too. A rerun on a file whose numbers the loader refuses stops with the loader's message and leaves the file untouched. (2026-11-13) | optional suppression spec, ticket 05 |

## The repository

| # | Decision |
| --- | --- |
| D34 | The repository is public. Everything committed is published, so the rule against real Jira, GitHub or person data in the repository also protects against publishing it. |
