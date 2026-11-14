# Telltale

A delivery metrics tool: it measures how a software team's work flows from ticket to merge, and how AI use changes that, at the level of the team's work, never individuals.

## Language

### Work and time

**Issue**:
A Jira work item. Identified by its issue key (e.g. PAY-123), which every branch name carries.
_Avoid_: Ticket (except for agent tickets in `.scratch/`), story, task

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

**Business day**:
A day that is neither a weekend nor a company holiday, counted in full (24 hours) in the team's time zone.
_Avoid_: Working day, workday, working hours

**Cycle time**:
The elapsed business days from an issue's first move into a work status to its last move into a done status, for delivered issues only. Once the cycle has started, all of its time counts: waiting for review or testing, time blocked, time moved back to a status that is waiting to be picked up, and time spent in done before a reopen. Time before the first work status (in the backlog, or waiting in the sprint to be picked up) does not count. It belongs to the sprint in which the issue was delivered. It measures how long delivery took, not the effort that went into it.
_Avoid_: Lead time, turnaround, delivery time

**Reopen**:
A move of an issue out of a done status back to any earlier status.

**Delivered**:
An issue that is in a done status category and was not abandoned (closed with a won't-do resolution). Delivery is reaching Done; shipping to customers is a later step outside the team's control. The delivery moment is the issue's last move into a done status. An issue in a done status whose history shows no such move, or whose history ends in another status, is not delivered; it is counted as a changelog anomaly.
_Avoid_: Completed, closed, resolved

**Point bucket**:
A group of issues with the same story point estimate at the moment their cycle started, used to compare like with like. Later re-estimates are ignored; issues with no estimate at that moment are unestimated.
_Avoid_: Size class, issue type

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
The smallest number of people (five) a result grouped by people may cover. A result grouped by issue attributes (sprint, point bucket) instead needs a team of at least five and at least three distinct assignees behind it. The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in its window. A result that fails either check is suppressed: its figures are withheld. When the team check fails, its issue counts are withheld too, because they add up to the withheld delivered count, and the sprint's tables show no per-bucket or per-status rows at all; when only the three-assignee check fails, the issue count is still shown. An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. The delivered count of a sprint also needs at least three distinct assignees among the delivered issues; when it is withheld for that reason, the sprint's point buckets are not shown at all, because their issue counts and even the number of buckets bound it from below (they count the delivered issues that have a cycle time). The sprint's per-status issue counts stay: they count every issue that spent time in a status, delivered or not, so they can approximate the withheld count but do not give it. A baseline sprint must itself pass the team check and have its delivered count shown; if one does not, every baseline figure is withheld and the report names the sprint. A baseline figure pools every issue of the baseline sprints; it is shown only when the pooled group passes both checks and no baseline sprint has that row suppressed on its own, so that a pooled figure cannot give back a withheld one. Five and three are the default numbers: a deployment's configuration may set other numbers (`minimumGroupSize`), and a number of zero turns that check off (ADR-0008).

**Case study**:
One of the two consented, named individual exceptions to the minimum group size: the project owner and one mid-level developer, each on a Pro seat.

**Volunteer cohort**:
The evaluation's volunteers as a group, fewer than five, who consented to results being reported for the group as a whole and never per person.
_Avoid_: Skills group, treatment group

**Replay benchmark**:
Rerunning past issues from the commit before their merge, under controlled conditions (model tier × with/without skills), and scoring the results.

**Success criteria**:
The pass/fail thresholds for the evaluation, written down and sent to leadership before the evaluation starts.
