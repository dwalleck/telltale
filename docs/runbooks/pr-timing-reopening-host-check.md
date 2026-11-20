# Runbook: checking reopened pull requests and throttling against the real hosts

This follows `pr-timing-real-host-check.md`, whose checks all passed. It covers what was added to PR 3 afterwards and has not met a real host: how the tool learns when a pull request was reopened, and how it reads a throttled answer from Azure DevOps.

**Rules for this run** are the same as before: run from the work folder, the tool only reads, and what comes back to the repository is the answer table at the end, with yes, no and field names only. The commands are PowerShell; `<...>` marks a value to fill in.

## 1. Upgrade

1. Copy the store: `Copy-Item data\telltale.db data\telltale.before-reopening.db`.
2. Publish the branch into a new install folder, as in the first runbook.
3. `<new>\telltale run --sprint <id>` for a sprint you already reported.

**Pass:** the command does not refuse. The branch added migrations `0012` to `0014` and a check that a host is still read from the address it was first read from; a store filled before that records no address and must be accepted as it is. The pull request figures of the sprint are what they were, unless a pull request was reopened in between.

## 2. GitHub: the events of a reopened pull request

When a sync sees that a stored pull request was reopened, the tool asks once for that pull request's issue events and takes the time of the reopening from them. The answer's shape is from GitHub's documentation.

Pick a pull request you know was closed and then reopened:

```
gh api --hostname <host> repos/<owner>/<repo>/issues/<n>/events --paginate --jq '.[] | select(.event == "closed" or .event == "reopened") | {event, created_at}'
```

**Pass, all three:**

- The request is answered (not 404 or 403) with the token the tool uses.
- There is a `closed` entry and a later `reopened` entry, each with a `created_at`.
- The times match what the pull request's page shows.

If the event names or the time field differ, write down the real names and stop: the tool would fall back to knowing only that the pull request was reopened before the sync that saw it.

## 3. GitHub: a reopening seen by the tool

This needs a pull request that is closed at one sync and open at the next. If none comes up by itself, skip this step; do not close and reopen someone's pull request for it.

1. Sync while the pull request is closed. Note the sprint's row in `telltale query "SELECT * FROM pr_without_review_per_sprint"`.
2. After it is reopened, `telltale run --sprint <that sprint>`.

**Pass:** the earlier sprint's row is unchanged: a pull request that was "closed without one" there is not moved to "still waiting". A review made while it was closed is not its first code review. A review made after the reopening is, with the wait counted from the first opening.

## 4. Azure DevOps: is a reactivation recorded in the threads?

The tool does not read when an Azure DevOps pull request was reactivated, so on that host a review made while a pull request was abandoned still counts once it is reactivated. This step finds out whether the threads the tool already reads would give the time. It changes nothing; it decides whether the limit can be removed later.

Set up `$auth` and `$base` as in step 5 of the first runbook, then, for a pull request you know was abandoned and reactivated:

```
$threads = Invoke-RestMethod -Headers $auth "$base/pullRequests/<n>/threads?api-version=7.1"
$threads.value | ForEach-Object { $_.properties.CodeReviewThreadType.'$value' } | Group-Object | Select-Object Name, Count
$threads.value | Where-Object { $_.properties.CodeReviewThreadType.'$value' -like '*Status*' } | ForEach-Object { $_.properties | ConvertTo-Json -Depth 4; $_.publishedDate }
```

**To record:** the thread types present (names only), whether one of them marks the abandoning and the reactivation, which property says which of the two it is, and whether `publishedDate` is the time it happened.

## 5. Azure DevOps: what a throttled answer carries

The tool waits what `Retry-After` asks for on Azure DevOps. Microsoft's documentation says a delayed or throttled answer also carries `X-RateLimit-Remaining` and `X-RateLimit-Reset`, with another meaning than GitHub's. No throttled answer was seen in the first run, so this may give nothing; do not provoke one.

After a large read (a backfill of the configured repositories), look at one ordinary answer's headers:

```
$r = Invoke-WebRequest -Headers $auth "$base/pullrequests?searchCriteria.status=all&`$top=1&api-version=7.1"
$r.Headers.Keys | Where-Object { $_ -match 'RateLimit|Retry-After' }
```

**To record:** which of those header names are present (names only), and whether any sync wrote a line about Azure DevOps limiting how often it can be asked.

## What to bring back

| # | Question | Answer |
| --- | --- | --- |
| 1 | Did the upgraded build run without refusing, with the figures unchanged? | |
| 2 | Issue events: answered? `closed` and `reopened` entries with `created_at`? Real names if not. | |
| 3 | A reopening seen by the tool: earlier sprint unchanged? Review while closed not counted? Or skipped. | |
| 4 | Azure DevOps thread types on a reactivated pull request, and which marks the reactivation, if any. | |
| 5 | Rate-limit header names seen on Azure DevOps, if any. Any throttling notice from a sync? | |
