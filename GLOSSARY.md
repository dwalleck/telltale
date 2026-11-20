# Telltale

A delivery metrics tool: it measures how a software team's work flows from ticket to merge, and how AI use changes that, at the level of the team's work, never individuals.

## Language

### Work and time

**Issue**:
A Jira work item. Identified by its issue key (e.g. PAY-123). On GitHub, team convention requires the key in both the source branch name and the pull request title. On GitHub and Azure DevOps, an unambiguous key in either the source branch or title, or a Jira link to the specific pull request, is enough to attribute it to the issue. If these sources disagree, the pull request is attributed to neither issue. A shared commit SHA alone identifies a candidate, not an attributed pull request. A key mentioned in a description or commit message may refer to other work.
_Avoid_: Ticket (except for agent tickets in `.scratch/`), story, task

**First attributed PR commit**:
For a delivered issue, the earliest commit author date on its earliest-opened attributed pull request that was open by the Jira delivery moment (it may already have closed). A pull request opened after delivery is retained as evidence but cannot supply the comparison timestamp, even if it contains an older commit. An abandoned attempt counts: work had started, even if a different pull request delivered it. If eligible pull requests tie for earliest opening, use their shared earliest author timestamp only when all tied histories are complete and agree on that instant. Otherwise retain the evidence and report “first PR ambiguous,” without choosing by PR number. If the first pull request’s commit history is unreadable or incomplete, its earliest author date is unknown. A later attributed pull request can still show that work had started by a known date, but cannot supply the missing first timestamp. If the selected commit predates Jira issue creation, keep its author timestamp in the comparison and flag it as “commit predates issue creation.” If the selected commit author timestamp is after Jira delivery, retain it as evidence, flag it as “commit after delivery,” and exclude it from the comparison. This is evidence alongside Jira cycle time, not a replacement for its start.

**Status interval**:
A continuous period an issue spent in one status, identified by status ID.
_Avoid_: Stage, phase

**Sprint window**:
The period a sprint counts for: from its start date to its completion date, or to now if not yet completed. A closed sprint with no completion date ends at its planned end. A closed sprint with neither date has no window: no delivery is counted in it, and the run says so and carries on.
_Avoid_: Sprint dates, iteration

**Membership interval**:
A continuous period an issue belonged to a given sprint, rebuilt from the Sprint field's history.
_Avoid_: Sprint assignment

**Time in status**:
How long an issue spent in a status during a sprint: its status intervals clipped to the sprint window and its membership intervals. Reported for statuses before delivery only (not Done or Shipped), and an issue counts toward a status only if it spent more than zero business days there.
The report shows the issue count, median business days and total business days for each status. The total sums each contributing issue's clipped business time, including repeated visits, before rounding. It includes delivered and unfinished issues; it measures accumulated issue time, not person-days of effort. The total is withheld under the same privacy rules as the median. Baseline status comparisons remain pooled medians.
When the treatment for time in status is `exclude`, the time of every brief visit to a status that has a threshold is left out of the issue's time in that status, before that time is clipped to a sprint. An issue left with no business time in the status does not count toward it, so the issue count, the median, the total and the baseline median are of what remains. The time left out is not added to any other status.

**Brief visit**:
One unbroken stay of an issue in a status that ended and lasted less than that status's threshold. The stay is measured in clock time over the issue's whole history, not clipped to a sprint window or a membership interval. A stay the issue is still in is not a visit. A status entered and left at the same instant is a stay of no length, and so a brief visit under any threshold. A stay whose start or end comes from a changelog anomaly is never a brief visit, whatever its length, because its real length is unknown: a stay that begins or ends with a change dated before the issue was created, which is treated as made at its creation, and a stay ended or begun by a change whose from-status is not the status the issue was in. Such a change into the status the issue is already in ends the stay and begins another. The threshold is the shortest believable stay in the status, set per status by the project owner; a status with no threshold is not checked. Brief visits are counted, per sprint and status, as the number of issues that had business time in the status during the sprint, before anything was left out, and that have at least one overlapping the time the sprint counts of the issue (owner decision D93). Whether a duration is left out because of one is the treatment's to say: under `count` no duration is excluded, and under `exclude` the entries of time in status and cycle time say what is left out. No duration is corrected because of one under either. A stay of no length on the instant such a period of counted time starts overlaps it, and one on the instant the period ends does not. A cycle starts with a brief visit when the stay that begins at the cycle's start is one (see cycle time).

