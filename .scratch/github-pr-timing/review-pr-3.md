# Code review of PR #3 (GitHub and Azure DevOps PR timing)

Review run on 2026-11-19 at the `max` level, with every finding reported, against branch `spec/github-pr-timing` at `b6c0ffe`. The review was read-only: nothing was built or run.

Method: seven finder agents covered the line-by-line scan (split into the collection side and the derive/store/report side), removed behaviour, cross-file tracing, language pitfalls, wrappers and fakes, and the CLAUDE.md conventions. That gave 23 distinct candidates. Each got one verifier vote: 11 confirmed, 9 plausible, 3 refuted. A final gap sweep added 3 more, checked against the code by hand.

Result: 17 important findings and 5 nits.

## Refuted and dropped

- **`collectFrom` is read as UTC midnight.** The settings and the README say so.
- **CLAUDE.md names only "PR timing figures" in the D114 exception.** It breaks no rule; at most the wording is narrower than D114.
- **The without-review table gives no marker to a 'reviews not read' row.** Its zeros print "not known", and the "Not shown" line explains them.

## Summary

| # | Severity | Area | Location | Finding |
|---|---|---|---|---|
| 1 | important | Collection | `GitHubCollector.cs:200` | One unreadable PR stops GitHub collection for good |
| 2 | important | Collection | `HostClient.cs:283` | Azure DevOps throttling reads GitHub's reset rule and ignores `Retry-After` |
| 3 | important | Coverage | `PullRequestTiming.cs:149` | A newly added, never-read repository shows as real zeros under 'read' |
| 4 | important | Coverage | `GitHubCollector.cs:119` | Moving `collectFrom` forward strands stored rows |
| 5 | important | Collection | `AdoCollector.cs:137` | Azure DevOps sync ignores the 15-minute failure budget |
| 6 | important | Pipeline | `PipelineSteps.cs:69` | A PR host failure hides the backfill's issue-count line |
| 7 | important | Derive | `GitHubCollector.cs:161` | After a new salt, the author's own comment can become a first code review |
| 8 | important | Collection | `GitHubCollector.cs:73` | A brief connection fault on the roster read exits 0 with review figures withheld |
| 9 | important | Collection | `HostClient.cs:50` | The failure budget is per client, so one command can spend 45 minutes |
| 10 | important | Collection | `HostClient.cs:328` | A GitHub secondary rate limit stops the read as a 403 refusal |
| 11 | important | Collection | `AdoCollector.cs:44` | A large first Azure DevOps read loses its work on a late stop |
| 12 | important | Derive | `PullRequestsWithoutReview.cs:78` | One failed thread read rewrites closed sprints |
| 13 | important | Pipeline | `PipelineSteps.cs:220` | PR times are not part of the as-of clock-skew rule |
| 14 | important | Collection | `HostClient.cs:290` | A fast laptop clock ends a rate-limit wait early |
| 15 | important | Config | `AdoConfig.cs:145` | Changing `ado.organization` silently merges two organizations' PRs |
| 16 | important | Derive | `PullRequestsWithoutReview.cs:39` | An active sprint with a future start counts open PRs as still waiting |
| 17 | important | Collection | `GitHubCollector.cs:92` | Re-adding a repository after a stopped read can skip PRs for good |
| 18 | nit | Report | `SprintReport.cs:865` | The Method note contradicts D114 |
| 19 | nit | Report | `PullRequestTiming.cs:150` | A failed read with unread reviews is explained wrongly |
| 20 | nit | Report | `SprintReport.cs:535` | "Its figures are of what earlier collections read" is false after a stopped read |
| 21 | nit | Report | `SprintReport.cs:612` | The "not known" note gives one reason for every coverage |
| 22 | nit | Pipeline | `PipelineSteps.cs:83` | `run` writes no report when a PR host fails |
| 23 | nit | Tests | `PullRequestCollectionTests.cs:76` | Nothing tests the guard that keeps the GitHub token on its host |

Items 1, 3, 4 and 5 are the most likely to produce wrong or missing figures in normal use.

---

## Important

### 1. One unreadable pull request stops GitHub collection for good

**Location:** `src/Telltale.Collectors/GitHub/GitHubCollector.cs:200`

