namespace Telltale.Core.Intervals;

/// <summary>
/// The brief visit rule (GLOSSARY: Brief visit; owner decisions D58 to D60). A visit is one unbroken stay in
/// a status: one <see cref="StatusInterval"/> of the issue's whole history, not clipped to a sprint window or
/// to sprint membership. It is brief when it ended and lasted less than the status's threshold, in clock time:
/// the difference between the two recorded instants.
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
    /// The statuses in which one issue has a brief visit that counts toward <paramref name="sprint"/>: a brief
    /// visit to a status listed in <paramref name="thresholds"/> (status ID to threshold; a status not listed
    /// is not checked, owner decision D58) that overlaps the time the sprint's time in status counts: inside
    /// the sprint's window, while the issue was a member (<see cref="TimeInStatus.CountedTime"/>). A brief visit
    /// made in an earlier sprint, or before the issue joined, does not count toward this one. Whether the issue
    /// is in the status's time in status row for the sprint is not asked here: the metric view asks it.
    /// </summary>
    public static IReadOnlySet<string> ForSprint(
        SprintWindow sprint,
        IEnumerable<StatusInterval> statuses,
        IEnumerable<MembershipInterval> membership,
        IReadOnlyDictionary<string, TimeSpan> thresholds,
        DateTimeOffset asOf)
    {
        var counted = TimeInStatus.CountedTime(sprint, membership, asOf);
        return statuses
            .Where(stay => thresholds.TryGetValue(stay.StatusId, out var threshold) && IsBrief(stay, threshold))
            .Where(stay => counted.Any(slice => stay.Resolve(asOf).Intersect(slice) is not null))
            .Select(stay => stay.StatusId)
            .ToHashSet(StringComparer.Ordinal);
    }
}
