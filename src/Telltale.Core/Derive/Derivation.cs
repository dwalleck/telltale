using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Rebuilds all derived data from the raw rows. It is a pure function of the raw data, the
/// configured status roles, abandoned resolutions and business calendar, and the as-of time, so changing a definition means rerunning it (ADR-0002).
/// </summary>
/// <remarks>
/// Throws <see cref="UserFacingException"/> when an issue is in, or its timeline entered, a status that has no configured role,
/// or when an issue was stored before its Sprint field was fetched.
/// </remarks>
public static class Derivation
{
    public static DerivedData Derive(RawData raw, StatusRoles roles, IReadOnlySet<string> abandonedResolutionIds, BusinessCalendar calendar, DateTimeOffset asOf)
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

        var pointChanges = raw.Changes
            .Where(change => change.Field == ChangeFields.Points)
            .ToLookup(change => change.IssueId, change => PointChange.FromRaw(change.HistoryId, change.At, change.From, change.To));

        // Which sprints count is decided by SprintWindows: the sprints that have a window. Every sprint in the
        // store is the configured board's: a backfill replaces them all, and no command reads a store filled
        // under another board (CollectionScope.RequireSameAs).
        var windows = raw.Sprints.Select(SprintWindows.Of).OfType<SprintWindow>().ToList();

        // Every status has exactly one role. Guessing one for a status the configuration does not mention
        // (a new status, a mistyped ID) would quietly move issues in or out of the delivered count. Checked
        // before anything reads a role, over exactly what the rules read: the status each issue is in now and
        // every entry of its timeline. The entries, not the intervals: a status entered and left at the same
        // instant has no interval, and is still a move the delivery and reopen rules read. A later change's
        // "from" that the timeline was not in is an anomaly, and no rule reads it.
        var timelines = raw.Issues.ToDictionary(
            issue => issue.IssueId,
            issue => StatusTimeline.Build(issue.Created, issue.StatusId, statusChanges[issue.IssueId]),
            StringComparer.Ordinal);
        var withoutRole = raw.Issues
            .SelectMany(issue => timelines[issue.IssueId].Entries.Select(entry => entry.StatusId).Append(issue.StatusId))
            .Distinct(StringComparer.Ordinal)
            .Where(statusId => roles.RoleOf(statusId) is null)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (withoutRole.Count > 0)
        {
            throw new UserFacingException(
                $"These statuses have no role in statusRoles in the configuration file: {string.Join(", ", withoutRole)}. "
                + "Give each one a role (not started, work, blocked or done) and run again.");
        }

        var deliverySprints = new DeliverySprints(windows, asOf);
        var intervals = new List<IssueStatusInterval>();
        var cycles = new List<IssueCycle>();
        var memberships = new List<IssueMembershipInterval>();
        var sprintStatusTimes = new List<SprintStatusTime>();
        var anomalies = new List<IssueAnomaly>();

        foreach (var issue in raw.Issues)
        {
            var timeline = timelines[issue.IssueId];
            intervals.AddRange(timeline.Intervals.Select(interval => new IssueStatusInterval(issue.IssueId, interval.StatusId, interval.Start, interval.End)));

            var reopens = Cycles.ReopenCount(timeline.Entries, roles);
            var outcome = Delivery.Of(issue.StatusId, issue.ResolutionId, timeline.Entries, roles, abandonedResolutionIds);
            cycles.Add(outcome switch
            {
                Delivered delivered => new IssueCycle(issue.IssueId, delivered.At, Delivered: true, deliverySprints.For(delivered.At), reopens)
                {
                    Cycle = Cycles.Measure(timeline.Entries, roles, delivered.At, calendar),
                    PointBucket = issue.StoryPoints,
                }.WithBucketFromHistory(delivered, pointChanges[issue.IssueId]),

                // An abandoned issue reached done, so it has a cycle end, but it is not a delivery: it belongs to
                // no delivery sprint and has no cycle time.
                Abandoned abandoned => new IssueCycle(issue.IssueId, abandoned.At, Delivered: false, DeliverySprintId: null, reopens),
                NotDelivered => new IssueCycle(issue.IssueId, CycleEnd: null, Delivered: false, DeliverySprintId: null, reopens),
            });

            var membership = SprintMembership.Build(issue.Created, SprintMembership.ParseSprintIds(issue.SprintIds), sprintChanges[issue.IssueId]);
            anomalies.AddRange(ChangelogAnomalies.Of(issue, timeline, membership, outcome, roles));
            memberships.AddRange(membership.Intervals.Select(interval => new IssueMembershipInterval(issue.IssueId, interval.SprintId, interval.Start, interval.End)));

            // Only sprints that count (the windows above) have time in status. A sprint the issue was never in
            // gives nothing, so it is not measured at all: most issues were in one or two of the board's sprints.
            var memberOf = membership.Intervals.Select(interval => interval.SprintId).ToHashSet(StringComparer.Ordinal);
            foreach (var window in windows.Where(counted => memberOf.Contains(counted.SprintId)))
            {
                sprintStatusTimes.AddRange(TimeInStatus.ForSprint(window, timeline.Intervals, membership.Intervals, calendar, asOf)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new SprintStatusTime(issue.IssueId, window.SprintId, pair.Key, pair.Value.CalendarTime, pair.Value.BusinessTime, pair.Value.BusinessDays)));
            }
        }

        return new DerivedData(
            [.. windows.Select(window => new DerivedSprintWindow(window.SprintId, window.Start, window.Completed))],
            intervals,
            cycles,
            memberships,
            sprintStatusTimes,
            [.. roles.StatusIds.Select((statusId, index) => new BoardStatus(statusId, index + 1, roles.IsDone(statusId)))])
        {
            AsOf = asOf,
            Anomalies = anomalies,
            ClosedSprintsWithoutAnEnd = [.. raw.Sprints.Where(SprintWindows.IsClosedWithoutAnEnd).Select(sprint => sprint.SprintId)],
        };
    }

    /// <summary>
    /// Puts a delivered issue in the bucket of its estimate at the start of its cycle, so a later re-estimate
    /// does not move it (ADR-0006). An issue that never entered a work status has no cycle start; its bucket
    /// is its estimate at the delivery moment (owner decision D35). <paramref name="cycle"/> comes with the
    /// current estimate as its bucket.
    /// </summary>
    private static IssueCycle WithBucketFromHistory(this IssueCycle cycle, Delivered delivered, IEnumerable<PointChange> pointChanges) =>
        cycle with
        {
            PointBucket = cycle.Cycle is { } measured
                ? PointHistory.EstimateAt(measured.Start, measured.StartHistoryId, cycle.PointBucket, pointChanges)
                : PointHistory.EstimateAt(delivered.At, delivered.HistoryId, cycle.PointBucket, pointChanges),
        };
}
