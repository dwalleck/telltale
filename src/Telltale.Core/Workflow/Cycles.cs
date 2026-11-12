using Telltale.Core.Calendar;
using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>
/// A delivered issue's cycle: from its first entry into a work-role status to its delivery moment,
/// measured in calendar days (elapsed time divided by 24 hours) and in business days.
/// </summary>
public sealed record Cycle(DateTimeOffset Start, DateTimeOffset End, double CalendarDays, double BusinessDays);

public static class Cycles
{
    /// <summary>
    /// Measures the cycle of an issue delivered at <paramref name="deliveredAt"/> from its status intervals
    /// (in time order). The cycle starts at the first entry into a work-role status, whichever one it is; time
    /// before that does not count, in a blocked status or any other, and everything after it does. An issue
    /// that never entered a work-role status has no cycle: the result is null.
    /// </summary>
    public static Cycle? Measure(IReadOnlyList<StatusInterval> intervals, StatusRoles roles, DateTimeOffset deliveredAt, BusinessCalendar calendar)
    {
        var started = intervals.FirstOrDefault(interval => roles.IsWork(interval.StatusId));
        if (started is null)
        {
            return null;
        }

        var span = new Interval(started.Start, deliveredAt);
        return new Cycle(span.Start, span.End, span.Duration / TimeSpan.FromDays(1), calendar.BusinessDays(span));
    }

    /// <summary>
    /// How many times the issue was reopened: its moves out of a done-role status into one that is not done,
    /// read from its status intervals (in time order). A move between two done-role statuses is not a reopen.
    /// </summary>
    public static int ReopenCount(IReadOnlyList<StatusInterval> intervals, StatusRoles roles)
    {
        var reopens = 0;
        for (var index = 1; index < intervals.Count; index++)
        {
            if (roles.IsDone(intervals[index - 1].StatusId) && !roles.IsDone(intervals[index].StatusId))
            {
                reopens++;
            }
        }

        return reopens;
    }
}