**Finding:** One PR whose reviews can never be read stops GitHub collection for good. Every later PR of that repository, and every PR of the repositories configured after it, is never collected, and `run` writes no report meanwhile. A failed review read is kept as a partial batch only when another PR was read first in the same run (`when (unread.Count < toRead)`). Reviews are read in number order, so after one stopped read the bad PR is always first, and each run stores nothing.

**Failure scenario:** PRs 1–500 need reviews, and `GET pulls/37/reviews` always fails (502 after retries, a list that repeats a page, or an answer in an unknown shape). Run 1 stores 1–36 and marks the host failed. Run 2 reads the whole list; 1–36 are unchanged and not in `toRead`, so 37 is read first. It fails with `unread.Count == toRead`, and `PullRequestCollection` writes an empty failed batch. Every later run repeats this. PRs 38–500 and every new PR are never collected, reports always say GitHub's last read failed, and `run` exits 1 without a report.

**Remediation:** Store a PR whose own reviews fail with History 'failed' and go on, as `AdoCollector` does. Stop the host only after several failures in a row, or on a host-level give-up.

### 2. Azure DevOps throttling reads GitHub's reset rule

**Location:** `src/Telltale.Collectors/Http/HostClient.cs:283`

**Finding:** An Azure DevOps throttle that asks for a wait of seconds is waited out for up to five minutes, or ends the read at once with a wrong "takes requests again at" time. `HostClient` checks GitHub's used-up rule (403/429, `x-ratelimit-remaining` 0, epoch `x-ratelimit-reset`) for every host, before the 429 branch that reads `Retry-After`. Microsoft documents headers of those names on Azure DevOps throttling with another meaning (Reset is when tracked usage drains to zero), and says to honour `Retry-After`.

**Failure scenario:** Azure DevOps answers a thread request with 429, `Retry-After: 30`, `X-RateLimit-Remaining: 0` and `X-RateLimit-Reset: now+290`. `HostClient` waits about 290 s instead of 30. Three such throttles spend the 15-minute failure budget, and the next one gives up. In the thread loop that PR is stored as 'failed', and three in a row fail the whole Azure DevOps read. A throttle on the pull request list fails the read at once. A reset past now+300 s (rounding or clock skew) throws immediately.

**Remediation:** Apply `RateLimitReset` only to the GitHub client (a constructor option), and prefer a readable `Retry-After` whenever one is present.

### 3. A newly added, never-read repository shows as real zeros

**Location:** `src/Telltale.Core/Derive/PullRequestTiming.cs:149`

**Finding:** When a repository is added to `github.repositories` or `ado.repositories` and only `derive` is run, as the refusal message says to, the report shows the never-read repository as real zeros ('0 | none') under coverage 'read' (combined 'complete'). Coverage comes only from the host's last collection state. Nothing records which repositories that read covered, and the staleness check asks for a derive rather than a sync.

**Failure scenario:** GitHub is configured with `fake-org/a`, synced and derived. The repositories are changed to `[fake-org/b]`. `report` refuses with "Run derive first." After `derive`, `report --sprint 42` prints `| First code review | 0 | none | none | none | none |` for a repository that was never asked for. The PR's own test `A_changed_repository_list_needs_a_derive_and_then_only_the_listed_repositories_count` asserts that output. Adding a repository beside the old ones gives short counts under 'read' instead.

**Remediation:** Record the repository keys a finished read covered with `pull_request_host`, and give 'not read' or 'partial' for any configured repository outside them. Alternatively, make the staleness check say "Run sync first" when a repository was added. Update the test above either way.

### 4. Moving `collectFrom` forward strands stored rows

**Location:** `src/Telltale.Collectors/GitHub/GitHubCollector.cs:119` (also `src/Telltale.Collectors/Ado/AdoCollector.cs:98`)

**Finding:** Once `collectFrom` is moved forward past a stored PR's last activity, that row is never refreshed. A PR stored as open that has since closed is counted "still waiting" in every later sprint under coverage 'read'. Both collectors filter by the date before looking up the stored row, while derive counts every stored PR. On Azure DevOps, a stored 'failed' row closed before the date is also never asked for again.

**Failure scenario:** `github.collectFrom` is 2026-07-01. The Oct 1 sync stores PR #5 open and unreviewed, and #5 is closed unmerged on Oct 2. The owner sets `collectFrom` to 2026-10-05 and syncs on Oct 6. #5's `updated_at` (Oct 2) is before Oct 5, so the callback returns null and the row keeps `closed` NULL. Every sprint starting on or after Oct 5 is 'read', and `pr_without_review_per_sprint` counts #5 as waiting, so the report's "Still waiting" is one too high. Azure DevOps does the same for a stored-open PR completed before the new date.

