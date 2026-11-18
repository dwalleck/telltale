using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;
using T = Telltale.Pipeline.Tests.Harness.ReportText.Treatment;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// What the method note says about the treatment of brief visits (GLOSSARY: Treatment; owner decisions D83 to
/// D89): its last paragraph, whole, under each of the four combinations of the two parts of
/// <c>briefVisitTreatment</c>. While both metrics count brief visits the paragraph is what it was before a
/// treatment could be chosen, plus the sentences on the footer's cycle time line. When either leaves them out,
/// it states the treatment of each metric, what was left out, why, what leaving out does not do, and that
/// it was added on 2026-11-16, after results had been seen. Sprint 41 runs from Monday 5 January 2026, 09:00,
/// to Monday the 19th, 10:00. Its team is five people, each with an issue that stays in New.
/// </summary>
public class BriefVisitTreatmentExplainedTests
{
    private const string LastParagraphBefore = "- Work that has since been removed from Jira stays in the figures until the next backfill.\n";

    private const string WhatABriefVisitIs =
        "- A brief visit is one unbroken stay in a status that ended and was shorter than the status's threshold."
        + " It is measured in clock time over the issue's whole history: weekends and holidays count, and the stay is not clipped to the sprint.";

    // Which row the count is measured against: the row as it is, and the row before anything was left out.
    private const string TheCountIsOfTheRow = ReportText.BriefVisitsParagraph.TheCountIsOfTheRow;

    private const string TheCountIsOfTheRowBeforeAnythingWasLeftOut = ReportText.BriefVisitsParagraph.TheCountIsOfTheRowBeforeAnythingWasLeftOut;

    private const string AShortMedian = " A short median in the time in status table does not show how many issues had a brief visit.";

    private const string ACountReadsSuppressed = ReportText.BriefVisitsParagraph.ACountReadsSuppressed;

    private const string ACountReadsSuppressedByTheRowBefore = ReportText.BriefVisitsParagraph.ACountReadsSuppressedByTheRowBefore;

    private const string AZeroAndAWithheldCountReadAlike =
        " A count of nothing has nobody behind it, so a zero and a withheld count both read \"suppressed\".";

    private const string AStatusWithNoRowHasNoLine = ReportText.BriefVisitsParagraph.AStatusWithNoRowHasNoLine;

    private const string AStatusWithNoRowBeforeHasNoLine = ReportText.BriefVisitsParagraph.AStatusWithNoRowBeforeHasNoLine;

    private const string WhenTheRuleWasAdded =
        " This rule was added on 2026-11-12, after the first run on real data: the thresholds were chosen with knowledge of results, and the counts describe data quality and are not a success criterion.";

    private const string Footer =
        "\n---\n\n"
        + "- As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)\n"
        + "- Issues with changelog anomalies, in the whole store: suppressed\n"
        + "- Issues delivered between sprints, in the whole store: suppressed\n";

