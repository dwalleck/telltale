# 11: `discover` writes the configuration

**What to build:** `discover` reads a Jira site and writes the configuration file so the owner does not look up IDs by hand. It first makes one call to check proxy and certificate trust and, on a TLS or proxy failure, says plainly that this is the likely cause. It then lists the board's statuses with their Jira category and a proposed role (not started, work, blocked, done), lists resolutions so the owner can mark the abandoned ones, finds the Sprint and story point field IDs, finds the keys of the board's projects, and writes these with the site URL and board ID to the configuration file. The collector reads the project keys from the configuration (owner decision, 2026-11-12) and does not ask Jira for them on each sync. Proposed roles and abandoned resolutions are meant to be reviewed and edited.

Owner decisions (2026-11-12): `discover` writes only the board's statuses, read from the board's configuration in column order, and never re-adds a status the owner removed (D29). A status whose name contains "block" is proposed as blocked (D30). The first `discover` needs the time zone and the backfill start; later runs take them from the file (D31). On a board with no status in Jira's done category, `discover` still writes the file and says plainly that no status has the done role yet; `run` refuses the file until the owner sets one (D40, applied in the cross-ticket fix pass).

**Blocked by:** 01

**Status:** resolved

- [x] Against the fake Jira, `discover` writes a configuration file that `run` then accepts unchanged
- [x] Statuses are listed with name, ID, Jira category and proposed role
- [x] Resolutions are listed with name and ID
- [x] The Sprint and story point field IDs are found from the field listing
- [x] A simulated certificate or proxy failure produces a message naming proxy and root-certificate trust as the likely cause
- [x] The board's project keys are written as the `projectKeys` setting
- [x] The written file contains no secrets
- [x] Rerunning `discover` does not silently discard roles or resolutions the owner has edited

## Comments

Resolved 2026-11-12. Merged into `spec/jira-flow-report` as 0c86014 (source `ticket/11-discover` at 76c7729).

- Checks: the merged tree equals the source tree, so the implementer's full check on 76c7729 stands (record `ticket11-merge-record.json`, base 0bd28ed): Core 66 and Pipeline 376 passed, 0 failed, 0 skipped, 0 warnings, format and line endings pass. Line endings were checked again at merge.
- Mutations: 35 records (11-m1 to 11-m35). 34 are active and red at 1e2ea34, the tree that holds integration tip 0bd28ed; the one commit after it changes only records. 11-m21 is stale: the Sprint field and the story point field now go through one check, which 11-m16 mutates. 11-m16 and 11-m17 were re-pointed to that check. None rerun at merge, because the merge changed no file relative to the source. No other ticket's record needed re-pointing. All 155 records validate on the merged tree (152 edits checked, 3 records stale).
- Review: 15 findings. Fixed here: 1, 2, 4 to 14. Settled by an owner decision: 3 (D29). Deferred to the cross-ticket fix pass: 15 (`Read`, `Text`, `Optional`, the status parse and the site URL rule are copies of `JiraCollector` and `ConfigFile` members). Finding 11 was corrected by root after the review: a 401 or 403 from Jira on the first call names the Jira email, the API token and `secrets set`; no connection, a TLS failure, a timeout, a 407 and a non-JSON answer name the system proxy and root-certificate trust; any other error from Jira is reported as it is, with no likely cause.
- D2: `discover` writes `jira.projectKeys` from the board's project list. Only `discover` calls the board's project and configuration endpoints; the collector reads the keys from the configuration (`Run_never_asks_jira_for_the_boards_projects`). On a rerun the keys in the file stay and what Jira lists is printed beside them when it differs.
- D29: only the board's statuses are written, read from the board configuration in column order and joined to the status list for names and categories. Once the file has roles it is the owner's: a role in it stays, and a board status it leaves out is printed and never added.
- D30: a status whose name contains "block", in any case, is proposed as blocked whatever its Jira category.
- D31: the first `discover` needs `--site-url`, `--board`, `--time-zone` and `--backfill-start`; later runs take them from the file, and an option still overrides.
- Command: `discover [--site-url] [--board] [--time-zone] [--backfill-start] [--story-point-field] [--sprint-field]` (`DiscoverCommand`, `JiraDiscovery`). Calls, in order: status list (the connection check), board configuration, resolutions, fields, the board's projects. It writes every setting the configuration type requires; `abandonedResolutionIds` and `holidays` start as empty lists and are never touched again.
- Fields: the option wins, then the file, then the single field Jira lists. None, or several without a choice, stops the command. A named field must be in Jira's field list, and the story point field must hold a number.
- Writing: the text about to be written goes through `ConfigFile.ReadAsync(stream, path)` and `PipelineSteps.ReadStatusRoles`, `ReadAbandonedResolutionIds` and `ReadTimeZone`, so a file `run` would refuse is neither written nor copied. That includes a file with a setting the tool does not know, which is how a pasted secret is kept out of the backup. The file is replaced through `<file>.tmp` and a rename. The previous file is kept as `<file>.<yyyyMMddTHHmmssZ>.bak`, never overwritten, and `/config/*.bak` is gitignored. A file whose parsed content is unchanged is not rewritten. Dropped comments are reported. An old site address is never printed, and a site address with a user name or password is refused by `discover` and by `run`.
- Shared files changed: `CliApp.cs` (one line), `ConfigFile.cs` (`ReadAsync(stream, path)` and the user-info check), `PipelineSteps.cs` (three checks made `internal`), `JiraClient.cs` (`LastRefusal`), `.gitignore`. Harness: `FakeJira` is `partial` with `FakeJira.Discover.cs`; `PipelineHarness.RunWithFilesAsTheyAreAsync` runs a command without writing the configuration first.
- `discover` is not built with `PipelineCommand.Create`, which loads the configuration and opens the store first. It uses `CliApp.GuardAsync`, like `secrets set`.
- Open with the owner: a first `discover` on a board with no status in Jira's done category writes nothing, because `run` refuses a file with no done role.
- Ticket 13 must confirm on a real site, because nothing here ran against one:
  - The board configuration shape: `columnConfig.columns[].statuses[].id` as strings, in column order, and a status mapped to no column absent. Research note 05 marks all of it unverified, from memory of the documentation. Any other shape stops `discover` with "answered in a shape this tool does not understand".
  - Sprint field detection by the custom type `com.pyxis.greenhopper.jira:gh-sprint`.
  - Story point field detection: the custom type `com.pyxis.greenhopper.jira:jsw-story-points`, or a custom number field named "Story Points". A site with both, or a renamed field, needs `--story-point-field`.
  - The first-call messages: that a real 401 or 403 from Jira names the credentials, and that the corporate proxy's failures (TLS interception, a 407, a sign-in page in place of JSON) name the proxy and root-certificate trust.
  - Paging of the resolution list and the board's project list (`isLast`, `total`).