**Remediation:** Apply `collectFrom` only to PRs the store does not hold. Compare and write known ones as usual, and don't count them toward GitHub's all-too-old page stop. Alternatively, have derive leave out stored rows whose last known activity is before the recorded `collect_from`.

### 5. Azure DevOps sync ignores the 15-minute failure budget

**Location:** `src/Telltale.Collectors/Ado/AdoCollector.cs:137`

**Finding:** An Azure DevOps sync is not held to the 15-minute failure bound. After the budget is spent it keeps sending thread requests, each waiting out a full timeout, and stores each PR as 'history not read'. The per-PR catch takes every `UserFacingException`, including `HostClient`'s own give-ups (retries used up, budget spent, rate limit used up) and a 407 or non-JSON answer. A PR already stored as 'failed' never counts toward the three-in-a-row stop.

**Failure scenario:** The store holds 40 open Azure DevOps PRs with history 'failed'. At this sync the list answers, but every thread request times out. The first two PRs take about 7 minutes each and exhaust the budget. From then on each request still waits `HttpClient`'s 100 s, gives up without retry, and is swallowed. The sync runs about 78 minutes instead of stopping near 15, and records Azure DevOps as 'read'.

**Remediation:** Let the client's own give-ups and proxy or answer-shape failures escape (a distinct exception type or flag). Treat only a refusal of that PR's threads as 'history not read'.

### 6. A PR host failure hides the backfill's issue-count line

**Location:** `src/Telltale.Pipeline/PipelineSteps.cs:69`

**Finding:** When any pull request host fails during a backfill, the "Issues: N before, M now" line is never printed, although Jira's replace-everything batch is already committed. A backfill that replaced 900 issues with 40 goes unnoticed. `CollectAsync` now runs PR collection after the Jira write, and `BackfillAsync` prints the counts only after `CollectAsync` returns. Its own doc comment calls those counts how the user sees such a backfill.

**Failure scenario:** `github` is configured with an expired token. A backfill with a removed project key returns 40 issues where the store held 900. The 900 are replaced and committed, GitHub answers 401, and the command exits 1 showing only GitHub's message. A later backfill prints "Issues: 40 before, 40 now", so the drop from 900 is never shown. Any host failure triggers it: Azure DevOps, a rate limit, Ctrl+C.

**Remediation:** Print the counts right after the Jira write, either by splitting Jira and PR collection in `BackfillAsync` or by printing in a `finally` once the Jira batch is committed.

### 7. After a new salt, the author's own comment can become a first code review

**Location:** `src/Telltale.Collectors/GitHub/GitHubCollector.cs:161`

**Finding:** After a backfill under a new salt, the author's own comment on a stored GitHub PR whose reviews were never read can be stored as a human code review for good. The Jira write nulls every `pull_request.author_hash`, and only PRs the list pass reaches are hashed again. The second pass's `id => AuthorHash(id) == withoutReviews.AuthorHash` is never true against NULL, and events are never reclassified.

**Failure scenario:** PR #9 is stored 'not read' because the Ops roster could not be read at any GitHub sync since it was stored. Its last update (June 20) is before a `collectFrom` that was later moved to July 1. The owner sets a new salt and runs backfill. The Jira write nulls `author_hash`, the list pass skips #9 as too old, and the second pass reads its reviews with no author match, so the author's COMMENTED reply becomes #9's first code review. Only first code review is affected. The row is also written back with `author_hash` still NULL.

**Remediation:** When the stored `AuthorHash` is null, fetch the PR (`GET pulls/{n}`) for the author's id, or leave the row 'not read' instead of classifying its reviews.

### 8. A brief connection fault on the roster read exits 0 with review figures withheld

**Location:** `src/Telltale.Collectors/GitHub/GitHubCollector.cs:73`

**Finding:** A passing connection fault on GitHub's first request makes a sync exit 0 with every GitHub first-review and first-approval figure withheld, where D111 says such a fault ends the run so that it is run again. With `opsTeam` set, the team-members read is the client's first request, so `HostClient` throws at once without retry. The roster catch takes any `UserFacingException` as an unreadable team and stores every new or changed PR as 'not read'.

