using System.Globalization;
using Telltale.Core.Configuration;
using Telltale.Core.Storage;

namespace Telltale.Core.Derive;

/// <summary>
/// The minimum group size's numbers the derived data was built under, and how a sentence states them (owner
/// decision D67). The report and the console take them from the record of the rebuild (<see cref="Of"/>), which
/// is where the metric views read them from, and never from the configuration: a sentence then describes the
/// figures beside it. Which of the two checks applies, and whether a number is one assignee or several, is
/// decided here and nowhere else. The same record says whether brief visits were looked for
/// (<see cref="BriefVisitsChecked"/>), and the report takes that from here too, for the same reason.
/// </summary>
public sealed class NumbersInForce(MinimumGroupSize numbers)
{
    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];

    /// <summary>The smallest team (<see cref="MinimumGroupSize.Team"/>).</summary>
    public int Team => numbers.Team;

    /// <summary>The fewest assignees behind a figure (<see cref="MinimumGroupSize.AssigneesBehindAFigure"/>).</summary>
    public int BehindAFigure => numbers.AssigneesBehindAFigure;

    /// <summary>Whether the team check applies: a number of zero turns a check off (owner decision D65).</summary>
    public bool TeamCheck => Team > 0;

    /// <summary>Whether the check on the assignees behind a figure applies.</summary>
    public bool FigureCheck => BehindAFigure > 0;

    /// <summary>
    /// Whether the derived data was built with at least one status listed under <c>briefVisitThresholds</c>
    /// (GLOSSARY: Brief visit), which is when the metric view <c>brief_visits_per_sprint</c> can have rows.
    /// Only <see cref="Of"/> sets it, from the record the view reads: numbers that come from anywhere else
    /// describe no derived data, and so no brief visits.
    /// </summary>
    public bool BriefVisitsChecked { get; private init; }

    /// <summary>Whether these are the default numbers, under which every sentence is what it was before the numbers became a setting.</summary>
    public bool AreTheDefault => numbers == MinimumGroupSize.Default;

    /// <summary>The team number as a sentence writes it: "five".</summary>
    public string TeamInWords => Number(Team);

    /// <summary>The number of assignees behind a figure as a sentence writes it: "three".</summary>
    public string BehindAFigureInWords => Number(BehindAFigure);

    /// <summary>"five assignees"; with <paramref name="distinct"/>, "five distinct assignees".</summary>
    public string TeamAssignees(bool distinct = false) => Assignees(Team, distinct);

    /// <summary>"three assignees", "one assignee"; with <paramref name="distinct"/>, "three distinct assignees".</summary>
    public string AssigneesBehindAFigure(bool distinct = false) => Assignees(BehindAFigure, distinct);

    /// <summary>The verb after <see cref="AssigneesBehindAFigure"/>: "are", or "is" after one assignee.</summary>
    public string BehindAFigureAre => BehindAFigure == 1 ? "is" : "are";

    /// <summary>
    /// The numbers <paramref name="derivedFrom"/> records: the ones the derived data was built under, which the
    /// metric view <c>minimum_group_size</c>, and through it every other view, reads from the same record.
    /// </summary>
    public static NumbersInForce Of(DerivedSource derivedFrom) =>
        new(new MinimumGroupSize { Team = checked((int)derivedFrom.MinimumTeam), AssigneesBehindAFigure = checked((int)derivedFrom.MinimumAssigneesBehindAFigure) })
        {
            BriefVisitsChecked = derivedFrom.BriefVisitsChecked,
        };

    /// <summary>A number as a sentence writes it: in words up to twelve ("five"), in digits above.</summary>
    public static string Number(int number) =>
        number >= 0 && number < Words.Length ? Words[number] : number.ToString(CultureInfo.InvariantCulture);

    private static string Assignees(int number, bool distinct) =>
        $"{Number(number)} {(distinct ? "distinct " : "")}{(number == 1 ? "assignee" : "assignees")}";
}
