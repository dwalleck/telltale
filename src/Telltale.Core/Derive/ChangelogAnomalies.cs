using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Finds the issues whose changelog does not line up. An anomaly never stops anything: the timeline and
/// the membership are still built. It is the issue's ID and a fixed kind, nothing from the changelog itself.
/// </summary>
public static class ChangelogAnomalies
{
    private static readonly IReadOnlySet<string> NoResolutions = new HashSet<string>();

    /// <summary>Every anomaly in the raw rows, in issue order. This is what the sync logs.</summary>
    public static IReadOnlyList<IssueAnomaly> Find(RawData raw, StatusRoles roles)
    {
        var statusChanges = Derivation.StatusChangesByIssue(raw);
        var sprintChanges = Derivation.SprintChangesByIssue(raw);

        return [.. raw.Issues.SelectMany(issue => Of(
            issue,
            StatusTimeline.Build(issue.Created, issue.StatusId, statusChanges[issue.IssueId]),
            SprintMembership.Build(issue.Created, SprintMembership.ParseSprintIds(issue.SprintIds), sprintChanges[issue.IssueId]),
            roles))];
    }

    /// <summary>One issue's anomalies, at most one of each kind, given the timeline and membership built from its history.</summary>
    public static IEnumerable<IssueAnomaly> Of(RawIssue issue, StatusTimeline timeline, SprintMembership membership, StatusRoles roles)
    {
        if (timeline.Anomalies.Count > 0)
        {
            yield return new IssueAnomaly(issue.IssueId, ChangelogAnomalyKind.StatusHistory);
        }

        if (membership.Anomalies.Count > 0)
        {
            yield return new IssueAnomaly(issue.IssueId, ChangelogAnomalyKind.SprintHistory);
        }

        // An issue that is done now and has no delivery moment (Delivery.Of): its history either ends outside
        // done, or stays in done from the issue's creation on. The resolution plays no part in that.
        if (roles.IsDone(issue.StatusId) && Delivery.Of(issue.StatusId, issue.ResolutionId, timeline.Entries, roles, NoResolutions).Value is NotDelivered)
        {
            yield return new IssueAnomaly(
                issue.IssueId,
                roles.IsDone(timeline.Entries[^1].StatusId) ? ChangelogAnomalyKind.DoneWithoutMoveIntoDone : ChangelogAnomalyKind.DoneButHistoryEndsOutsideDone);
        }
    }
}