    [Test]
    public async Task While_both_metrics_count_brief_visits_the_paragraph_is_what_it_was_plus_the_sentences_on_the_footers_cycle_time_line()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "count", ["cycleTime"] = "count" };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs + TheCountIsOfTheRow + T.NothingIsDone + AShortMedian
            + ACountReadsSuppressed + AZeroAndAWithheldCountReadAlike
            + AStatusWithNoRowHasNoLine
            + T.TheCycleTimeLine + T.TheyAreInTheFigures + T.TheCycleNumberReadsSuppressed + T.TheCycleTimeLineWasAdded
            + WhenTheRuleWasAdded + "\n"
            + Footer
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes\n"
            + ReportText.CyclesThatStartWithBriefVisit("4"));
    }

    [Test]
    public async Task With_a_status_listed_and_both_metrics_counting_the_whole_report_is_what_it_was_plus_the_sentences_on_the_cycle_time_line()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The sentences are in the report once, and the report without them is the report as it was before
        // they were written, to the byte.
        var report = await pipeline.ReadReportAsync(41);
        var sentences = T.TheCycleTimeLine + T.TheyAreInTheFigures + T.TheCycleNumberReadsSuppressed + T.TheCycleTimeLineWasAdded;
        await Assert.That(report.Replace(sentences, "")).IsEqualTo(TheReportBeforeTheSentences.ReplaceLineEndings("\n"));
        await Assert.That(report.Split(sentences).Length).IsEqualTo(2);
    }

    /// <summary>
    /// The whole report for sprint 41 of <see cref="AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive"/> with
    /// Development listed and no treatment chosen, as it was before the method note had sentences on the
    /// footer's cycle time line.
    /// </summary>
    private const string TheReportBeforeTheSentences =
        """
        # Fake Sprint 41 (2026-01-05 to 2026-01-19)

        Delivered issues: 5

        ## Cycle time by point bucket

        | Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |
        | --- | ---: | ---: | ---: | ---: | ---: |
        | unestimated | 5 | 2.0 | 2.0 | 2.0 | 2.0 |

        ## Time in status

        | Status | Issues | Median (business days) | Total (business days) |
        | --- | ---: | ---: | ---: |
        | Fake New | 10 | 6.0 | 59.4 |
        | Fake Development | 5 | 0.0 | 2.0 |
        | Fake Review | 4 | 2.0 | 8.0 |

        ## Method

        - Durations are in business days: days that are neither a weekend nor a company holiday, each counted in full (24 hours) in the team's time zone (America/Chicago). Calendar days are shown alongside: elapsed time divided by 24 hours.
        - Cycle time runs from an issue's first entry into a work status to its last entry into a done status, for delivered issues only. It belongs to the sprint in which the issue was delivered.
        - The median is the middle value, or the mean of the two middle values. The P85 is the smallest value with at least 85% of the values at or below it.
        - Time in status includes delivered and unfinished issues in statuses before done, clipped to the sprint window and each issue's membership intervals. An issue counts toward a status only if it spent more than zero business days there. Total business days sum that time across all contributing issues, including repeated visits; this is accumulated issue time, not effort or the sprint's elapsed duration. Totals and medians use the same privacy rules.
        - A figure is shown only where it cannot show one person's work. The team of a sprint is the distinct assignees of every issue that was a member of the sprint or was delivered in it. When the team is smaller than five, the delivered count and both tables read "suppressed", with no rows. With a team of five or more, a row with fewer than three distinct assignees behind it keeps its issue count and reads "suppressed" for its figures, and the delivered count reads "suppressed" when fewer than three distinct assignees are among the delivered issues, and the cycle time table then reads "suppressed" as well, with no rows, because the number of its rows and their counts (the delivered issues that have a cycle time) would each give a lower bound of the withheld count. The time in status table of such a sprint keeps its counts: they count every issue that spent time in a status, delivered or not, so the count of a late status can be close to the withheld delivered count. The footer's two counts of the whole store read "suppressed" when the issues in the whole store have fewer than five distinct assignees, or fewer than three are among the issues counted.
        - An assignee is a Jira account as of the last sync, so a bot or a person's second account counts as a person. An unassigned issue counts toward nobody.
        - An interval that is still open (an issue still in a status, a sprint not yet completed) ends at the as-of time below.
        - Work that has since been removed from Jira stays in the figures until the next backfill.
        - A brief visit is one unbroken stay in a status that ended and was shorter than the status's threshold. It is measured in clock time over the issue's whole history: weekends and holidays count, and the stay is not clipped to the sprint. For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts. Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count. A short median in the time in status table does not show how many issues had a brief visit. A count reads "suppressed" when its row in the time in status table reads "suppressed" for its figures, or when fewer than three distinct assignees are among the issues counted. A count of nothing has nobody behind it, so a zero and a withheld count both read "suppressed". A listed status with no row in the time in status table has no line, and a sprint whose team is smaller than five has no line for any listed status. This rule was added on 2026-11-12, after the first run on real data: the thresholds were chosen with knowledge of results, and the counts describe data quality and are not a success criterion.

        ---

        - As-of time: 2026-02-03 12:00 -06:00 (America/Chicago)
        - Issues with changelog anomalies, in the whole store: suppressed
        - Issues delivered between sprints, in the whole store: suppressed
        - Fake Development: 4 issues had a visit shorter than 5 minutes
        - Delivered issues whose cycle starts with a brief visit, which are in the cycle time figures: 4

        """;

    [Test]
    public async Task With_no_work_status_listed_the_footer_has_no_cycle_time_line_and_the_paragraph_no_sentences_on_it()
    {
        // New is a not started status: no cycle starts in it. The team's five issues stay in New.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs + TheCountIsOfTheRow + T.NothingIsDone + AShortMedian
            + ACountReadsSuppressed + AZeroAndAWithheldCountReadAlike
            + AStatusWithNoRowHasNoLine
            + WhenTheRuleWasAdded + "\n"
            + Footer
            + "- Fake New: issues with a visit shorter than 5 minutes: suppressed\n");
    }

    [Test]
    public async Task With_brief_visits_left_out_of_time_in_status_alone_the_paragraph_says_what_was_done_why_and_what_it_does_not_do()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs + TheCountIsOfTheRowBeforeAnythingWasLeftOut
            + T.IsASetting + T.LeftOutOfTimeInStatusAlone
            + T.TimeInStatusIsLeftOut
            + T.Why
            + T.WhatLeavingOutDoesNotDo + AShortMedian
            + ACountReadsSuppressedByTheRowBefore + AZeroAndAWithheldCountReadAlike
            + T.TheSecondNumber + T.TheSecondNumberReadsSuppressed
            + AStatusWithNoRowBeforeHasNoLine
            + T.TheCycleTimeLine + T.TheyAreInTheFigures + T.TheCycleNumberReadsSuppressed + T.TheCycleTimeLineWasAdded
            + WhenTheRuleWasAdded
            + T.WhenLeavingOutWasAdded("time in status is") + "\n"
            + Footer
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("4") + "\n"
            + ReportText.CyclesThatStartWithBriefVisit("4"));
    }

    [Test]
    public async Task With_brief_visits_left_out_of_cycle_time_alone_the_paragraph_says_what_was_done_why_and_what_it_does_not_do()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // Time in status counts brief visits, so its sentences are what they are with no treatment chosen,
        // and the listed status's line has no second number.
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs + TheCountIsOfTheRow
            + T.IsASetting + T.LeftOutOfCycleTimeAlone
            + T.CycleTimeIsLeftOut
            + T.Why + T.WhyForACycle
            + T.WhatLeavingOutDoesNotDo + AShortMedian
            + ACountReadsSuppressed + AZeroAndAWithheldCountReadAlike
            + AStatusWithNoRowHasNoLine
            + T.TheCycleTimeLine + T.TheyHaveNoCycleTime + T.TheCycleNumberReadsSuppressed + T.TheCycleTimeLineWasAdded
            + WhenTheRuleWasAdded
            + T.WhenLeavingOutWasAdded("cycle time is") + "\n"
            + Footer
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("4"));
    }

    [Test]
    public async Task With_brief_visits_left_out_of_both_metrics_the_paragraph_says_both_things_once_each()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfBoth(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            LastParagraphBefore
            + WhatABriefVisitIs + TheCountIsOfTheRowBeforeAnythingWasLeftOut
            + T.IsASetting + T.LeftOutOfBoth
            + T.TimeInStatusIsLeftOut + T.CycleTimeIsLeftOut
            + T.Why + T.WhyForACycle
            + T.WhatLeavingOutDoesNotDo + AShortMedian
            + ACountReadsSuppressedByTheRowBefore + AZeroAndAWithheldCountReadAlike
            + T.TheSecondNumber + T.TheSecondNumberReadsSuppressed
            + AStatusWithNoRowBeforeHasNoLine
            + T.TheCycleTimeLine + T.TheyHaveNoCycleTime + T.TheCycleNumberReadsSuppressed + T.TheCycleTimeLineWasAdded
            + WhenTheRuleWasAdded
            + T.WhenLeavingOutWasAdded("time in status and cycle time are") + "\n"
            + Footer
            + "- Fake Development: 4 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("4") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("4"));
    }

    [Test]
    [Arguments(5, 3,
        " It reads \"suppressed\" when fewer than three distinct assignees are among the issues left out, and so also when nobody was left out.",
        " Its number reads \"suppressed\" when the sprint's delivered count does, as for a sprint whose team is smaller than five, when fewer than three distinct assignees are among the issues it counts, and when it is nothing.",
        "suppressed")]
    [Arguments(0, 2,
        " It reads \"suppressed\" when fewer than two distinct assignees are among the issues left out, and so also when nobody was left out.",
        " Its number reads \"suppressed\" when the sprint's delivered count does, when fewer than two distinct assignees are among the issues it counts, and when it is nothing.",
        "suppressed")]
    [Arguments(5, 0,
        " It is not withheld either, and reads 0 when nobody was left out.",
        " Its number reads \"suppressed\" when the sprint's delivered count does, as for a sprint whose team is smaller than five, and a number of nothing reads 0.",
        "0")]
    [Arguments(0, 0,
        " It is not withheld either, and reads 0 when nobody was left out.",
        " Its number is not withheld, and a number of nothing reads 0.",
        "0")]
    public async Task What_the_paragraph_says_of_a_number_of_nothing_is_what_the_footer_reads_by_the_numbers_in_force(int team, int assigneesBehindAFigure, string theSecondNumberReads, string theCycleNumberReads, string nothingReads)
    {
        // Nobody had a brief visit: three people's issues did two days in Development and were done. So
        // nothing was left out of the Development row, and no cycle starts with a brief visit.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 11)))
        {
            Scenarios.Delivered(pipeline.Jira, 41, $"FAKE-{number}", points: 3, person, days: 2);
        }

        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfBoth(pipeline);
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(T.TheSecondNumber + theSecondNumberReads + " A listed status that had no row");
        await Assert.That(report).Contains(T.TheCycleTimeLine + T.TheyHaveNoCycleTime + theCycleNumberReads + T.TheCycleTimeLineWasAdded + WhenTheRuleWasAdded);
        await Assert.That(report).EndsWith(ReportText.LeftOutOfTheRow(nothingReads) + "\n" + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime(nothingReads));
    }

    [Test]
    [Arguments(5, 3)]
    [Arguments(5, 0)]
    public async Task A_sprint_short_of_a_team_has_the_cycle_time_line_reading_suppressed_as_the_paragraph_says(int team, int assigneesBehindAFigure)
    {
        // Four people, each with a delivered issue whose cycle starts with a brief visit, and no fifth.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        ListDevelopment(pipeline);
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).Contains(" Its number reads \"suppressed\" when the sprint's delivered count does, as for a sprint whose team is smaller than five, ");
        await Assert.That(report).EndsWith(Footer + ReportText.CyclesThatStartWithBriefVisit("suppressed"));
    }

    private static void ListDevelopment(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

    private static void LeaveBriefVisitsOutOfBoth(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "exclude" };

    /// <summary>
    /// Sprint 41 with its team of five idle in New, four issues <see cref="Scenarios.CaughtUp"/> of four people, and a
    /// fifth person's issue that did two days in Development: a Development row of five issues, four of them
    /// with a brief visit and no other time there, and five delivered issues, four of them with a cycle that
    /// starts with a brief visit.
    /// </summary>
    private static void AddSprint41WithFourCaughtUpIssuesInADevelopmentRowOfFive(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(jira, $"FAKE-{number}", person);
        }

        jira.AddIssue("FAKE-5", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
    }
}