**Failure scenario:** The first TLS connection through the corporate proxy is reset. `HostClient` throws "Could not reach GitHub ..." (blaming the proxy or certificate), the catch sets `rosterRead = false`, and the PR list then succeeds on a new connection. `FetchAsync` returns host 'read', roster 'failed' and the changed PRs 'not read'. The command exits 0, and the report shows GitHub's review rows as not shown until a later sync reads the team.

**Remediation:** From the roster read, rethrow failures that came before any answer (connection, TLS, timeout, proxy) and auth or rate-limit failures. Treat only a refusal of the team endpoint (403/404) as an unreadable roster. Alternatively, read the roster after the first list page.

### 9. The failure budget is per client, so one command can spend 45 minutes

**Location:** `src/Telltale.Collectors/Http/HostClient.cs:50`

**Finding:** One sync, backfill or run can now spend up to 45 minutes on failed requests and waits, not the 15 that `README.md:172` promises ("One command spends at most 15 minutes on failures in all"). `_spentOnFailures` belongs to each `HostClient`, and a command now builds three of them (Jira, GitHub, Azure DevOps), each with its own budget. The field comment ("one client, and so one command") was not updated either.

**Failure scenario:** `github` and `ado` are configured and a gateway is flaky. Jira spends about 15 minutes retrying and succeeds, GitHub's new client spends another 15 and fails, and Azure DevOps's spends a further 15. The unattended command runs 45 minutes or more of retries and waits before ending with "Run the command again later."

**Remediation:** Create one failure budget per command in `PipelineSteps.CollectAsync` and pass it to every `HostClient`. Alternatively, restate the bound as per host in the field comment and the README.

### 10. A GitHub secondary rate limit stops the read as a 403 refusal

**Location:** `src/Telltale.Collectors/Http/HostClient.cs:328`

**Finding:** A GitHub secondary rate limit stops the read with "GitHub answered 403 (Forbidden)", which reads like a token problem, when GitHub only asked for a wait. GitHub documents secondary limits as 403 or 429 where `x-ratelimit-remaining` need not be 0, often with `retry-after`. `RateLimitReset` returns null for them, a 403 never reaches the code that reads `Retry-After`, and 403 is not `CanPass`. A 429 without `Retry-After` is also retried after 5, 10 and 20 s, though GitHub asks for at least a minute.

**Failure scenario:** A long backfill trips a secondary limit, and GitHub answers `GET repos/o/r/pulls/812/reviews` with 403, `retry-after: 60` and `x-ratelimit-remaining: 4120`. The command exits 1 with "GitHub answered 403 (Forbidden) to GET ..." and keeps only the PRs read whole. On the list request nothing new is stored, and on the roster request the team is taken as unreadable. Because requests are sequential, the 900-points-a-minute route is unlikely at normal latency, but GitHub does not disclose its other secondary heuristics.

**Remediation:** Send a 403/429 that carries `Retry-After` through `RetryWait`. For one with requests left and neither header, wait at least 60 s with backoff. The existing test's 403 without `retry-after` can stay a refusal.

### 11. A large first Azure DevOps read loses its work on a late stop

**Location:** `src/Telltale.Collectors/Ado/AdoCollector.cs:44`

**Finding:** A large first Azure DevOps read that meets three thread failures in a row late on discards every PR it had already read whole, so the next sync starts from zero and can fail at the same point again. The third failure leaves `FetchAsync` as a plain `UserFacingException` (so does a list failure for a later repository), and `PullRequestCollection` stores only a failed host row. `GitHubCollector` instead hands its work over in a `PullRequestReadStoppedException`, which the collector contract names as the remedy for exactly this.

**Failure scenario:** On the first sync of an Azure DevOps repository with 3,000 PRs, 2,500 thread lists are read, then three consecutive thread requests give up (throttling past the budget). An empty failed batch is written. The next sync re-reads all 2,500 and, under the same throttling, fails at about the same point. The behaviour is documented and tested as intended (`AdoCollector.cs:42-43`, `README.md:166` and `README.md:380`), but that rule predates the stopped-read mechanism and was not revisited.

**Remediation:** When the read stops after some PRs were read whole, throw `PullRequestReadStoppedException` with those rows and events and a Failed host state (`CollectFrom` = the stored date). Update the README and tests that pin the current rule.

