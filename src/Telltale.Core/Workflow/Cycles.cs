using Telltale.Core.Calendar;
using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>
/// A delivered issue's cycle time: it starts at the issue's first entry into a work-role status and ends at
/// the delivery moment. Measured in calendar days (elapsed time divided by 24 hours) and in business days.
/// </summary>
public sealed record Cycle(DateTimeOffset Start, double CalendarDays, double BusinessDays);

public static class Cycles
{
    /// <summary>
    /// Measures the cycle of an issue delivered at <paramref name="deliveredAt"/> (the moment
    /// <see cref="Delivery.Of"/> gives for the same entries) from the statuses it entered
    /// (<see cref="StatusTimeline.Entries"/>). The cycle starts at the first entry into a work-role status,
    /// whichever one it is; time before that does not count, in a blocked status or any other, and everything
    /// after it does. A work-role status entered at the delivery moment itself gives a cycle of zero days.
    /// An issue that never entered a work-role status has no cycle: the result is null.
    /// </summary>
    public static Cycle? Measure(IReadOnlyList<StatusEntry> entries, StatusRoles roles, DateTimeOffset deliveredAt, BusinessCalendar calendar)
    {
        var started = entries.FirstOrDefault(entry => roles.IsWork(entry.StatusId));
        if (started is null)
        {
            return null;
        }

        var span = new Interval(started.At, deliveredAt);
        return new Cycle(span.Start, span.Duration / TimeSpan.FromDays(1), calendar.BusinessDays(span));
    }

    /// <summary>
    /// How many times the issue was reopened: its transitions out of a done-role status into one that is not
    /// done, read from the statuses it entered (<see cref="StatusTimeline.Entries"/>). A move between two
    /// done-role statuses is not a reopen.
    /// </summary>
    public static int ReopenCount(IReadOnlyList<StatusEntry> entries, StatusRoles roles)
    {
        var reopens = 0;
        for (var index = 1; index < entries.Count; index++)
        {
            if (roles.IsDone(entries[index - 1].StatusId) && !roles.IsDone(entries[index].StatusId))
            {
                reopens++;
            }
        }

        return reopens;
    }
}