**Treatment**:
What is done with brief visits when a metric is measured, chosen by the project owner for each metric a treatment can change (`briefVisitTreatment`). It is `count` or `exclude`. Under `count`, brief visits are counted and no figure is changed; a treatment that is not written is `count`. Under `exclude`, brief visits are left out of the metric: what that means for time in status and for cycle time is in the entry of each. A treatment applies to every status that has a threshold and to every sprint, the baseline sprints included; there is no treatment per status. The count of brief visits is the same under either treatment: it is of the issues that had business time in the status before anything was left out. So is the number of a sprint's delivered issues whose cycle starts with a brief visit, which is shown under either treatment (owner decision D87). Leaving out is not a correction: no length is estimated or replaced. The treatment the derived data was built under can be read in the metric view `brief_visit_treatment`, one value for time in status and one for cycle time, so that a figure read through `query` can be told from the same figure under the other treatment (owner decision D103). The treatment was added on 2026-11-16, after results had been seen, and it changes how a metric is measured.
_Avoid_: Correction, filtering

**Business day**:
A day that is neither a weekend nor a company holiday, counted in full (24 hours) in the team's time zone.
_Avoid_: Working day, workday, working hours

**Cycle time**:
The elapsed business days from an issue's first move into a work status to its last move into a done status, for delivered issues only. Once the cycle has started, all of its time counts: waiting for review or testing, time blocked, time moved back to a status that is waiting to be picked up, and time spent in done before a reopen. Time before the first work status (in the backlog, or waiting in the sprint to be picked up) does not count. It belongs to the sprint in which the issue was delivered. It measures how long delivery took, not the effort that went into it.
When the treatment for cycle time is `exclude`, a delivered issue whose cycle starts with a brief visit has no cycle time: the stay that begins at the cycle's start, in the first work status the issue entered or the one it was created in, is a brief visit under that status's threshold. Such a cycle is left out because it starts when the board was updated and not when the work started. That usually makes it too short; where an issue was moved into a work status and straight back out before the work began, it makes it too long (owner decision D107). The issue is still delivered: the delivered count, seat value and the reopen count do not change, the issue keeps its point bucket, which is its estimate at the start of its cycle as under `count`, and the issue count, the median and the P85 of its point bucket, and the baseline's, are of the cycles that remain. A brief visit later in the cycle changes nothing, and neither does a first stay bounded by a changelog anomaly, which is never a brief visit. An issue moved into a work status and straight back out, with its real start later, is treated in the same way: its start is not moved to the later entry.
_Avoid_: Lead time, turnaround, delivery time

**Reopen**:
A move of an issue out of a done status back to any earlier status.

