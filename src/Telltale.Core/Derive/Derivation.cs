using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Rebuilds all derived data from the raw rows. It is a pure function of the raw data, the
/// configured status roles and the as-of time, so changing a definition means rerunning it (ADR-0002).
/// </summary>
public static class Derivation
{
    public static DerivedData Derive(RawData raw, StatusRoles roles, DateTimeOffset asOf)
    {
        var statusChanges = raw.Changes
            .Where(change => change.Field == ChangeFields.Status && change.To is not null)
            .ToLookup(change => change.IssueId, change => new StatusChange(change.HistoryId, change.At, change.From, change.To!));

        // A future sprint has no window yet, even when it carries planned dates.
        var windows = raw.Sprints
            .Where(sprint => sprint.State != SprintStates.Future && sprint.Start is not null)
            .Select(sprint => new SprintWindow(sprint.SprintId, sprint.Start!.Value, sprint.PlannedEnd, sprint.Completed))
            .ToList();

        var intervals = new List<IssueStatusInterval>();
        var cycles = new List<IssueCycle>();

        foreach (var issue in raw.Issues)
        {
            var timeline = StatusTimeline.Build(issue.Created, issue.StatusId, statusChanges[issue.IssueId]);
            intervals.AddRange(timeline.Intervals.Select(interval => new IssueStatusInterval(issue.IssueId, interval.StatusId, interval.Start, interval.End)));

            cycles.Add(Delivery.Of(timeline.Intervals, roles) switch
            {
                Delivered delivered => new IssueCycle(issue.IssueId, delivered.At, Delivered: true, DeliverySprint.For(delivered.At, windows, asOf)),
                NotDelivered => new IssueCycle(issue.IssueId, CycleEnd: null, Delivered: false, DeliverySprintId: null),
            });
        }

        return new DerivedData(intervals, cycles);
    }
}
