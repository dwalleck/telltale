namespace Telltale.Core.Intervals;

/// <summary>
/// The brief visit rule (GLOSSARY: Brief visit; owner decisions D58 to D60). A visit is one unbroken stay in
/// a status: one <see cref="StatusInterval"/> of the issue's whole history, not clipped to a sprint window or
/// to sprint membership. It is brief when it ended and lasted less than the status's threshold, in clock time:
/// the difference between the two recorded instants. A status entered and left at the same instant is a stay of
/// no length, and so brief under any threshold (decision bv-D1 of the brief-status-visits run).
/// </summary>
public static class BriefVisits
{
    /// <summary>
    /// Whether <paramref name="stay"/> is a brief visit under <paramref name="threshold"/>. A stay the issue
    /// is still in is not a visit for this rule, however short it is so far.
    /// </summary>
    public static bool IsBrief(StatusInterval stay, TimeSpan threshold) =>
        stay.End is { } left && left - stay.Start < threshold;

    /// <summary>
    /// Every stay of an issue's history: the timeline's intervals, and one stay of no length (the same start and
    /// end) for each status the issue entered and left at the same instant. The timeline keeps no interval for
    /// such a status, because it took no time, so these are found from the timeline's entries: two entries in a
    /// row at one instant mean the first one's status was left as it was entered. The status the issue is in
    /// now is never one of them: its stay has no end.
    /// </summary>
    public static IEnumerable<StatusInterval> Stays(StatusTimeline timeline) =>
        timeline.Intervals.Concat(timeline.Entries
            .Zip(timeline.Entries.Skip(1), (entered, next) => (entered.StatusId, entered.At, Left: next.At))
            .Where(stay => stay.Left == stay.At)
            .Select(stay => new StatusInterval(stay.StatusId, stay.At, stay.Left)));

    /// <summary>
    /// The statuses in which one issue has a brief visit that counts toward a sprint: a brief visit to a status
    /// listed in <paramref name="thresholds"/> (status ID to threshold; a status not listed is not checked,
    /// owner decision D58) that overlaps <paramref name="countedTime"/>, the time the sprint's time in status
    /// counts: inside the sprint's window, while the issue was a member (<see cref="TimeInStatus.CountedTime"/>).
    /// A brief visit made in an earlier sprint, or before the issue joined, does not count toward this one.
    /// <paramref name="statuses"/> are the issue's stays (<see cref="Stays"/>). A stay of no length overlaps
    /// the counted time when its instant is inside it: at or after the start of one of its periods and before
    /// that period's end, as for any instant of a half-open span (<see cref="Interval.Contains"/>).
    /// Whether the issue is in the status's time in status row for the sprint is not asked here: the metric
    /// view asks it. With no status listed nothing is looked at.
    /// </summary>
    public static IReadOnlySet<string> During(
        IReadOnlyList<Interval> countedTime,
        IEnumerable<StatusInterval> statuses,
        IReadOnlyDictionary<string, TimeSpan> thresholds)
    {
        if (thresholds.Count == 0)
        {
            return NoStatuses;
        }

        return statuses
            .Where(stay => thresholds.TryGetValue(stay.StatusId, out var threshold) && IsBrief(stay, threshold))
            .Where(stay => stay.End is { } left && countedTime.Any(slice => Overlaps(slice, stay.Start, left)))
            .Select(stay => stay.StatusId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool Overlaps(Interval slice, DateTimeOffset entered, DateTimeOffset left) =>
        entered == left ? slice.Contains(entered) : slice.Intersect(new Interval(entered, left)) is not null;

    private static readonly IReadOnlySet<string> NoStatuses = new HashSet<string>();
}
