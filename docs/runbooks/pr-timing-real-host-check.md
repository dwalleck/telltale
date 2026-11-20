# Runbook: checking PR timing against the real hosts

PR timing (PR 3, branch `spec/github-pr-timing`) was built and tested against synthetic hosts only. This runbook is the first run against the real GitHub and Azure DevOps, on the work machine. It takes the checks in the order that risks least: nothing is read from a host until step 3, and the first read is deliberately small.

**Rules for this run**

- Run from the work folder, not from the clone, as always.
- The tool only reads from both hosts. Nothing here writes to Jira, GitHub or Azure DevOps.
- Results stay on the work machine. What comes back to the repository is the answer table at the end: yes, no, and field names. No repository names, people, PR numbers or figures.
- The commands are PowerShell. `<...>` marks a value to fill in.

## 1. Before touching anything

1. Copy the store: `Copy-Item data\telltale.db data\telltale.before-pr-timing.db`.
2. With the build you have now, write a reference report for a completed sprint and keep it: `telltale report --sprint <id>`, then copy `reports\sprint-<id>.md` to `reports\sprint-<id>.before.md`.
3. Publish the branch into a **new** install folder, so the old build stays usable:
   ```
   git fetch origin
   git switch spec/github-pr-timing
   dotnet publish src/Telltale.Cli -c Release -o <new install folder>
   ```

Rollback at any point: restore the copied store and go back to the old install folder. The branch adds migrations `0009` to `0011`; whether the old build opens a store they were applied to has not been checked, which is why the copy comes first.

## 2. Jira-only results are unchanged

Do this before adding any host setting. The branch changed two SQL scripts every Jira view joins and renamed the HTTP client Jira uses.

1. `<new>\telltale run --sprint <id>` (the same sprint as the reference).
2. Compare: `Compare-Object (Get-Content reports\sprint-<id>.before.md) (Get-Content reports\sprint-<id>.md)`.

**Pass:** no difference in any figure, and no pull request section. A sync between the two reports can move Jira figures for real reasons (an issue reassigned or edited); if something differs, run `derive` and `report` with the old build on the same store copy before concluding the branch caused it.

## 3. GitHub: check the assumptions before the first read

These three are plain API reads, outside the tool. `gh` must be logged in to the enterprise host (`gh auth status --hostname <host>`).

**3a. Does a review move `updated_at`?** The tool asks for a PR's reviews again only when its list entry changed. Pick a PR whose most recent activity was a review (no push or comment after it):

```
gh api --hostname <host> repos/<owner>/<repo>/pulls/<n> --jq .updated_at
gh api --hostname <host> repos/<owner>/<repo>/pulls/<n>/reviews --jq '.[].submitted_at'
```

**Pass:** `updated_at` is at or after the last `submitted_at`. If it is earlier, stop: late reviews would be missed, and the skip rule needs changing before any figure is used.

**3b. How big is the first read?** It costs one request per PR updated on or after `collectFrom`, plus the list pages.

```
gh api --hostname <host> rate_limit --jq .resources.core
gh api --hostname <host> "search/issues?q=repo:<owner>/<repo>+is:pr+updated:>=<collectFrom>" --jq .total_count
```

**Pass:** the PR count across the listed repositories is comfortably under the remaining limit. If not, move `collectFrom` later for the first run. The tool stops cleanly at the limit and a rerun continues with the reviews it lacks, but a PR *list* too long to read within the limit stores nothing.

**3c. Can the token read the Ops team?**

```
gh api --hostname <host> orgs/<org>/teams/<team-slug>/members --jq length
```

**Pass:** a number. A 404 or 403 means the token lacks the organization read permission; the tool would then store PRs with no reviews.

## 4. GitHub: first read, kept small

1. Add the `github` setting with **one** repository, `opsTeam`, `apiUrl` (the host followed by `/api/v3/`) and a recent `collectFrom`, a sprint or two back.
2. `telltale secrets set github-token`.
3. `Measure-Command { telltale run --sprint <id> 2> github-first.err }`, then read `github-first.err`.

**Pass**

- Exit code 0 and nothing in the error file about the Ops team, a refusal or a rate limit.
- The report has a Pull request timing section. Rows are `read`, or `partial` for sprints that began before `collectFrom`. None says `reviews not read` or `failed read`.
- `telltale query "SELECT * FROM pr_without_review_per_sprint"`: `history_not_read` and `unclassifiable` are 0 or empty.

4. Run the same command again at once. **Pass:** it is much faster than the first, since an unchanged PR costs no request. The tool does not print how many requests it made, so the time is the evidence.

5. Spot check by hand. Pick two or three PRs you know: one reviewed and merged in the same sprint, one reviewed in one sprint and merged in a later one, one approved by Ops only. Work out by hand which sprint each wait belongs to and check the counts move as expected. An Ops-only approval must not appear as a first code approval; it shows under Ops only in the second table while the PR has no code review.

Then widen: add the remaining repositories, move `collectFrom` back to where you want it, and run `telltale backfill` (a `sync` does not go back for older PRs), then `telltale run`.

## 5. Azure DevOps: check the shapes before the first read

The collector's idea of these answers was written from documentation and memory, not from a real answer. This step is the most likely to find something wrong.

Set up one read with a personal access token (Code, Read):

```
$pat = Read-Host -AsSecureString "ADO token" | ConvertFrom-SecureString -AsPlainText
$auth = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat")) }
$base = "https://dev.azure.com/<organization>/<project>/_apis/git/repositories/<repository>"
```

**5a. The list.**

