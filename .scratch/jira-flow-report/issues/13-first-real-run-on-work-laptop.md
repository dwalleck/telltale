# 13: First real run on the work laptop

**What to build:** The owner runs the tool against the real Jira for the first time, on the managed Windows laptop. This is where the assumptions made off the laptop are confirmed. Any difference from the documented Jira shapes is fixed by adding a synthetic fixture that reproduces it; no real Jira data is copied into the repo or off company systems.

**Blocked by:** 10, 11, 12 (10 added 2026-11-12: without paging the run stops on a board with more than about 100 issues)

**Status:** ready-for-human

- [x] The .NET 11 SDK is installed on the work laptop, or the obstacle to installing it is recorded
- [x] All tests pass on the work laptop
- [x] `secrets set` stores the secrets in Credential Manager and the tool reads them back
- [x] `discover` reaches Jira through the corporate proxy and writes the configuration file
- [x] Confirmed or corrected: abandoned work is Done with a "Won't Do" resolution
- [x] Confirmed or corrected: the status roles (New, Approved, Committed not started; Development, Review, QE work; Blocked blocked; Done, Shipped done)
- [x] Confirmed or corrected: one Scrum board whose sprints do not overlap
- [x] Confirmed: which JQL search endpoint the site accepts
- [x] A backfill completes and a report is produced for the most recent completed sprint
- [x] Team time zone and company holiday list are entered in the configuration

## Before you start

This section is for whoever picks the ticket up on the work laptop, including an agent that has only this repository. It was added on 2026-11-12, after the build was merged.

**Read first:** `README.md` (install, first run, commands), `GLOSSARY.md`, `docs/decisions.md` (the owner decisions that comments and tests cite by number), and `docs/research/jira-cloud-rest-api.md` ("research note 05": the Jira shapes the code assumes, every one of them unverified against a real site).

**The repository is public, on a personal GitHub account.** `CLAUDE.md` says company data stays on company systems. On this ticket that means:

- Run the tool from a working folder outside the clone. `config/`, `data/` and `reports/` are ignored by git as a second guard, not as permission to keep them in the clone.
- Never commit, push or paste into a ticket, a commit message, a test name or a code comment: the configuration file, the database, a report, a real Jira response, or a real site URL, project key, board ID, sprint name, status name, issue ID, person or field value. The status names in the checklist below are the owner's own description and are the only exception.
- When the real site differs from what the code assumes, describe the difference in terms of shape ("`created` is a number", "the last page has no `isLast`"), then reproduce it in the fake Jira (`tests/Telltale.Pipeline.Tests/Harness/FakeJira.cs`) with invented values, with a test that fails before the fix. Do not copy a response and edit it.
- Secrets go in with `telltale secrets set`, typed at its prompt. They are never an argument, an environment variable on Windows, or text in a file or a conversation.

**Who does what.** The ticket is `ready-for-human` because the owner has to be there: only the owner enters credentials, judges whether a real status or resolution has the assumed role, and decides anything that changes a metric definition. An agent's part is running the checks, reading error messages, and fixing differences with synthetic fixtures and tests. A change to a metric definition is a commit that gives the reason (`CLAUDE.md`).

**Checks.** `dotnet build` and `dotnet test` from the repository root are the whole check; the build treats warnings as errors. The build run used a toolkit and skills that are not in this repository (`.repo-toolkit.psd1` and `.scratch/jira-flow-report/mutations/` belong to it) and are not needed here.

**Windows acceptance:** the code was built and tested only on macOS before this ticket. The first Windows run found CRLF-sensitive expected strings in report/query tests; these are corrected and the suite passes (see Comments). File locking, path separators, and database-file cleanup remain useful diagnostic leads for future Windows failures.

**Setup choices decided after this ticket was written:**

- [x] The hashing salt is created with `telltale secrets set hashing-salt --generate`, before the first backfill, and is not changed afterwards. Changing it needs `--replace` and a new backfill.
- [x] `backfillStart` is earlier than the start of the earliest baseline sprint. A report refuses a sprint that started before it.
- [x] Each baseline sprint has a team of at least five assignees and at least three distinct assignees among its delivered issues. One sprint that falls short withholds the whole baseline, and the report names it (D50, D56). Check the candidates with `telltale report --sprint <id>` before fixing the list: a sprint whose "Delivered issues" line reads `suppressed` cannot be a baseline sprint.
- [x] The baseline sprint list is fixed once and then left alone.
- [ ] Every command but `backfill` refuses a store filled under other collection settings. After changing the board, the projects, a field ID or `backfillStart`, run `backfill`.

## Handed over by the resolved tickets

Collected in the final fix pass (2026-11-12) from the comments of tickets 04, 09, 10, 11, 12 and 14 and from the final review, so that this ticket holds every "confirm on a real site or on Windows" item in one place. At handover, none of these checks had run against a real site or on Windows. Checked items and Comments below record subsequent evidence; unchecked items remain unverified.

Collection (tickets 09, 10, 14; final review C2, B15):

