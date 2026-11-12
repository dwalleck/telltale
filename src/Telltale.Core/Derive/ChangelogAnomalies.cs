using Telltale.Core.Intervals;
using Telltale.Core.Storage;
using Telltale.Core.Workflow;

namespace Telltale.Core.Derive;

/// <summary>
/// Finds what does not line up in an issue's changelog. An anomaly never stops anything: the timeline and
/// the membership are still built. It is the issue's ID and a fixed kind, nothing from the changelog itself.
/// </summary>
public static class ChangelogAnomalies
{
    /// <summary>
    /// One issue's anomalies, at most one of each kind, given the timeline and membership built from its history
    /// and the <paramref name="outcome"/> <see cref="Delivery.Of"/> gave it.
    /// </summary>
    public static IEnumerable<IssueAnomaly> Of(RawIssue issue, StatusTimeline timeline, SprintMembership membership, IssueOutcome outcome, StatusRoles roles)
    {
        if (timeline.Anomalies.Count > 0)
        {
            yield return new IssueAnomaly(issue.IssueId, ChangelogAnomalyKind.StatusHistory);
        }

        if (membership.Anomalies.Count > 0)
        {
            yield return new IssueAnomaly(issue.IssueId, ChangelogAnomalyKind.SprintHistory);
        }

        // An issue that is done now and has no delivery moment: its history either ends outside done, or
        // stays in done from the issue's creation on.
        if (roles.IsDone(issue.StatusId) && outcome.Value is NotDelivered)
        {
            yield return new IssueAnomaly(
                issue.IssueId,
                roles.IsDone(timeline.Entries[^1].StatusId) ? ChangelogAnomalyKind.DoneWithoutMoveIntoDone : ChangelogAnomalyKind.DoneButHistoryEndsOutsideDone);
        }
    }
}
