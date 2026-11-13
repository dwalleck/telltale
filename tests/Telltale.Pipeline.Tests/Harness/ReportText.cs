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

    /// <summary>What follows the two tables: the method note's heading.</summary>
    public const string MethodHeading = "\n## Method\n";
}