- [ ] The search's page cap: a search with named fields returns at most about 100 issues a page, and `ORDER BY updated ASC, id ASC` is accepted and stable.
- [ ] The page limit: no list of the board needs more than 2000 pages (the collector stops there with a plain message).
- [ ] The `fieldIds` filter and old items: whether the bulk changelog, filtered by `fieldIds`, returns old items that have no field ID. If it does not, the name-based reading never runs and old status and Sprint history is missing without a sign. Fetch an issue old enough to have such items with and without `fieldIds`.
- [ ] The "Sprint" name: an old Sprint item is recognised only as a custom field named exactly "Sprint". A renamed Sprint field loses those items; a second custom field of that name stops the run, or is stored as sprint changes if its values are digits.
- [ ] Old story point items: an old item of the story point field (no field ID) is not read, so estimate changes from that history are missing without a sign if the site has them.
- [ ] A 429 without `Retry-After`: whether the site sends one, and whether the 5 to 80 second waits fit it. Also whether the site ever sends an empty page with a token, which stops the run.
- [ ] The quoted JQL date: `updated >= "yyyy-MM-dd HH:mm"` and `updated >= "yyyy-MM-dd"` are accepted and read in the account's time zone (research note 05 marks it unverified).
- [ ] The field list (`rest/api/3/field`): its shape, and that it lists the configured Sprint and story point field IDs; the collector stops when it does not.
- [ ] The bulk changelog returns history for every issue fetched: after a backfill compare the number of issues with history against the number fetched. A short answer turns a delivered issue into "not delivered" (counted as a changelog anomaly).

Story point history (ticket 04):

- [ ] How Jira writes a number field's history: the numbers in `fromString`/`toString` with `from`/`to` empty, in `from`/`to`, or in both, and as text or as JSON numbers. All of these are read.
- [ ] The form of the number: a decimal comma or a thousands separator stops the run with "holds a value that is not a story point number this tool reads".
- [ ] A first estimate and a cleared estimate come as an empty or missing value, not as `0` or other text.
- [ ] A re-estimate made in the same edit as a status move shares that move's history ID.

`discover` (ticket 11):

- [ ] The board configuration shape: `columnConfig.columns[].statuses[].id` as strings, in column order, and a status mapped to no column absent. Any other shape stops `discover` with "answered in a shape this tool does not understand".
- [ ] Sprint field detection by the custom type `com.pyxis.greenhopper.jira:gh-sprint`.
- [ ] Story point field detection: the custom type `com.pyxis.greenhopper.jira:jsw-story-points`, or a custom number field named "Story Points". A site with both, or a renamed field, needs `--story-point-field`.
- [ ] The first-call messages: a real 401 or 403 from Jira names the credentials, and the corporate proxy's failures (TLS interception, a 407, a sign-in page in place of JSON) name the proxy and root-certificate trust.
- [ ] Paging of the resolution list and the board's project list (`isLast`, `total`).

Secrets, on Windows only (ticket 12):

- [ ] `WindowsCredentialVault` against the real `advapi32`: read, write, a missing entry as error 1168, zeroing and freeing.
- [ ] Policy error 1312: whether corporate policy answers it, and so whether a DPAPI store is needed.
- [ ] `SystemConsoleInput`, the console paste: keys waiting after Enter show the rest of a multi-line paste, and whether a paste ending in CRLF gives one Enter or two.

The team (tickets 06, 07; owner decision D42):

- [x] Bot assignees: whether issues on the board are assigned to bot or duplicate accounts. Each counts as a person toward the minimum group size, and the method note says so.

## Comments

### 2026-11-12 — First owner-assisted Windows run

**Status remains `ready-for-human`.** The owner chose to commit the verified work and leave the unobserved edge-case checks above open. A successful report is not evidence that every Jira history or pagination assumption is correct.

Agent-observed checks:

- SDK `11.0.100` is installed under `C:\Program Files\dotnet`. The first `dotnet build` resolved a user-level dotnetup host with only an RC of .NET 11 and failed SDK resolution. Explicitly invoking the system host preserved `global.json` and built successfully with zero warnings and errors. No SDK installation or pin change was needed.
- The initial Windows suite passed 765 tests and failed 11. Actual report/query text was already LF-normalized, but multiline expected C# strings inherited CRLF from the checkout. The fix normalizes those expected strings too, without changing expected content, suppression rules, production code, or metric definitions.
- Using the system host, `dotnet test --project tests/Telltale.Pipeline.Tests/Telltale.Pipeline.Tests.csproj --treenode-filter '/*/*/QueryCommandTests/*'` passed all 25 tests after the first fix. The final `dotnet test` passed all 776 tests: 95 core and 681 pipeline, zero failures or skips.
- A throwaway probe exercised the real `WindowsCredentialVault`/`advapi32` adapter with an invented value under an isolated, randomly named credential target. Missing-entry reads returned 1168; write and read-back succeeded. The probe deleted its credential and was removed. It never read or changed production credentials. Native memory zeroing/freeing was not instrumented, so the combined checklist item remains open. Error 1312 was not observed; this run did not establish behavior under a policy that rejects storage.

Owner-confirmed checks, performed in a private PowerShell window with runtime files outside the clone:

- Both Jira credentials and a generated hashing salt were stored through the CLI prompts. Discovery reached Jira and wrote the configuration, exercising credential read-back and the configured HTTPS path.
- The described status roles and abandonment resolution were confirmed and configured, as were one non-overlapping Scrum board, the team calendar, and a backfill start preceding the intended baseline.
- Backfill and the subsequent run produced a report. The collector uses `POST /rest/api/3/search/jql`; the successful collection confirms that endpoint is accepted.
- The run emitted a Sprint-history consistency warning but still wrote the report. The warning means successive Sprint history values disagree, or the final history value disagrees with current membership. Its cause remains unverified; do not suppress it or invent a fixture without a structural reproduction. Keep any investigation and real identifiers on company systems.
- All chosen baseline candidates passed the delivered-count suppression check. The owner fixed the baseline list and confirmed that `derive` and `report` succeeded afterwards.
- The owner checked bot/duplicate-account assignees and acknowledged that each account counts toward group size.

Still open: the unchecked handover probes, particularly filtered versus unfiltered old changelog coverage and the Sprint-history warning's cause. No real Jira identifiers, responses, configuration, database, report, or secret values are included in this record.
