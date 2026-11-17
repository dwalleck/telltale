namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The fixed parts of a report that many tests expect around the rows they are about: the heading and head
/// of each table, without and with the baseline columns, and the heading that follows the tables. One copy,
/// so a test class states only the rows it is about.
/// </summary>
public static class ReportText
{
    public const string CycleTimeHeader =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n";

    public const string CycleTimeHeaderWithBaseline =
        "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) | Baseline median (business days) | Baseline P85 (business days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |\n";

    public const string TimeInStatusHeader =
        "\n## Time in status\n\n"
        + "| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n";

    public const string TimeInStatusHeaderWithBaseline =
        "\n## Time in status\n\n"
        + "| Status | Issues | Median (business days) | Total (business days) | Baseline median (business days) |\n| --- | ---: | ---: | ---: | ---: |\n";

    /// <summary>
    /// The footer's last line when a work status is listed for brief visits, directly after the brief visit
    /// lines (owner decisions D87 and D100): the number of delivered issues whose cycle starts with a brief
    /// visit. <paramref name="figure"/> is the number, or <c>suppressed</c>.
    /// </summary>
    public static string CyclesThatStartWithBriefVisit(string figure) =>
        $"- Delivered issues whose cycle starts with a brief visit, which are in the cycle time figures: {figure}\n";

    /// <summary>
    /// The same line when the cycles that start with a brief visit are left out of cycle time (owner decision
    /// D85): the number is the same (D87), and the line says the issues have no cycle time.
    /// </summary>
    public static string CyclesThatStartWithBriefVisitAndHaveNoCycleTime(string figure) =>
        $"- Delivered issues whose cycle starts with a brief visit, which have no cycle time: {figure}\n";

    /// <summary>What follows the two tables: the method note's heading.</summary>
    public const string MethodHeading = "\n## Method\n";
}
