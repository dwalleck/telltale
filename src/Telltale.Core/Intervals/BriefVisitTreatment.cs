using Telltale.Core.Workflow;

namespace Telltale.Core.Intervals;

/// <summary>Brief visits are counted and no figure is changed because of one: what the tool does when nothing else is chosen.</summary>
public sealed record CountBriefVisits;

/// <summary>Brief visits are left out of the figure.</summary>
public sealed record ExcludeBriefVisits;

/// <summary>
/// What is done with brief visits when a figure is measured (GLOSSARY: Treatment; owner decision D83). A
/// closed set of cases. Consume it with a switch expression that lists every case and has no discard arm, so
/// that a later case (a visit replaced by a length from another source, D88) is a compile error wherever a
/// treatment is read.
/// </summary>
public union BriefVisitTreatment(CountBriefVisits, ExcludeBriefVisits);

/// <summary>
/// The treatment of brief visits, applied (GLOSSARY: Treatment): the one place a treatment changes what is
/// measured, with one function for each metric a treatment can change. For time in status it sits between
/// finding an issue's stays (<see cref="BriefVisits.Stays"/>) and measuring its time in status
/// (<see cref="TimeInStatus.During"/>), and reads the brief visit rule (<see cref="BriefVisits.IsBriefVisit"/>)
/// without restating it. For cycle time it sits between measuring a delivered issue's cycle and storing it,
/// and reads whether the cycle starts with a brief visit without deciding it.
/// </summary>
public static class BriefVisitTreatments
{
    /// <summary>The label of <see cref="CountBriefVisits"/> in the configuration file and in a rebuild's record.</summary>
    public const string Count = "count";

    /// <summary>The label of <see cref="ExcludeBriefVisits"/> in the configuration file and in a rebuild's record.</summary>
    public const string Exclude = "exclude";

    /// <summary>Reads a treatment from its label. False for anything that is not one of the labels.</summary>
    public static bool TryParse(string? label, out BriefVisitTreatment treatment)
    {
        switch (label)
        {
            case Count:
                treatment = new CountBriefVisits();
                return true;
            case Exclude:
                treatment = new ExcludeBriefVisits();
                return true;
            default:
                treatment = new CountBriefVisits();
                return false;
        }
    }

    /// <summary>The label <see cref="TryParse"/> reads <paramref name="treatment"/> from.</summary>
    public static string LabelOf(BriefVisitTreatment treatment) =>
        treatment switch
        {
            CountBriefVisits => Count,
            ExcludeBriefVisits => Exclude,
        };

    /// <summary>Whether <paramref name="treatment"/> leaves brief visits out of the figure it is for.</summary>
    public static bool LeavesOut(BriefVisitTreatment treatment) =>
        treatment switch
        {
            CountBriefVisits => false,
            ExcludeBriefVisits => true,
        };

    /// <summary>
    /// The status intervals an issue's time in status is measured from, under <paramref name="treatment"/>.
    /// <paramref name="intervals"/> are the intervals of the issue's whole history and <paramref name="stays"/>
    /// its stays, both of one timeline; <paramref name="thresholds"/> lists the statuses checked (status ID to
    /// threshold). Under <see cref="CountBriefVisits"/> every interval is measured, as it is. Under
    /// <see cref="ExcludeBriefVisits"/> the span of every brief visit to a listed status is left out (owner
    /// decision D84): the intervals of that status from the stay's start to its end, which are several when a
    /// change from the status to itself cut the stay. This is done to the whole history, before anything is
    /// clipped to a sprint window or to membership. Nothing else is left out: not a stay in a status that is
    /// not listed, not one the issue is still in, and not one bounded by a changelog anomaly, since none of
    /// those is a brief visit. A stay of no length is brief and has no interval, so it has no time to leave out.
    /// When nothing is left out, <paramref name="intervals"/> itself is returned, the same list: a caller that
    /// has already measured it can tell that it need not measure again, without reading the treatment's case.
    /// </summary>
    public static IReadOnlyList<StatusInterval> IntervalsToMeasure(
        IReadOnlyList<StatusInterval> intervals,
        IReadOnlyList<Stay> stays,
        IReadOnlyDictionary<string, TimeSpan> thresholds,
        BriefVisitTreatment treatment) =>
        treatment switch
        {
            CountBriefVisits => intervals,
            ExcludeBriefVisits => WithoutBriefVisits(intervals, stays, thresholds),
        };

    /// <summary>
    /// The cycle a delivered issue's cycle time is taken from, under <paramref name="treatment"/>.
    /// <paramref name="cycle"/> is the issue's cycle (<see cref="Cycles.Measure"/>), null when it never entered
    /// a work-role status, and <paramref name="startsWithBriefVisit"/> says whether that cycle starts with a
    /// brief visit (<see cref="Cycles.StartsWithBriefVisit"/>; owner decisions D90 and D91). Under
    /// <see cref="CountBriefVisits"/> the cycle is measured as it is. Under <see cref="ExcludeBriefVisits"/> a
    /// cycle that starts with a brief visit is left out (owner decision D85): the issue then has no cycle
    /// time, as one that never entered a work status has none, and is delivered all the same. Its point
    /// bucket is not decided here and does not change (owner decision D102). A brief visit
    /// later in the cycle leaves nothing out, because a cycle is measured from its start to the delivery and
    /// not as a sum of stays.
    /// </summary>
    public static Cycle? CycleToMeasure(Cycle? cycle, bool startsWithBriefVisit, BriefVisitTreatment treatment) =>
        treatment switch
        {
            CountBriefVisits => cycle,
            ExcludeBriefVisits => startsWithBriefVisit ? null : cycle,
        };

    private static IReadOnlyList<StatusInterval> WithoutBriefVisits(IReadOnlyList<StatusInterval> intervals, IReadOnlyList<Stay> stays, IReadOnlyDictionary<string, TimeSpan> thresholds)
    {
        var briefVisits = stays
            .Where(stay => BriefVisits.IsBriefVisit(stay, thresholds))
            .ToList();
        if (briefVisits.Count == 0)
        {
            return intervals;
        }

        return [.. intervals.Where(interval => !briefVisits.Any(visit => Covers(visit, interval)))];
    }

    /// <summary>
    /// Whether <paramref name="interval"/> is part of <paramref name="stay"/>: of its status, and inside its
    /// span. The timeline cuts an interval wherever a stay begins or ends, so an interval is inside a stay or
    /// outside it, never across its edge.
    /// </summary>
    private static bool Covers(Stay stay, StatusInterval interval) =>
        interval.StatusId == stay.StatusId && interval.Start >= stay.Start && interval.End <= stay.End;
}
