namespace Telltale.Core.Intervals;

/// <summary>
/// One unbroken stay of an issue in a status, over its whole history (<see cref="BriefVisits.Stays"/>): from the
/// moment the issue entered the status to the moment it next entered another one. A null <see cref="End"/>
/// means the issue is still in the status. It is not a <see cref="StatusInterval"/>: the timeline's intervals
/// are cut at every change, also one from a status to itself, and hold no stay of no length, so the brief
/// visit rule takes stays and nothing else.
/// </summary>
public sealed record Stay(string StatusId, DateTimeOffset Start, DateTimeOffset? End)
{
    /// <summary>
    /// True when the stay begins or ends with a change that is a changelog anomaly
    /// (<see cref="StatusEntry.IsAnomaly"/>): one dated before the issue was created, or one whose from-status
    /// is not the status the issue was in. The stay's real length is then unknown, and it is never a brief
    /// visit (decision bv-D15 of the brief-status-visits run, 2026-11-13).
    /// </summary>
    public bool BoundedByAnAnomaly { get; init; }
}

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
    /// is still in is not a visit for this rule, however short it is so far, and neither is a stay that
    /// begins or ends with a changelog anomaly, however short it looks (<see cref="Stay.BoundedByAnAnomaly"/>).
    /// </summary>
    public static bool IsBrief(Stay stay, TimeSpan threshold) =>
        !stay.BoundedByAnAnomaly &&
        stay.End is { } left && left - stay.Start < threshold;

    /// <summary>
    /// Every stay of an issue's history, in order, read from the timeline's entries alone. A stay begins when
    /// the issue enters a status and ends when it next enters another one: a change from a status to itself
    /// does not end it. A change into the status the issue is in that names another from-status does end it,
    /// and begins a new stay: the issue left and came back, and the change that took it out is missing. The
    /// last stay has no end. A status entered and left at the same instant is a stay of no
    /// length (the same start and end), which the timeline's intervals do not hold because it took no time.
    /// A stay that begins or ends with a change that is a changelog anomaly is marked
    /// (<see cref="Stay.BoundedByAnAnomaly"/>; decisions bv-D5 and bv-D15): where such a change puts the
    /// stay's start or end is not where the issue is known to have entered or left the status.
    /// </summary>
    public static IReadOnlyList<Stay> Stays(StatusTimeline timeline)
    {
        var entries = timeline.Entries;
        var stays = new List<Stay>();
        for (int entered = 0, left; entered < entries.Count; entered = left)
        {
            left = entered + 1;
            while (left < entries.Count && entries[left].StatusId == entries[entered].StatusId && !entries[left].FromAnotherStatus)
            {
                left++;
            }

            stays.Add(new Stay(entries[entered].StatusId, entries[entered].At, left < entries.Count ? entries[left].At : null)
            {
                BoundedByAnAnomaly = entries[entered].IsAnomaly || (left < entries.Count && entries[left].IsAnomaly),
            });
        }

        return stays;
    }

    /// <summary>
    /// The statuses in which one issue has a brief visit that counts toward a sprint: a brief visit to a status
    /// listed in <paramref name="thresholds"/> (status ID to threshold; a status not listed is not checked,
    /// owner decision D58) that overlaps <paramref name="countedTime"/>, the time the sprint's time in status
    /// counts: inside the sprint's window, while the issue was a member (<see cref="TimeInStatus.CountedTime"/>).
    /// A brief visit made in an earlier sprint, or before the issue joined, does not count toward this one.
    /// <paramref name="stays"/> are the issue's stays (<see cref="Stays"/>).
    /// Whether the issue is in the status's time in status row for the sprint is not asked here: the metric
    /// view asks it. With no status listed nothing is looked at.
    /// </summary>
    public static IReadOnlySet<string> During(
        IReadOnlyList<Interval> countedTime,
        IEnumerable<Stay> stays,
        IReadOnlyDictionary<string, TimeSpan> thresholds)
    {
        if (thresholds.Count == 0)
        {
            return NoStatuses;
        }

        return stays
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
    private static bool Overlaps(Stay stay, Interval period) =>
        period.Contains(stay.Start) || (stay.Start < period.Start && !(stay.End <= period.Start));

    private static readonly IReadOnlySet<string> NoStatuses = new HashSet<string>();
}
