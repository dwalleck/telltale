using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Rebuilds all derived data from the raw rows. It is a pure function of the raw data, the
/// configured board and status roles, and the as-of time, so changing a definition means rerunning it (ADR-0002).
/// </summary>
/// <remarks>Throws <see cref="UserFacingException"/> when an issue has been in a status that has no configured role.</remarks>
public static class Derivation
{
    public static DerivedData Derive(RawData raw, long boardId, StatusRoles roles, DateTimeOffset asOf)
    {
        var statusChanges = raw.Changes
            .Where(change => change.Field == ChangeFields.Status && change.To is not null)
            .ToLookup(change => change.IssueId, change => new StatusChange(change.HistoryId, change.At, change.From, change.To!));

        // Which sprints count is decided here and nowhere else. Only the configured board's: the store can
        // still hold sprints of a board ID that was since corrected. And only sprints that have started:
        // a future sprint has no window yet, even when it carries planned dates.
        var windows = raw.Sprints
            .Where(sprint => sprint.BoardId == boardId && sprint.State != SprintStates.Future && sprint.Start is not null)
            .Select(sprint => new SprintWindow(sprint.SprintId, sprint.Start!.Value, sprint.PlannedEnd, sprint.Completed))
            .ToList();

        var deliverySprints = new DeliverySprints(windows, asOf);
        var intervals = new List<IssueStatusInterval>();
        var cycles = new List<IssueCycle>();

        foreach (var issue in raw.Issues)
        {
            var timeline = StatusTimeline.Build(issue.Created, issue.StatusId, statusChanges[issue.IssueId]);
            intervals.AddRange(timeline.Intervals.Select(interval => new IssueStatusInterval(issue.IssueId, interval.StatusId, interval.Start, interval.End)));

            cycles.Add(Delivery.Of(issue.StatusId, timeline.Intervals, roles) switch
            {
                Delivered delivered => new IssueCycle(issue.IssueId, delivered.At, Delivered: true, deliverySprints.For(delivered.At)),
                NotDelivered => new IssueCycle(issue.IssueId, CycleEnd: null, Delivered: false, DeliverySprintId: null),
            });
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
            cycles);
    }
}
