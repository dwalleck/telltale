using Telltale.Core.Calendar;
using Telltale.Core.Intervals;

namespace Telltale.Core.Workflow;

/// <summary>
/// A delivered issue's cycle time: it starts at the issue's first entry into a work-role status and ends at
/// the delivery moment. Measured in calendar days (elapsed time divided by 24 hours) and in business days.
/// </summary>
public sealed record Cycle(DateTimeOffset Start, double CalendarDays, double BusinessDays)
{
    /// <summary>
    /// The changelog entry of the move that started the cycle; null when the issue was created in a work-role
    /// status. It places the start among other changes made at the same instant.
    /// </summary>
    public string? StartHistoryId { get; init; }
}

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
        return new Cycle(span.Start, span.Duration / TimeSpan.FromDays(1), calendar.BusinessDays(span)) { StartHistoryId = started.HistoryId };
    }

    /// <summary>
    /// Whether the issue's cycle starts with a brief visit (GLOSSARY: Brief visit; owner decision D90): the stay
    /// that begins at the cycle's start is a brief visit under its own status's threshold. The cycle's start is
    /// <see cref="Measure"/>'s, the first entry into a work-role status, and that entry always begins a stay, so
    /// the stay is the first of <paramref name="stays"/> (<see cref="BriefVisits.Stays"/>) in a work-role status.
    /// It is found by its place and not by its time: several stays can begin at one instant.
    /// <paramref name="thresholds"/> is status ID to threshold; a status not listed is not checked, so a first
    /// stay in one is not a brief visit, whatever follows it. A brief visit later in the cycle does not count,
    /// and neither does a first stay bounded by a changelog anomaly (<see cref="BriefVisits.IsBrief"/>). A move
    /// into a work status and straight back out counts, though the real start came later (D91). An issue that
    /// never entered a work-role status has no cycle, and the answer is no.
    /// </summary>
    public static bool StartsWithBriefVisit(IReadOnlyList<Stay> stays, StatusRoles roles, IReadOnlyDictionary<string, TimeSpan> thresholds) =>
        stays.FirstOrDefault(stay => roles.IsWork(stay.StatusId)) is { } first
        && thresholds.TryGetValue(first.StatusId, out var threshold)
        && BriefVisits.IsBrief(first, threshold);

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
