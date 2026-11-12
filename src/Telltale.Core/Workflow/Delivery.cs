using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>The issue is in a done-role status. <paramref name="At"/> is its last move from a status that is not done into one that is.</summary>
public sealed record Delivered(DateTimeOffset At);

/// <summary>The issue is not in a done-role status now, or its history holds no move into one.</summary>
public sealed record NotDelivered;

/// <summary>
/// What became of an issue. Consume it with a switch expression that lists every case and has no
/// discard arm, so a new case is a compile error wherever an outcome is read.
/// </summary>
public union IssueOutcome(Delivered, NotDelivered);

public static class Delivery
{
    /// <summary>
    /// Decides an issue's outcome from the status it is in now, its status intervals (rebuilt from its
    /// changelog, in time order) and the configured status roles.
    /// </summary>
    /// <remarks>
    /// The issue's current status decides whether it is done; the changelog only dates the delivery.
    /// When the two disagree (the changelog ends in a status the issue is not in), or the changelog holds
    /// no move into done at all, there is no delivery moment and the issue is not delivered.
    /// </remarks>
    public static IssueOutcome Of(string currentStatusId, IReadOnlyList<StatusInterval> intervals, StatusRoles roles)
    {
        if (!roles.IsDone(currentStatusId) || intervals.Count == 0 || !roles.IsDone(intervals[^1].StatusId))
        {
            return new NotDelivered();
        }

        // Walk back over the unbroken run of done statuses the issue is in now. The run's start is
        // the last move into done from outside it: Done to Shipped stays inside the run, and an
        // earlier Done that was reopened lies before it.
        var first = intervals.Count - 1;
        while (first > 0 && roles.IsDone(intervals[first - 1].StatusId))
        {
            first--;
        }

        // A run that reaches back to the issue's creation was never entered: nothing moved the issue into done.
        return first == 0 ? new NotDelivered() : new Delivered(intervals[first].Start);
    }
}