### 12. One failed thread read rewrites closed sprints

**Location:** `src/Telltale.Core/Derive/PullRequestsWithoutReview.cs:78` (also `src/Telltale.Core/Derive/PullRequestTiming.cs:96`)

**Finding:** One failed thread read at today's sync rewrites closed sprints under coverage 'read'. The PR's first code review (and approval) sample leaves the sprint where it happened, and that sprint counts the PR under 'history not read'. `StateOf` here, and `PullRequestTiming.Waits`, check the row's current History before its stored events. Events are stored only from complete reads and never deleted, though, so an unread event cannot come before a stored, classified human review.

**Failure scenario:** Azure DevOps PR #12 opens in sprint 10 and gets a human vote in sprint 10 (stored, 'read'). It stays open, so its threads are asked for at every sync. In sprint 20 one thread request fails and the row becomes 'failed'. After derive, sprint 10 loses #12's `first_code_review` sample (and its `first_code_approval` sample for a positive vote), and sprints 10 to 20 each count it as `history_not_read` under coverage 'read', until a later sync reads the threads. This follows the documented rule for 'failed' rows, but the spec withholds a measure only when missing evidence "could change the first qualifying event".

**Remediation:** When the stored events already settle the first code review before the sprint's moment, use them (return null here and keep the wait in `Waits`). Use 'history not read' only when they do not.

### 13. PR times are not part of the as-of clock-skew rule

**Location:** `src/Telltale.Pipeline/PipelineSteps.cs:220`

**Finding:** A merge or review that GitHub or Azure DevOps dated slightly ahead of the laptop's clock is left out of the derive that follows the sync, and report and query accept the result as current. `AsOf` pushes the as-of time past the newest Jira change and issue update because Jira's clock can run ahead, but pull request times are not part of that rule, and PR derive drops everything dated at or after the as-of time.

**Failure scenario:** The laptop runs 2 minutes behind GitHub, and a PR is merged 1 minute before `run`. `merged_at = T` is stored, and derive starts at local T − 1 minute plus the sync time, so the as-of time is before T. `Waits` nulls `Merged`, so there is no merge wait, and if the PR had no first code review it is counted 'waiting' in the running sprint. The raw digest matches, so report and query accept this until the next derive.

**Remediation:** Include pull request opened, merged and closed times and event times in `newest`.

### 14. A fast laptop clock ends a rate-limit wait early

**Location:** `src/Telltale.Collectors/Http/HostClient.cs:290`

**Finding:** If the laptop's clock runs more than about 5 seconds ahead of GitHub's, a used-up rate limit that is about to lift ends the command instead of being waited out. The wait is `reset - _clock.GetUtcNow()` with no margin and without the answer's `Date` header, floored at 1 s, and every retry counts against `MaxRetries` (5). Once the local clock passes the reset, the remaining retries come 1 s apart and run out early.

**Failure scenario:** The local clock is 10 s ahead, and GitHub answers 403 with remaining 0 and reset 12:00:00. The client waits until local 12:00:00 (GitHub 11:59:50), is still limited, and then retries every 1 s. The fifth retry lands near GitHub 11:59:55, and the command stops with "GitHub's rate limit is used up ... again after 5 retries. Run the command again later."

**Remediation:** Compute the wait from the answer's `Date` header when present, plus a margin, or back off further when GitHub repeats a reset that the local clock has passed.

### 15. Changing `ado.organization` silently merges two organizations' PRs

**Location:** `src/Telltale.Core/Configuration/AdoConfig.cs:145`

**Finding:** Changing `ado.organization` silently merges two organizations' pull requests. Rows of the same project/repository name are overwritten or mixed, and the report shows them as one host read in full. The key is project/repository only, `pull_request_host` records no organization or address, the repository-list staleness check compares only keys, and there is no scope check like Jira's `CollectionScope.RequireSameAs`. The README documents that `apiUrl` continues the same rows but says nothing about the organization.

**Failure scenario:** `ado.organization` changes from orgA to orgB, both with project `pay` and repository `api`. orgB's PR 101 overwrites orgA's stored row (`ON CONFLICT DO UPDATE`), and orgA's events for 101 stay beside orgB's. An orgB event whose kind and ID collide with one of them is dropped. orgA's other PRs keep counting, and orgA PRs left open are counted waiting in every later sprint. Nothing refuses or warns.

