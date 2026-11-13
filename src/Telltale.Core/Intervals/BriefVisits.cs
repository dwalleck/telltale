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
    /// Every stay of an issue's history, in order, read from the timeline's entries alone. A stay begins when
    /// the issue enters a status and ends when it next enters another one: a change from a status to itself
    /// does not end it. The last stay has no end. A status entered and left at the same instant is a stay of no
    /// length (the same start and end), which the timeline's intervals do not hold because it took no time.
    /// One stay of no length is left out: one that begins or ends with a change dated before the issue was
    /// created, since it has no length only because the timeline treats that change as made at the creation.
    /// It is a changelog anomaly and not a visit (decision bv-D5).
    /// </summary>
    public static IReadOnlyList<StatusInterval> Stays(StatusTimeline timeline)
    {
        var entries = timeline.Entries;
        var stays = new List<StatusInterval>();
        for (int entered = 0, left; entered < entries.Count; entered = left)
        {
            left = entered + 1;
            while (left < entries.Count && entries[left].StatusId == entries[entered].StatusId)
            {
                left++;
            }

            var stay = new StatusInterval(entries[entered].StatusId, entries[entered].At, left < entries.Count ? entries[left].At : null);
            var onlyByAMovedChange = stay.End == stay.Start && (entries[entered].MovedToCreation || entries[left].MovedToCreation);
            if (!onlyByAMovedChange)
            {
                stays.Add(stay);
            }
        }

        return stays;
    }

    /// <summary>
    /// The statuses in which one issue has a brief visit that counts toward a sprint: a brief visit to a status
    /// listed in <paramref name="thresholds"/> (status ID to threshold; a status not listed is not checked,
    /// owner decision D58) that overlaps <paramref name="countedTime"/>, the time the sprint's time in status
    /// counts: inside the sprint's window, while the issue was a member (<see cref="TimeInStatus.CountedTime"/>).
    /// A brief visit made in an earlier sprint, or before the issue joined, does not count toward this one.
    /// <paramref name="statuses"/> are the issue's stays (<see cref="Stays"/>).
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
            .Where(stay => countedTime.Any(period => Overlaps(stay, period)))
            .Select(stay => stay.StatusId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The overlap rule, for a stay of any length or none: the stay overlaps a counted period when it begins
    /// inside the period (at or after its start and before its end), or begins before the period and has not
    /// ended by the period's start. So a stay of no length on the instant a period starts overlaps it, and one
    /// on the instant it ends does not (decision bv-D6), as time in status counts the one instant and not the other.
    /// </summary>
    private static bool Overlaps(StatusInterval stay, Interval period) =>
        period.Contains(stay.Start) || (stay.Start < period.Start && !(stay.End <= period.Start));

    private static readonly IReadOnlySet<string> NoStatuses = new HashSet<string>();
}
