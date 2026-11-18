namespace Telltale.Pipeline.Tests.Harness;

/// <summary>
/// The fixed parts of a report that many tests expect around the rows they are about: the heading and head
/// of each table, without and with the baseline columns, the heading that follows the tables, and the text
/// a report carries about brief visits and their treatment. One copy, so a test class states only the rows
/// it is about and a change of wording is one edit. A whole report written out as one literal is not built
/// from these on purpose: it is there to disagree with them.
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

    /// <summary>
    /// How a listed status's footer line goes on, before its line break, when brief visits are left out of
    /// time in status (owner decisions D84 and D86): that those visits are left out of the row, and the number
    /// of issues that are not in the row's count. <paramref name="figure"/> is the number, or <c>suppressed</c>.
    /// </summary>
    public static string LeftOutOfTheRow(string figure) =>
        $"; those visits are left out of the status's time in status row, and issues that had no other business time in the status and so are not in the row's count: {figure}";

    /// <summary>What follows the two tables: the method note's heading.</summary>
    public const string MethodHeading = "\n## Method\n";

    /// <summary>
    /// What a report carries outside its method note and footer when brief visits are left out of a metric
    /// (owner decisions D86 and D101), stated once for the test classes that expect it: the line under the
    /// heading in its three forms, the mark on a time in status row, and the note under the cycle time table.
    /// </summary>
    public static class LeftOut
    {
        /// <summary>The line under the heading when time in status alone leaves brief visits out, with the empty line after it.</summary>
        public const string LineForTimeInStatus =
            "Brief visits are left out of time in status in this report. A row of that table marked \"adjusted\" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit and the number left out of the row.\n\n";

        /// <summary>The line under the heading when cycle time alone leaves brief visits out, with the empty line after it.</summary>
        public const string LineForCycleTime =
            "Brief visits are left out of cycle time in this report. A delivered issue whose cycle starts with a brief visit has no cycle time: the cycle time table's figures are of the cycles that remain, and the footer gives the number of such issues.\n\n";

        /// <summary>The line under the heading when both metrics leave brief visits out, with the empty line after it.</summary>
        public const string LineForBoth =
            "Brief visits are left out of time in status and of cycle time in this report. A row of the time in status table marked \"adjusted\" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit and the number left out of the row. A delivered issue whose cycle starts with a brief visit has no cycle time: the cycle time table's figures are of the cycles that remain, and the footer gives the number of such issues.\n\n";

        /// <summary>The mark after the name of a listed status in the time in status table, when time in status leaves brief visits out.</summary>
        public const string Mark = " (adjusted: see the footer)";

        /// <summary>The note under the cycle time table when cycles that start with a brief visit are left out (D101), with the empty line that parts it from the table.</summary>
        public const string NoteUnderTheCycleTimeTable =
            "\nA delivered issue whose cycle starts with a brief visit has no cycle time and is not in this table (adjusted: see the footer).\n";
    }

    /// <summary>
    /// The sentences of the method note's paragraph on brief visits that have another wording when brief
    /// visits are left out of time in status (owner decisions D93, D98 and D99), each in both wordings, under
    /// the default numbers of the minimum group size: which row the count is measured against, when a count
    /// reads "suppressed", and which listed status has no line. The sentences on what is done with brief
    /// visits are in <see cref="Treatment"/>.
    /// </summary>
    public static class BriefVisitsParagraph
    {
        public const string TheCountIsOfTheRow =
            " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts.";

        public const string TheCountIsOfTheRowBeforeAnythingWasLeftOut =
            " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues that were in the status's time in status row before anything was left out had a brief visit during the time the row counts.";

        public const string ACountReadsSuppressed =
            " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than three distinct assignees are among the issues counted.";

        public const string ACountReadsSuppressedByTheRowBefore =
            " A count reads \"suppressed\" when its row in the time in status table would read \"suppressed\" for its figures with nothing left out, or when fewer than three distinct assignees are among the issues counted.";

        public const string AStatusWithNoRowHasNoLine =
            " A listed status with no row in the time in status table has no line, and a sprint whose team is smaller than five has no line for any listed status.";

        public const string AStatusWithNoRowBeforeHasNoLine =
            " A listed status that had no row in the time in status table before anything was left out has no line; one whose every issue was left out has a line and no row, and a sprint whose team is smaller than five has no line for any listed status.";
    }

    /// <summary>
    /// The sentences of the method note's paragraph on brief visits that say what is done with them
    /// (GLOSSARY: Treatment; owner decisions D83 to D89), stated once for the test classes that expect them.
    /// The paragraph whole, under each combination of the two treatments, is pinned in
    /// <c>BriefVisitTreatmentExplainedTests</c>.
    /// </summary>
    public static class Treatment
    {
        /// <summary>While both metrics count brief visits: the sentence the paragraph had before a treatment could be chosen.</summary>
        public const string NothingIsDone =
            " Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count.";

        /// <summary>When either metric leaves brief visits out: where the treatment is set. The sentence that follows says which is in force for each metric.</summary>
        public const string IsASetting =
            " What is done with brief visits is set for time in status and for cycle time separately (briefVisitTreatment in the configuration file).";

        public const string LeftOutOfTimeInStatusAlone = " In this report they are left out of time in status and counted for cycle time.";

        public const string LeftOutOfCycleTimeAlone = " In this report they are counted for time in status and left out of cycle time.";

        public const string LeftOutOfBoth = " In this report they are left out of time in status and of cycle time.";

        /// <summary>What is left out of time in status, in the words of owner decision D84.</summary>
        public const string TimeInStatusIsLeftOut =
            " The time of every brief visit to a listed status is left out of the issue's time in that status, and an issue left with no business time in the status is not in its row: the row's count, median and total, and the baseline median, are of what remains.";

        /// <summary>What is left out of cycle time, in the words of owner decision D85.</summary>
        public const string CycleTimeIsLeftOut =
            " A delivered issue whose cycle starts with a brief visit has no cycle time. It is still delivered: the delivered count does not change, and the cycle time table's counts, medians and P85s, and the baseline's, are of the cycles that remain.";

        public const string Why =
            " The reason: a stay shorter than the threshold is too short to be the work, and its usual cause is an issue moved through several statuses at once after the work was done, so its recorded length says when the board was updated and not how long the work took.";

        /// <summary>Follows <see cref="Why"/> when cycle time leaves brief visits out.</summary>
        public const string WhyForACycle = " A cycle that starts with such a stay starts when the board was updated, not when the work began.";

        public const string WhatLeavingOutDoesNotDo =
            " Leaving out is not a correction: the time the work took is still recorded under the earlier status, the adjusted figures describe the issues that were moved on time, which may not be typical, and no length was estimated or replaced."
            + " No other figure was changed because of a brief visit.";

        /// <summary>The second number of a listed status's footer line, there when time in status leaves brief visits out. What it reads when withheld follows it, by the numbers in force.</summary>
        public const string TheSecondNumber =
            " A listed status's line goes on to a second number: how many of the issues with a brief visit had no other business time in the status and so are not in the row's count.";

        /// <summary>Under the default numbers.</summary>
        public const string TheSecondNumberReadsSuppressed =
            " It reads \"suppressed\" when fewer than three distinct assignees are among the issues left out, and so also when nobody was left out.";

        /// <summary>What the footer's cycle time line counts. It is there when a listed status has the work role, under either treatment.</summary>
        public const string TheCycleTimeLine =
            " The footer's last line gives how many of the sprint's delivered issues have a cycle that starts with a brief visit: the stay that begins where the cycle starts is one.";

        public const string TheyAreInTheFigures = " Those issues are in the cycle time figures.";

        public const string TheyHaveNoCycleTime = " Those issues have no cycle time.";

        /// <summary>Under the default numbers.</summary>
        public const string TheCycleNumberReadsSuppressed =
            " Its number reads \"suppressed\" when the sprint's delivered count does, as for a sprint whose team is smaller than five, when fewer than three distinct assignees are among the issues it counts, and when it is nothing.";

        /// <summary>Ends the sentences on the cycle time line, under either treatment (owner decision D109): the date is the day the line was decided (D87), not that of the rule of 2026-11-12 that the next sentence dates.</summary>
        public const string TheCycleTimeLineWasAdded = " This line was added on 2026-11-16, after results had been seen.";

        /// <summary>The paragraph's last sentence when a metric leaves brief visits out (owner decision D89). <paramref name="metrics"/> is "time in status is", "cycle time is" or "time in status and cycle time are".</summary>
        public static string WhenLeavingOutWasAdded(string metrics) =>
            $" Leaving brief visits out was added on 2026-11-16, after results had been seen, and it changes how {metrics} measured.";
    }
}