**Remediation:** Record the host's scope (organization, `apiUrl`) in `pull_request_host` and refuse when it changes, or ask for a backfill that clears the host's rows. If continuing across organizations is intended, document the organization the way `apiUrl` is documented.

### 16. An active sprint with a future start counts open PRs as still waiting

**Location:** `src/Telltale.Core/Derive/PullRequestsWithoutReview.cs:39`

**Finding:** For an active sprint whose Jira start is still in the future, every open PR without a first code review is counted "still waiting" in a sprint that has not begun, while the waits view and the Jira tables give that sprint nothing. `SprintWindows.Of` gives every started (non-future) sprint a window, and this code looks at it at `at = asOf`, which is before the window's start. `Resolve(asOf)` makes the same window empty everywhere else.

**Failure scenario:** On Friday a sprint is started in Jira with a start date of next Monday (state 'active'). The derive's as-of time is Friday, so `at < Start`. An open PR with no review has `Closed` NULL (not `< Start`) and `Opened < at`, so it is 'waiting' in that sprint. `pr_without_review_per_sprint` and the report show N still waiting for a sprint with 0 waits, and the same PRs are also counted at the previous sprint's close.

**Remediation:** Skip the window when `at <= window.Start` (or clamp `at` to the start).

### 17. Re-adding a repository after a stopped read can skip PRs for good

**Location:** `src/Telltale.Collectors/GitHub/GitHubCollector.cs:92`

**Finding:** PRs that a stopped GitHub read left out of a repository can be skipped for good by later syncs while coverage says 'read'. The whole-list decision (`readInFull`) is per host. Once a sync without that repository records the host 'read', re-adding the repository gets an incremental read that stops at the first page of stored, unchanged PRs.

**Failure scenario:** Repositories A and B. Run 1 stops on the rate limit during B's reviews, so some of B's PRs are stored, others are left out, and the host is 'failed'. The owner removes B and syncs (host 'read'), then adds B back and syncs. If a full page of B's stored, unchanged PRs comes before a missing one in updated order (older PRs touched after newer ones, as by a stale-bot sweep), the read stops there and the missing PRs are never read. A backfill would read them, but the README names sync as the step after changing repositories.

