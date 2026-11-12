using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>The issue is in a done-role status. <paramref name="At"/> is its last move from a status that is not done into one that is.</summary>
public sealed record Delivered(DateTimeOffset At);

/// <summary>The issue is not in a done-role status, whatever it was earlier.</summary>
public sealed record NotDelivered;

/// <summary>
/// What became of an issue. Consume it with a switch expression that lists every case and has no
/// discard arm, so a new case is a compile error wherever an outcome is read.
/// </summary>
public union IssueOutcome(Delivered, NotDelivered);

public static class Delivery
{
    /// <summary>
    /// Decides an issue's outcome from its status intervals (in time order, the last one being the
    /// status it is in now) and the configured status roles.
    /// </summary>
    public static IssueOutcome Of(IReadOnlyList<StatusInterval> intervals, StatusRoles roles)
    {
        if (intervals.Count == 0 || !roles.IsDone(intervals[^1].StatusId))
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

        return new Delivered(intervals[first].Start);
    }
}