```
$list = Invoke-RestMethod -Headers $auth "$base/pullrequests?searchCriteria.status=all&`$top=5&api-version=7.1"
$list.value | Select-Object pullRequestId, status, creationDate, closedDate, @{n='authorId';e={$_.createdBy.id}}
```

**Pass:** all five fields are there, `status` is one of `active`, `completed`, `abandoned`, and a completed PR has a `closedDate`. On Azure DevOps Server, a refusal naming the API version means the server is older than the version the tool pins (7.1).

**5b. A vote thread.** Pick a PR you know someone voted on:

```
$threads = Invoke-RestMethod -Headers $auth "$base/pullRequests/<n>/threads?api-version=7.1"
$vote = $threads.value | Where-Object { $_.properties.CodeReviewThreadType.'$value' -eq 'VoteUpdate' } | Select-Object -First 1
$vote.properties | ConvertTo-Json -Depth 5
$vote.identities | ConvertTo-Json -Depth 3
$vote.publishedDate
```

**Pass, all four:**

- A thread is found, so `properties.CodeReviewThreadType` exists and its `$value` is `VoteUpdate`.
- `properties.CodeReviewVoteResult.$value` is the vote: 10, 5, 0, -5 or -10.
- `properties.CodeReviewVotedByIdentity.$value` is a key into the thread's `identities`, and that entry has an `id`.
- `publishedDate` is when the vote was cast.

If any differs, write down the real property names (names only) and stop here: votes would be stored as nothing or as unknown.

**5c. A file comment.** On a PR with a comment on a file:

```
$threads.value | Where-Object { $_.threadContext.filePath } | Select-Object -First 1 -ExpandProperty comments | Select-Object commentType, publishedDate, isDeleted, @{n='authorId';e={$_.author.id}}
```

**Pass:** `commentType` is `text` for a person's comment and the other three fields are there.

**5d. Old pull requests.** Repeat 5b on a voted PR from the oldest sprint you intend to report. **Pass:** it has a vote thread too. If old PRs have none, approval figures for those sprints cannot be measured, and the report must not be read as "nobody approved".

**5e. The automation account.** If a scanner or bot comments on PRs, note its identity ID (the `author.id` on one of its comments) for `automationIdentities`. Without it the bot counts as a person and ends the wait for review.

## 6. Azure DevOps: first read

1. Add the `ado` setting: `organization`, one `project/repository`, `automationIdentities`, and a `collectFrom`.
2. `telltale secrets set ado-token`.
3. `Measure-Command { telltale run --sprint <id> 2> ado-first.err }`, then read `ado-first.err`.

**Pass**

- Exit code 0. A line per PR whose threads could not be read is tolerable for a few; many of them, or a failed read of the host, means a shape from step 5 is wrong.
- `telltale query "SELECT * FROM pr_without_review_per_sprint WHERE host_scope = 'ado'"`: `unclassifiable` and `history_not_read` are 0 or small. A large `unclassifiable` means votes are read but not understood.
- `telltale query "SELECT * FROM pr_timing_per_sprint WHERE host_scope = 'ado'"`: `first_code_approval` has counts in sprints where you know PRs were approved.

4. Run it again at once. **Pass:** faster than the first. It will not be as fast as GitHub's second run: Azure DevOps reads the whole list and the threads of every open PR at every sync.

5. Spot check by hand as in step 4: one PR approved with suggestions (counts as approved), one abandoned after a review (review counted, no merge), one where the bot commented first (the bot must not end the wait).

## 7. Both hosts together

With both settings in place, run `telltale run --sprint <id>` for a sprint from the migration period.

**Pass:** the table has a Host column with GitHub, Azure DevOps and Combined rows; the combined count is the sum of the two hosts' counts; Combined is marked partial only when one host is.

## What to bring back

Fill this in and bring back only this. A "no" on 3a, 5b or 5d changes the code or what the report may claim; the rest are tuning.

| # | Question | Answer |
| --- | --- | --- |
| 2 | Jira-only report unchanged by the upgrade? | Yes. Line-for-line match; no PR timing section. |
| 3a | Does a review move the PR's `updated_at` on the enterprise host? | Yes on the sampled PR: the review was its latest activity, and `updated_at` matched `submitted_at`. |
| 3b | Did the first read fit in the rate limit at the `collectFrom` you want? | Yes for the pilot repository and cutoff. Wider backfill not tested. |
| 3c | Can the token read the Ops team? | Yes, with `opsTeam` in `organization/team-slug` form. |
| 4 | Second GitHub sync clearly faster than the first? | Yes. Both runs succeeded. |
| 4 | Hand-checked PRs counted in the sprints you expected? | Yes for same-sprint and cross-sprint waits. An Ops-only approval counted as neither a first code review nor a first code approval. |
| 5a | List fields as expected? API version accepted? | Yes to both, including `closedDate` on completed PRs. |
| 5b | Vote thread: which of the four held? Real property names if not. | The sampled `VoteUpdate` threads had a recognized `CodeReviewVoteResult`, a `CodeReviewVotedByIdentity` key resolving to `identities[key].id`, and a `publishedDate`. The vote time was not independently verified against a separate clock. |
| 5c | File comment fields as expected? | Not always. Some text comments omitted `isDeleted`; the collector already treats an absent flag as not deleted. The sampled comments had `commentType`, `publishedDate`, and `author.id`. |
| 5d | Do old PRs have vote threads? From roughly when? | Yes on a voted PR closed in early January 2026. |
| 6 | Any threads unreadable or votes unclassifiable? Roughly how common: none, a few, many? | None observed. Approval with suggestions counted as approval, an abandoned PR retained its review but had no merge, and an automation-first comment did not end the review wait. |
| 6 | Second ADO sync time acceptable? | The repeat was faster; no absolute acceptance threshold was set. Both runs succeeded. |
| 7 | Combined rows add up? | Yes in the rendered migration-sprint report. Across stored sprints, combined counts and coverage matched the host rows. |
