using Telltale.Core.Calendar;
using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Rebuilds all derived data from the raw rows. It is a pure function of the raw data, the
/// configured status roles, abandoned resolutions, business calendar and brief visit thresholds, and the as-of time, so changing a definition means rerunning it (ADR-0002).
/// </summary>
/// <remarks>
/// Throws <see cref="UserFacingException"/> when an issue is in, or its timeline entered, a status that has no configured role,
/// or when an issue was stored before its Sprint field was fetched.
/// </remarks>
public static class Derivation
{
    public static DerivedData Derive(RawData raw, DerivationSettings settings, DateTimeOffset asOf)
    {
        var (delivery, calendar) = settings;
        var roles = delivery.Roles;

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
        var briefVisits = new List<SprintBriefVisit>();

        // The one place that asks whether any status is listed: with none, no stay is worked out and nothing is looked at.
        var checksBriefVisits = settings.BriefVisitThresholds.Count > 0;

        foreach (var issue in raw.Issues)
        {
            var timeline = timelines[issue.IssueId];
            intervals.AddRange(timeline.Intervals.Select(interval => new IssueStatusInterval(issue.IssueId, interval.StatusId, interval.Start, interval.End)));

            var reopens = Cycles.ReopenCount(timeline.Entries, roles);
            var outcome = Delivery.Of(issue.StatusId, issue.ResolutionId, timeline.Entries, delivery);

            // The one place an issue's cycle is built from its outcome.
            cycles.Add(outcome switch
            {
                Delivered delivered => DeliveredCycle(issue, delivered, deliverySprints.For(delivered.At), Cycles.Measure(timeline.Entries, roles, delivered.At, calendar), pointChanges[issue.IssueId], reopens),

                // An abandoned issue reached done, so it has a cycle end, but it is not a delivery: it belongs to
                // no delivery sprint and has no cycle time.
                Abandoned abandoned => IssueCycle.OfAbandoned(issue.IssueId, abandoned.At, reopens),
                NotDelivered => IssueCycle.OfNotDelivered(issue.IssueId, reopens),
            });

            var membership = SprintMembership.Build(issue.Created, SprintMembership.ParseSprintIds(issue.SprintIds), sprintChanges[issue.IssueId]);
            anomalies.AddRange(ChangelogAnomalies.Of(issue, timeline, membership, outcome, roles));
            memberships.AddRange(membership.Intervals.Select(interval => new IssueMembershipInterval(issue.IssueId, interval.SprintId, interval.Start, interval.End)));

            // Only sprints that count (the windows above) have time in status. A sprint the issue was never in
            // gives nothing, so it is not measured at all: most issues were in one or two of the board's sprints.
            var memberOf = membership.Intervals.Select(interval => interval.SprintId).ToHashSet(StringComparer.Ordinal);

            // The issue's stays are the same for every sprint: worked out once, when the first sprint needs them.
            IReadOnlyList<Stay>? stays = null;
            foreach (var window in windows.Where(counted => memberOf.Contains(counted.SprintId)))
            {
                // The time this sprint counts of this issue, worked out once for the time in status and the brief visits.
                var countedTime = TimeInStatus.CountedTime(window, membership.Intervals, asOf);
                sprintStatusTimes.AddRange(TimeInStatus.During(countedTime, timeline.Intervals, calendar, asOf)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new SprintStatusTime(issue.IssueId, window.SprintId, pair.Key, pair.Value.CalendarTime, pair.Value.BusinessTime, pair.Value.BusinessDays)));

                // A brief visit is not a changelog anomaly: nothing here adds to the anomalies or to what derive logs.
                if (checksBriefVisits)
                {
                    briefVisits.AddRange(BriefVisits.During(countedTime, stays ??= BriefVisits.Stays(timeline), settings.BriefVisitThresholds)
                        .Order(StringComparer.Ordinal)
                        .Select(statusId => new SprintBriefVisit(issue.IssueId, window.SprintId, statusId)));
                }
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
            BriefVisits = briefVisits,
        };
    }

    /// <summary>
    /// A delivered issue's cycle, in the bucket of its estimate at the start of its cycle, so a later
    /// re-estimate does not move it (ADR-0006). An issue that never entered a work status has no cycle start;
    /// its bucket is its estimate at the delivery moment (owner decision D35).
    /// </summary>
    private static IssueCycle DeliveredCycle(RawIssue issue, Delivered delivered, string? deliverySprintId, Cycle? cycle, IEnumerable<PointChange> pointChanges, int reopens) =>
        IssueCycle.OfDelivered(
            issue.IssueId,
            delivered.At,
            deliverySprintId,
            cycle,
            cycle is { } measured
                ? PointHistory.EstimateAt(measured.Start, measured.StartHistoryId, issue.StoryPoints, pointChanges)
                : PointHistory.EstimateAt(delivered.At, delivered.HistoryId, issue.StoryPoints, pointChanges),
            reopens);
}

/// <summary>Everything a rebuild is decided with besides the raw data and the as-of time: how a delivery is decided, the business calendar, and the brief visit thresholds.</summary>
public sealed record DerivationSettings(DeliveryRules Delivery, BusinessCalendar Calendar)
{
    /// <summary>The threshold of every status checked for brief visits (GLOSSARY: Brief visit), by status ID. Empty when nothing is checked.</summary>
    public IReadOnlyDictionary<string, TimeSpan> BriefVisitThresholds { get; init; } = new Dictionary<string, TimeSpan>();
}