**Remediation:** Decide the whole-list read per repository (read a repository's whole list when it was not part of the last read that ended), or document backfill as the step after re-adding a repository. This pairs naturally with the fix for item 3, which records which repositories a read covered.

---

## Nits

### 18. The Method note contradicts D114

**Location:** `src/Telltale.Reporting/SprintReport.cs:865`

**Finding:** With a pull request host configured, the report contradicts itself. Its Method note says "A figure is shown only where it cannot show one person's work" and that under a small team "both tables read suppressed". Meanwhile the Pull request timing section shows waits and counts resting on one or two PRs that are never withheld (D114). `AppendMinimumGroupSize` was not changed, and nothing limits it to the Jira figures.

**Failure scenario:** With the default numbers (5/3), GitHub configured and one reviewed PR in the sprint, the report shows `| First code review | 1 | 2.0 | 2.0 | 2.0 | 2.0 |`, as `PullRequestFiguresNotWithheldTests` pins. The Method section then says "A figure is shown only where it cannot show one person's work."

**Remediation:** When `PullRequestsCounted` is true, limit those sentences to the figures derived from Jira, or add that the pull request section is the exception.

### 19. A failed read with unread reviews is explained wrongly

**Location:** `src/Telltale.Core/Derive/PullRequestTiming.cs:150`

**Finding:** When GitHub's last read failed while some stored PRs still lack their reviews, the report explains the "not known" review rows wrongly. It says the figures are what earlier collections read, and never that the review waits are withheld for the whole host. Coverage gives 'reviews not read' only while the host state is 'read', and 'failed read' otherwise. But `Waits` drops every review wait of a host in `HostsWithoutTheirReviews` whatever its state, so the "Not shown" line (`SprintReport.cs:514`) is never written.

**Failure scenario:** Sync 1 reads PR 1's review (`first_code_review` 1 in sprint 42). Sync 2 cannot read the roster and stores PR 2 as 'not read'. Sync 3 fails outright (state 'failed', roster 'not read'), as in `PullRequestRosterTests`. The sprint 42 report then shows both review rows as "not known", with "GitHub could not be read ... Its figures are of what earlier collections read ... where they read nothing the row says not known", though an earlier collection did read PR 1's review. No figure is false.

**Remediation:** Give `ReviewsNotRead` for the review measures whenever `withoutReviews` contains the host, or make the failed-read line say the review waits are withheld.

### 20. "Its figures are of what earlier collections read" is false after a stopped read

**Location:** `src/Telltale.Reporting/SprintReport.cs:535`

**Finding:** After a GitHub read that stopped part-way, the report says "Its figures are of what earlier collections read". That is false: the failed collection itself stored PRs and reviews that are in the figures, and on a first read there was no earlier collection. This line, the `FailedRead` doc comment and `pr_host_coverage.sql`'s description of 'failed read' all assume that a failed read stores nothing. That stopped being true once `PullRequestReadStoppedException` began handing over a partial batch.

**Failure scenario:** On the first-ever sync, GitHub's rate limit runs out at PR 3 of 5, and PRs 1 and 2 are stored with their reviews, host 'failed'. After derive, the report shows `| First code review | 2 | 5.5 |` and "- GitHub could not be read when pull requests were last collected. Its figures are of what earlier collections read...". `PullRequestRateLimitTests` asserts both lines.

**Remediation:** Say the figures are of what the store holds, which may be short of what the host has now (a read that stopped part-way keeps what it read whole). Correct the `FailedRead` doc comment and `pr_host_coverage.sql` to match.

### 21. The "not known" note gives one reason for every coverage

**Location:** `src/Telltale.Reporting/SprintReport.cs:612`

**Finding:** Under the "Without a first code review" table, every "not known" cell is explained as standing "where a row does not rest on a full read of its reviews". Only 'reviews not read' fits that reason. It is wrong for the other coverages that print it: partial because of `collectFrom` (reviews were read in full, but older PRs were never collected), last read failed, and never read.

**Failure scenario:** `github.collectFrom` is 2026-01-15 and sprint 41 began on 5 January. The view gives the sprint 'partial', and the report prints `| GitHub (partial) | not known | ... |` followed by the note that blames an incomplete read of reviews (`PullRequestCollectFromCoverageTests` asserts this text), when the cause is the start date.

**Remediation:** Give no single reason ("where a row does not rest on a full read; the lines under the table above say which and why"), or word the note per coverage.

### 22. `run` writes no report when a PR host fails

**Location:** `src/Telltale.Pipeline/PipelineSteps.cs:83`

**Finding:** With a pull request host configured, any GitHub or Azure DevOps failure makes `run` write no report, although Jira is synced and the 'failed read' coverage exists for this case. `README.md:172` still says "A fetch is stored whole or not at all, so a run that still fails has changed nothing". `RunAsync` awaits `SyncAsync`, which now rethrows host failures after Jira's batch and any stopped GitHub read have been stored.

**Failure scenario:** The first `run --sprint 42` after adding `github` for a large repository runs out of rate limit part-way. The partial read is stored, and the command exits 1 without writing `reports/sprint-42.md`. Until the read completes over the following hours, every `run` writes no report, while `derive` then `report` would have shown the sprint with GitHub marked "last read failed".

**Remediation:** In `RunAsync`, catch the PR-collection failure, still derive and report, then rethrow so the exit code is 1. Correct `README.md:172` and the `run` section for pull request hosts.

### 23. Nothing tests the guard that keeps the GitHub token on its host

**Location:** `tests/Telltale.Pipeline.Tests/PullRequestCollectionTests.cs:76`

**Finding:** Nothing tests the guard that keeps the GitHub token from going to another host. No test has a `Link` header that names another host, and the "...under the configured address" tests cannot see the address. `StubHttpHandler` records only the request path and matches routes on method and path, so a request to any host with that path is served and counted.

**Failure scenario:** A regression drops `next.AbsoluteUri.StartsWith(baseUrl, ...)` from `HostClient.NextPage`, or builds the base address from the wrong setting. GitHub, or a page served in its place, then names a next page on `https://elsewhere.example/api/v3/...`, and the Bearer token goes there. Every PR test still passes; no test file contains "not under the configured address".

**Remediation:** Record scheme and authority in `StubRequest` and assert them in the GitHub and Azure DevOps "...configured address" tests. Add a GitHub test whose next link names another host and expects the "not under the configured address" stop, with no request reaching it.
