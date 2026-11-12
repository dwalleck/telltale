using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Rebuilds all derived data from the raw rows. It is a pure function of the raw data, the
/// configured board, status roles, abandoned resolutions and business calendar, and the as-of time, so changing a definition means rerunning it (ADR-0002).
/// </summary>
/// <remarks>
/// Throws <see cref="UserFacingException"/> when an issue has been in a status that has no configured role, when
/// a closed sprint of the board has neither a completion time nor a planned end, or when an issue was stored
/// before its Sprint field was fetched.
/// </remarks>
public static class Derivation
{
    public static DerivedData Derive(RawData raw, long boardId, StatusRoles roles, IReadOnlySet<string> abandonedResolutionIds, BusinessCalendar calendar, DateTimeOffset asOf)
    {
        // Unknown is not "in no sprint": deriving anyway would leave these issues out of every sprint's time in status.
        var sprintsUnknown = raw.Issues.Count(issue => issue.SprintIds is null);
        if (sprintsUnknown > 0)
        {
            throw new UserFacingException(sprintsUnknown == 1
                ? "1 issue in the store was fetched before the Sprint field was kept, so the sprints it belonged to are unknown. Run a backfill to fetch it again. No figures were derived."
                : $"{sprintsUnknown} issues in the store were fetched before the Sprint field was kept, so the sprints they belonged to are unknown. Run a backfill to fetch them again. No figures were derived.");
        }

        var sprintChanges = raw.Changes
            .Where(change => change.Field == ChangeFields.Sprint)
            .ToLookup(change => change.IssueId, change => SprintChange.FromRaw(change.HistoryId, change.At, change.From, change.To));

        var statusChanges = raw.Changes
            .Where(change => change.Field == ChangeFields.Status && change.To is not null)
            .ToLookup(change => change.IssueId, change => new StatusChange(change.HistoryId, change.At, change.From, change.To!));

        // Which sprints count is decided here and nowhere else. Only the configured board's: the store can
        // still hold sprints of a board ID that was since corrected. And only sprints that have started:
        // a future sprint has no window yet, even when it carries planned dates.
        var windows = raw.Sprints
            .Where(sprint => sprint.BoardId == boardId && sprint.State != SprintStates.Future && sprint.Start is not null)
            .Select(sprint => new SprintWindow(sprint.SprintId, sprint.Start!.Value, sprint.PlannedEnd, WindowEnd(sprint)))
            .ToList();

        var deliverySprints = new DeliverySprints(windows, asOf);
        var intervals = new List<IssueStatusInterval>();
        var cycles = new List<IssueCycle>();
        var memberships = new List<IssueMembershipInterval>();
        var sprintStatusTimes = new List<SprintStatusTime>();

        foreach (var issue in raw.Issues)
        {
            var timeline = StatusTimeline.Build(issue.Created, issue.StatusId, statusChanges[issue.IssueId]);
            intervals.AddRange(timeline.Intervals.Select(interval => new IssueStatusInterval(issue.IssueId, interval.StatusId, interval.Start, interval.End)));

            var reopens = Cycles.ReopenCount(timeline.Entries, roles);
            cycles.Add(Delivery.Of(issue.StatusId, issue.ResolutionId, timeline.Entries, roles, abandonedResolutionIds) switch
            {
                Delivered delivered => new IssueCycle(issue.IssueId, delivered.At, Delivered: true, deliverySprints.For(delivered.At), reopens)
                {
                    Cycle = Cycles.Measure(timeline.Entries, roles, delivered.At, calendar),

                    // The point bucket is the issue's current estimate.
                    PointBucket = issue.StoryPoints,
                },

                // An abandoned issue reached done, so it has a cycle end, but it is not a delivery: it belongs to
                // no delivery sprint and has no cycle time.
                Abandoned abandoned => new IssueCycle(issue.IssueId, abandoned.At, Delivered: false, DeliverySprintId: null, reopens),
                NotDelivered => new IssueCycle(issue.IssueId, CycleEnd: null, Delivered: false, DeliverySprintId: null, reopens),
            });

            var membership = SprintMembership.Build(issue.Created, SprintMembership.ParseSprintIds(issue.SprintIds), sprintChanges[issue.IssueId]);
            memberships.AddRange(membership.Intervals.Select(interval => new IssueMembershipInterval(issue.IssueId, interval.SprintId, interval.Start, interval.End)));

            // Only sprints that count (the windows above) have time in status. A sprint the issue was never in gives nothing.
            foreach (var window in windows)
            {
                sprintStatusTimes.AddRange(TimeInStatus.ForSprint(window, timeline.Intervals, membership.Intervals, calendar, asOf)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new SprintStatusTime(issue.IssueId, window.SprintId, pair.Key, pair.Value.CalendarTime, pair.Value.BusinessTime, pair.Value.BusinessDays)));
            }
        }

        // Every status has exactly one role. Guessing one for a status the configuration does not mention
        // (a new status, a mistyped ID) would quietly move issues in or out of the delivered count.
        var withoutRole = intervals.Select(interval => interval.StatusId).Concat(raw.Issues.Select(issue => issue.StatusId)).Distinct().Where(statusId => roles.RoleOf(statusId) is null).Order(StringComparer.Ordinal).ToList();
        if (withoutRole.Count > 0)
        {
            throw new UserFacingException(
                $"These statuses have no role in statusRoles in the configuration file: {string.Join(", ", withoutRole)}. "
                + "Give each one a role (not started, work, blocked or done) and run again.");
        }

        return new DerivedData(
            [.. windows.Select(window => new DerivedSprintWindow(window.SprintId, window.Start, window.Completed))],
            intervals,
            cycles,
            memberships,
            sprintStatusTimes,
            [.. roles.StatusIds.Select((statusId, index) => new BoardStatus(statusId, index + 1, roles.IsDone(statusId)))]);
    }

    /// <summary>
    /// When a sprint's window ends: at its completion; for a closed sprint Jira gives no completion time,
    /// at its planned end (owner decision D7); null while the sprint is still running, so its window runs
    /// to the as-of time. A closed sprint with neither time stops the run: nothing says when it ended.
    /// </summary>
    private static DateTimeOffset? WindowEnd(RawSprint sprint)
    {
        if (sprint.State != SprintStates.Closed)
        {
            return sprint.Completed;
        }

        return sprint.Completed
            ?? sprint.PlannedEnd
            ?? throw new UserFacingException(
                $"Sprint {sprint.SprintId} is closed, but Jira gives it neither a completion time nor a planned end, so when it ended is unknown. No figures were derived.");
    }
}