**Delivered**:
An issue that is in a done status category and was not abandoned (closed with a won't-do resolution). Delivery is reaching Done; shipping to customers is a later step outside the team's control. The delivery moment is the issue's last move into a done status. An issue in a done status whose history shows no such move, or whose history ends in another status, is not delivered; it is counted as a changelog anomaly.
_Avoid_: Completed, closed, resolved

**Point bucket**:
A group of issues with the same story point estimate at the moment their cycle started, used to compare like with like. Later re-estimates are ignored; issues with no estimate at that moment are unestimated. A delivered issue that has no cycle time because its cycle starts with a brief visit keeps the bucket of its cycle's start (owner decision D102; see cycle time).
_Avoid_: Size class, issue type

**Pull request wait**:
The time from a pull request's first opening to one event that ends the wait: its first code review, or its merge. The pull requests are those of the configured repositories, whether or not they are linked to an issue and whoever opened them; they are not assumed to be the team's. The start is the original opening, also for a pull request opened as a draft or reopened later. A wait is counted once, at its full length, in the sprint window that contains the event that ended it, start included and end excluded. An event between sprint windows, or in more than one, is counted in no sprint; this differs from a delivery, which goes to the earliest-started of overlapping sprints. Nothing dated at or after a run's as-of time is counted in that run. A wait is stated in elapsed days (the wait divided by 24 hours) and in business days. A pull request without the event has no wait, not a wait of zero. It measures recorded waiting, not the effort of a review.
_Avoid_: Review time, PR cycle time

**Time to merge**:
The pull request wait from opening to merge. A pull request closed without a merge has none.

**Time to first code review**:
The full wait from a pull request opening to its first submitted human code review. Count it once, in the sprint window containing that review, even if the pull request opened earlier or has not closed. A comment-only review, request for changes or approval qualifies, even if later dismissed. Ordinary conversation comments, the author's own activity, automation and Ops reviews do not qualify. A pull request still waiting at sprint close has no observed duration, not a duration of zero. Only a review made before the pull request closed counts. Whether a reviewer is in the Ops group is decided once, by the group's members at the sync that first collects the review, because nothing is stored of who reviewed. When an earlier review cannot be classified, the pull request has no first code review that can be relied on, and none is counted. See pull request wait for where the wait starts, the sprint it is counted in and its units.

### AI use

**Structured AI use**:
Work on an issue where at least one agent session explicitly invoked a skill from the structured workflow by name. A skill the agent activated on its own does not count.
_Avoid_: AI-assisted (too vague), skills group (that is people, not work)

**Ad hoc AI use**:
Work on an issue where agent sessions occurred but none invoked a structured workflow skill.

**Agent session**:
One run of an AI coding agent, summarized without identity: times, branch, model, skills invoked, and tokens where the plan reports them.

**Ticket-branch session**:
An agent session on a branch whose name carries an issue key.

**Non-ticket session**:
An agent session with no issue key on its branch: research, exploration or tooling. Reported separately, never counted as waste.
_Avoid_: Wasted session, unattributed session

**Plan tier**:
The AI subscription level of a seat, e.g. the $40 unlimited tier or the $100 Pro tier.

**Seat value**:
Delivered issues and merged pull requests per seat per sprint.
_Avoid_: ROI, cost per token

### Review

**Review round**:
One or more reviews with comments, from any reviewers, followed by the author pushing changes. Reviews stacked up before a push are one round; a final review with no push after it is not a round.
_Avoid_: Iteration, pass

**Not-fixed thread**:
A review thread where the reviewer comments again after the author pushed a change meant to address it.

**Reviewer wait**:
Time a pull request spends waiting on a reviewer, as opposed to waiting on its author.

**Rework**:
Lines changed again, in a different pull request and by anyone including the original author, within three weeks of being merged. Reverts are counted separately.
_Avoid_: Churn (ambiguous)

### Measurement

**Headline metric**:
One of the three metrics that carry the story to leadership: review rounds per pull request, P85 cycle time by point bucket, and seat value.

**Guardrail metric**:
A metric that shows whether gains came at a cost: reopen rate, revert rate, rework, linked bugs, pull request size, reviews per active reviewer.

**Baseline**:
A person's or group's own measurements from before the evaluation, used as their comparison point. It is drawn from a list of sprints fixed before the evaluation starts.

**Minimum group size**:
The smallest number of people (five) a result grouped by people may cover. A result grouped by issue attributes (sprint, point bucket) instead needs a team of at least five and at least three distinct assignees behind it. The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in its window. A result that fails either check is suppressed: its figures are withheld. When the team check fails, its issue counts are withheld too, because they add up to the withheld delivered count, and the sprint's tables show no per-bucket or per-status rows at all; when only the three-assignee check fails, the issue count is still shown. An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. That holds for past sprints too: assignee history is not collected, so an issue counts toward whoever it is assigned to now, and reassigning an old issue can change whether a past sprint, or the baseline it belongs to, is suppressed (owner decision D82). The delivered count of a sprint also needs at least three distinct assignees among the delivered issues; when it is withheld for that reason, the sprint's point buckets are not shown at all, because their issue counts and even the number of buckets bound it from below (they count the delivered issues that have a cycle time). The sprint's per-status issue counts stay: they count every issue that spent time in a status, delivered or not (under `exclude` for time in status, apart from an issue whose only time in a listed status was brief visits), so they can approximate the withheld count but do not give it. A baseline sprint must itself pass the team check and have its delivered count shown; if one does not, every baseline figure is withheld and the report names the sprint. A baseline figure pools every issue of the baseline sprints; it is shown only when the pooled group passes both checks and no baseline sprint has that row suppressed on its own, so that a pooled figure cannot give back a withheld one. The count of issues with a brief visit to a status is withheld when the median of that status's time in status row would be withheld with nothing left out (while brief visits are counted, that is the row's own median; when they are left out, the count can be shown beside a row that is suppressed for what remains, owner decision D99), and also when fewer than three distinct assignees are behind the counted issues; a count of nothing has nobody behind it, so it is withheld too. When brief visits are left out of time in status, the number of issues left out of a status's row (those with no other business time in the status) is withheld when fewer than three distinct assignees are behind the issues left out, and when it is zero. It is not withheld because figures of two derives under different treatments can be set side by side (owner decision D95), or because shown figures can give it or bound it: a listed status whose every issue was left out has a footer line and no row, which shows that its number and its brief visit count are each at least one; a shown brief visit count larger than the row's issue count shows that the number is at least their difference; a brief visit count shown beside a row that is suppressed shows that it is at least one; and a withheld number is less than a shown brief visit count, so with one issue in the row it is the number itself. No count is withheld for any of these (owner decision D98). A shown brief visit count less a shown number left out is the number of issues that had a brief visit and are still in the row, which can rest on fewer assignees than a figure needs; the tool neither defines nor withholds that number (owner decision D106). The number of a sprint's delivered issues whose cycle starts with a brief visit (the stay that begins at the cycle's start is a brief visit) is withheld when the sprint's delivered count is withheld, when fewer than three distinct assignees are behind the issues it counts, and when it is zero; it is not withheld because it can be combined with shown brief visit counts to say something about a withheld one (owner decision D97). When brief visits are left out of cycle time, a sprint's delivered count less the issue counts of its point buckets is the number of its delivered issues with no cycle time: those that never entered a work status and those whose cycle was left out. When no issue of the first kind exists, that is the number of cycles left out, also when the footer withholds it; no figure is withheld for that either (owner decision D94). Pull request waits and the counts that explain them are not subject to the minimum group size: neither check is applied to them, whatever the numbers are. A pull request's author is still stored only as a salted hash, and no pull request result is grouped by person. Five and three are the default numbers: a deployment's configuration may set other numbers (`minimumGroupSize`), and a number of zero turns that check off (ADR-0008).

**Case study**:
One of the two consented, named individual exceptions to the minimum group size: the project owner and one mid-level developer, each on a Pro seat.

**Volunteer cohort**:
The evaluation's volunteers as a group, fewer than five, who consented to results being reported for the group as a whole and never per person.
_Avoid_: Skills group, treatment group

**Replay benchmark**:
Rerunning past issues from the commit before their merge, under controlled conditions (model tier × with/without skills), and scoring the results.

**Success criteria**:
The pass/fail thresholds for the evaluation, written down and sent to leadership before the evaluation starts.
