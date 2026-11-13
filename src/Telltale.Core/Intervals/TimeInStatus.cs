using Telltale.Core.Calendar;

namespace Telltale.Core.Intervals;

/// <summary>
/// A sprint's time window. A completed sprint ends at <paramref name="Completed"/>; a sprint not yet
/// completed has none and runs until asOf. The planned end plays no part here: where it stands in for
/// a missing completion time, the caller has already decided so.
/// </summary>
public sealed record SprintWindow(string SprintId, DateTimeOffset Start, DateTimeOffset? Completed)
{
    public Interval Resolve(DateTimeOffset asOf)
    {
        var end = Completed ?? asOf;
        return new Interval(Start, end > Start ? end : Start);
    }
}

/// <summary>Time spent in one status, measured three ways: calendar time (comparable with other teams), business time, and business days.</summary>
public sealed record StatusDuration(TimeSpan CalendarTime, TimeSpan BusinessTime, double BusinessDays)
{
    public static StatusDuration Zero { get; } = new(TimeSpan.Zero, TimeSpan.Zero, 0);

    public StatusDuration Add(Interval slice, BusinessCalendar calendar)
    {
        var (time, days) = calendar.Measure(slice);
        return new StatusDuration(CalendarTime + slice.Duration, BusinessTime + time, BusinessDays + days);
    }
}

public static class TimeInStatus
{
    /// <summary>
    /// How long one issue spent in each status during one sprint: its status history
    /// clipped to the sprint's window and to the periods the issue actually belonged to
    /// that sprint. Returns only statuses with time in the sprint, keyed by status ID.
    /// </summary>
    public static IReadOnlyDictionary<string, StatusDuration> ForSprint(
        SprintWindow sprint,
        IEnumerable<StatusInterval> statuses,
        IEnumerable<MembershipInterval> membership,
        BusinessCalendar calendar,
        DateTimeOffset asOf)
    {
        var slices = CountedTime(sprint, membership, asOf);
        var totals = new Dictionary<string, StatusDuration>(StringComparer.Ordinal);

        foreach (var status in statuses)
        {
            var span = status.Resolve(asOf);
            foreach (var slice in slices)
            {
                if (span.Intersect(slice) is { } part)
                {
                    var soFar = totals.GetValueOrDefault(status.StatusId) ?? StatusDuration.Zero;
                    totals[status.StatusId] = soFar.Add(part, calendar);
                }
            }
        }

        return totals;
    }

    /// <summary>
    /// The time one issue's history counts toward one sprint: the periods it was a member of the sprint, clipped
    /// to the sprint's window. Stated once, for the time in status and for which brief visits count toward the
    /// sprint (<see cref="BriefVisits.ForSprint"/>).
    /// </summary>
    public static IReadOnlyList<Interval> CountedTime(SprintWindow sprint, IEnumerable<MembershipInterval> membership, DateTimeOffset asOf)
    {
        var window = sprint.Resolve(asOf);
        return [.. membership
            .Where(m => m.SprintId == sprint.SprintId)
            .Select(m => m.Resolve(asOf).Intersect(window))
            .Where(slice => slice.HasValue)
            .Select(slice => slice!.Value)];
    }
}
