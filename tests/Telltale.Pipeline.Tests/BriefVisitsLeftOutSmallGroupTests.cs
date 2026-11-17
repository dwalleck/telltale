using System.Globalization;
using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Leaving brief visits out and small groups (GLOSSARY: Treatment, Minimum group size; owner decisions D93 to
/// D95, D97 to D99 and D102): the number of issues left out of a status's row, and that no adjusted figure and
/// no count of issues left out says more about a small group than the rest of the report does, in the report
/// and through <c>query</c>.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00, and sprint 42 for the two
/// weeks after it. Unless a test says otherwise each has a team of five people, each with an issue that stays
/// in New; the tests add the issues that pass through the work statuses. Development is listed with a
/// threshold of five minutes, and where a test lists Review, with two.
/// </summary>
public class BriefVisitsLeftOutSmallGroupTests
{
    /// <summary>The brief visit rows with the left-out number and its flag, as <c>query</c> prints them.</summary>
    private const string LeftOutSql =
        "SELECT sprint_id, status_name, brief_visit_issue_count, suppressed, left_out_issue_count, left_out_suppressed FROM brief_visits_per_sprint ORDER BY sprint_id, board_position;";

    private const string LeftOutHead =
        "| sprint_id | status_name | brief_visit_issue_count | suppressed | left_out_issue_count | left_out_suppressed |\n| --- | --- | --- | --- | --- | --- |\n";

    /// <summary>The Development row of each sprint's time in status table. Days are rounded to a thousandth.</summary>
    private const string DevelopmentRowSql =
        "SELECT sprint_id, status_name, issue_count, ROUND(median_business_days, 3) AS median_business_days, ROUND(total_business_days, 3) AS total_business_days, suppressed FROM time_in_status_per_sprint WHERE status_name = 'Fake Development' ORDER BY sprint_id;";

    private const string DevelopmentRowHead =
        "| sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |\n| --- | --- | --- | --- | --- | --- |\n";

    private const string BucketsSql =
        "SELECT sprint_id, point_bucket, issue_count, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days, suppressed FROM cycle_time_per_sprint_and_bucket ORDER BY sprint_id, point_bucket;";

    private const string BucketsHead =
        "| sprint_id | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |\n| --- | --- | --- | --- | --- | --- | --- | --- |\n";

    private const string CycleStartsSql = "SELECT sprint_id, issue_count, suppressed FROM cycle_starts_with_brief_visit_per_sprint ORDER BY sprint_id;";

    private const string CycleStartsHead = "| sprint_id | issue_count | suppressed |\n| --- | --- | --- |\n";

    private const string Mark = " (adjusted: see the footer)";

    private const string DevelopmentCountWithheld = "- Fake Development: issues with a visit shorter than 5 minutes: suppressed";

    [Test]
    public async Task The_view_and_the_footer_give_the_number_of_issues_left_out_of_a_row_and_under_count_the_view_gives_none()
    {
        // Four people's issues went through Development in twenty seconds, and one of them came back for two
        // days. Three more people's issues did two days there.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            var issue = pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 7, 10), Workflow.Development)
                .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review);
            if (number == 4)
            {
                issue.Moved(Local(2026, 1, 8, 9), Workflow.Development).Moved(Local(2026, 1, 12, 9), Workflow.Review);
            }
        }

        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await QueryAsync(pipeline, LeftOutSql);
        var countedReport = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: false);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // Counted: nothing is left out, so there is no number, and nothing is withheld. The line is what it was.
        await Assert.That(counted).IsEqualTo(LeftOutHead + "| 41 | Fake Development | 4 | 0 | NULL | 0 |\n");
        await Assert.That(countedReport).Contains("\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n");

        // Left out: four issues had a brief visit, and three of them had no other time in Development. The
        // row is the four that remain.
        await Assert.That(await QueryAsync(pipeline, LeftOutSql)).IsEqualTo(LeftOutHead + "| 41 | Fake Development | 4 | 0 | 3 | 0 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development (adjusted: see the footer) | 4 | 2.0 | 8.0 |\n");
        await Assert.That(report).Contains("\n- Fake Development: 4 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("3") + "\n");
    }

    [Test]
    [Arguments(4, "- Fake Development: 4 issues had a visit shorter than 5 minutes", "4", "4 | 0 | 4 | 0")]
    [Arguments(2, DevelopmentCountWithheld, "suppressed", "NULL | 1 | NULL | 1")]
    public async Task A_status_whose_every_issue_was_left_out_has_no_row_and_a_footer_line_that_still_explains_it(
        int people, string line, string leftOut, string countsAndFlags)
    {
        // The only issues in Development were caught up through it in twenty seconds: four people's, or two
        // people's.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        foreach (var (person, number) in People.Team.Take(people).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        ListDevelopment(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: false);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The line is there with no row above it, which shows that the number left out and the brief visit
        // count are each at least one, also when they are withheld: the owner accepted that (D98).
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).DoesNotContain("| Fake Development");
        await Assert.That(report).Contains("\n" + line + ReportText.LeftOutOfTheRow(leftOut) + "\n");
        await Assert.That(await QueryAsync(pipeline, DevelopmentRowSql)).IsEqualTo(DevelopmentRowHead);
        await Assert.That(await QueryAsync(pipeline, LeftOutSql)).IsEqualTo(LeftOutHead + $"| 41 | Fake Development | {countsAndFlags} |\n");
    }

    [Test]
    public async Task A_row_and_a_point_bucket_that_had_three_assignees_and_have_two_after_leaving_out_have_their_figures_withheld()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddThreePeoplesIssuesOfWhichOneWasCaughtUp(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await QueryAsync(pipeline, DevelopmentRowSql, BucketsSql);
        var countedReport = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // Counted: three people are behind the row and behind the bucket, and both are shown.
        await Assert.That(counted).IsEqualTo(
            DevelopmentRowHead + "| 41 | Fake Development | 3 | 2 | 4 | 0 |\n"
            + BucketsHead + "| 41 | 3 | 3 | 2 | 2 | 2 | 2 | 0 |\n");
        await Assert.That(countedReport).Contains("\n| Fake Development | 3 | 2.0 | 4.0 |\n");
        await Assert.That(countedReport).Contains(ReportText.CycleTimeHeader + "| 3 | 3 | 2.0 | 2.0 | 2.0 | 2.0 |\n");

        // Left out: two people are behind what remains of each, so each keeps its count and withholds its
        // figures. The bucket is counted over the issues that still have a cycle time.
        await Assert.That(await QueryAsync(pipeline, DevelopmentRowSql, BucketsSql)).IsEqualTo(
            DevelopmentRowHead + "| 41 | Fake Development | 2 | NULL | NULL | 1 |\n"
            + BucketsHead + "| 41 | 3 | 2 | NULL | NULL | NULL | NULL | 1 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 3\n");
        await Assert.That(report).Contains("\n| Fake Development (adjusted: see the footer) | 2 | suppressed | suppressed |\n");
        await Assert.That(report).Contains(ReportText.CycleTimeHeader + "| 3 | 2 | suppressed | suppressed | suppressed | suppressed |\n");

        // One person's issue had the brief visit and was left out: the two counts on the line and the number
        // of cycles are each withheld.
        await Assert.That(report).EndsWith(
            "\n" + DevelopmentCountWithheld + ReportText.LeftOutOfTheRow("suppressed") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("suppressed"));
        await Assert.That(await QueryAsync(pipeline, LeftOutSql, CycleStartsSql)).IsEqualTo(
            LeftOutHead + "| 41 | Fake Development | NULL | 1 | NULL | 1 |\n"
            + CycleStartsHead + "| 41 | NULL | 1 |\n");

        // What the owner accepted (D94): the delivered count less the bucket counts is the number of delivered
        // issues with no cycle time. Every delivered issue here entered a work status, so it is the withheld
        // number of cycles that start with a brief visit.
        await Assert.That(await QueryAsync(
            pipeline,
            "SELECT d.sprint_id, d.delivered_count - (SELECT SUM(b.issue_count) FROM cycle_time_per_sprint_and_bucket b WHERE b.sprint_id = d.sprint_id) AS delivered_with_no_cycle_time FROM delivered_per_sprint d;")).IsEqualTo(
            "| sprint_id | delivered_with_no_cycle_time |\n| --- | --- |\n| 41 | 1 |\n");
    }

    [Test]
    public async Task A_baseline_figure_is_withheld_when_one_baseline_sprints_row_or_bucket_is_withheld_for_what_remains()
    {
        // Sprints 41 and 42 are the baseline. Sprint 41 is the one whose Development row and 3-point bucket
        // have two people behind them once the caught-up issue is left out. In sprint 42 three people's
        // 3-point issues did three days in Development.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        AddThreePeoplesIssuesOfWhichOneWasCaughtUp(pipeline.Jira);
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 21)))
        {
            Scenarios.Delivered(pipeline.Jira, 42, $"FAKE-{number}", points: 3, person, days: 3);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        ListDevelopment(pipeline);
        const string BaselineSql =
            "SELECT status_name AS baseline_row, ROUND(median_business_days, 3) AS median_business_days, suppressed FROM baseline_time_in_status WHERE status_name = 'Fake Development' "
            + "UNION ALL SELECT 'bucket ' || CAST(point_bucket AS INTEGER), ROUND(median_business_days, 3), suppressed FROM baseline_cycle_time_per_bucket;";
        const string BaselineHead = "| baseline_row | median_business_days | suppressed |\n| --- | --- | --- |\n";

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        var counted = await QueryAsync(pipeline, BaselineSql);
        var countedReport = await pipeline.ReadReportAsync(42);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        // Counted: six issues are pooled in the row and in the bucket, and both medians are shown.
        await Assert.That(counted).IsEqualTo(BaselineHead + "| Fake Development | 2.5 | 0 |\n| bucket 3 | 2.5 | 0 |\n");
        await Assert.That(countedReport).Contains("\n| Fake Development | 3 | 3.0 | 9.0 | 2.5 |\n");
        await Assert.That(countedReport).Contains("\n| 3 | 3 | 3.0 | 3.0 | 3.0 | 3.0 | 2.5 | 3.0 |\n");

        // Left out: sprint 41 withholds its row and its bucket, so the pooled figures are withheld, though
        // five issues of five people would be pooled.
        await Assert.That(await QueryAsync(pipeline, BaselineSql)).IsEqualTo(BaselineHead + "| Fake Development | NULL | 1 |\n| bucket 3 | NULL | 1 |\n");
        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).Contains("\n| Fake Development (adjusted: see the footer) | 3 | 3.0 | 9.0 | suppressed |\n");
        await Assert.That(report).Contains("\n| 3 | 3 | 3.0 | 3.0 | 3.0 | 3.0 | suppressed | suppressed |\n");
    }

    [Test]
    [Arguments("", "suppressed", "NULL | 1")]
    [Arguments("1", "suppressed", "NULL | 1")]
    [Arguments("1,3", "suppressed", "NULL | 1")]
    [Arguments("1,2,3,4", "suppressed", "NULL | 1")]
    [Arguments("8", "suppressed", "NULL | 1")]
    [Arguments("1,3,8", "suppressed", "NULL | 1")]
    [Arguments("1,3,5", "3", "3 | 0")]
    [Arguments("1,3,5,8", "4", "4 | 0")]
    public async Task The_number_left_out_is_shown_only_with_three_distinct_assignees_behind_the_issues_left_out_and_nothing_left_out_reads_as_a_withheld_number_does(
        string leftOut, string figure, string numberAndFlag)
    {
        // Nobody; one person; two people; four issues of two people; an unassigned issue alone; two people
        // and an unassigned issue: each is withheld, and each reads as the others do. Three people: shown, and
        // the unassigned issue is counted among the issues though it adds nobody.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, Numbers(leftOut));

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The brief visit count is of all eight, whoever was left out, and the row is eight issues either way.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development (adjusted: see the footer) | 8 | 2.0 | 16.0 |\n");
        await Assert.That(report).Contains("\n- Fake Development: 8 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow(figure) + "\n");
        await Assert.That(await QueryAsync(pipeline, LeftOutSql)).StartsWith(LeftOutHead + $"| 41 | Fake Development | 8 | 0 | {numberAndFlag} |\n");
    }

    [Test]
    public async Task No_query_over_the_metric_views_tells_nothing_left_out_from_a_withheld_number_or_one_withheld_number_from_another()
    {
        // Five stores whose number left out of Development in sprint 41 is withheld: nobody was left out; one
        // issue of one person; two issues of two people; four issues of two people; two people's and an
        // unassigned one. Around it are figures a reader could try to subtract from: the row's issue count and
        // the brief visit count, both shown and the same in every store, and shown numbers left out of Review
        // in the same sprint and of Development in sprint 42. Then a store whose number is shown: three issues
        // of three people.
        // What this test claims is that the withheld number cannot be recovered, or told from nothing, in
        // these stores. It does not claim that for every store: the tests below pin what the owner accepted.
        string[] withheld = ["", "1", "1,3", "1,2,3,4", "1,3,8"];
        var read = new List<(string Reports, string Views, string Attempts)>();
        foreach (var leftOut in withheld.Append("1,3,5"))
        {
            await using var pipeline = new PipelineHarness();
            AddStore(pipeline, Numbers(leftOut));
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);

            read.Add((
                await pipeline.ReadReportAsync(41) + await pipeline.ReadReportAsync(42),
                await QueryEveryMetricViewAsync(pipeline),
                await QueryAsync(
                    pipeline,
                    LeftOutSql,
                    "SELECT SUM(left_out_issue_count) AS shown, COUNT(left_out_issue_count) AS numbers, SUM(left_out_suppressed) AS withheld FROM brief_visits_per_sprint;",
                    "SELECT b.sprint_id, b.status_name, t.issue_count, t.suppressed AS row_suppressed, b.brief_visit_issue_count - t.issue_count AS more_counted_than_remain, t.issue_count + b.left_out_issue_count AS row_before "
                    + "FROM brief_visits_per_sprint b LEFT JOIN time_in_status_per_sprint t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id ORDER BY b.sprint_id, b.board_position;")));
        }

        // What the first store gives: the withheld number is null beside the shown ones, and no sum or
        // difference holds it. Review in sprint 41 and Development in sprint 42 have a line and no row.
        await Assert.That(read[0].Attempts).IsEqualTo(
            LeftOutHead
            + """
            | 41 | Fake Development | 8 | 0 | NULL | 1 |
            | 41 | Fake Review | 3 | 0 | 3 | 0 |
            | 42 | Fake Development | 3 | 0 | 3 | 0 |
            | 42 | Fake Review | NULL | 1 | NULL | 1 |
            | shown | numbers | withheld |
            | --- | --- | --- |
            | 6 | 2 | 2 |
            | sprint_id | status_name | issue_count | row_suppressed | more_counted_than_remain | row_before |
            | --- | --- | --- | --- | --- | --- |
            | 41 | Fake Development | 8 | 0 | 0 | NULL |
            | 41 | Fake Review | NULL | NULL | NULL | NULL |
            | 42 | Fake Development | NULL | NULL | NULL | NULL |
            | 42 | Fake Review | 3 | 0 | NULL | NULL |

            """.ReplaceLineEndings("\n"));
        var developmentLine = "\n- Fake Development: 8 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("suppressed") + "\n";
        await Assert.That(read[0].Reports).Contains(
            developmentLine + "- Fake Review: 3 issues had a visit shorter than 2 minutes" + ReportText.LeftOutOfTheRow("3") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("3"));

        // The marks are there with nothing left out, so a mark cannot tell nothing from a withheld number.
        await Assert.That(read[0].Reports).Contains("\n| Fake Development" + Mark + " | 8 | 2.0 | 16.0 |\n");
        await Assert.That(read[0].Reports).Contains("\n| Fake Review" + Mark + " | 3 | 2.0 | 6.0 |\n");

        // The other four give the same, to the byte: every row of every metric view, and both reports. A
        // query reads nothing but those views, so nothing can tell these stores apart.
        foreach (var other in read[1..withheld.Length])
        {
            await Assert.That(other.Views).IsEqualTo(read[0].Views);
            await Assert.That(other.Attempts).IsEqualTo(read[0].Attempts);
            await Assert.That(other.Reports).IsEqualTo(read[0].Reports);
        }

        // The comparison can fail: with three people behind the number, that one row and line differ and
        // nothing else.
        var rowWithheld = $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 8 | 0 | NULL | 1 |\n";
        await Assert.That(read[0].Views).Contains(rowWithheld);
        await Assert.That(read[^1].Views).IsEqualTo(
            read[0].Views.Replace(rowWithheld, $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 8 | 0 | 3 | 0 |\n", StringComparison.Ordinal));
        await Assert.That(read[^1].Reports).IsEqualTo(
            read[0].Reports.Replace(developmentLine, developmentLine.Replace(ReportText.LeftOutOfTheRow("suppressed"), ReportText.LeftOutOfTheRow("3"), StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Test]
    [Arguments(0, 3, 0)]
    [Arguments(2, 5, 2)]
    public async Task The_number_of_cycles_less_the_shown_brief_visit_counts_is_a_withheld_brief_visit_count_which_bounds_the_number_left_out(
        int caughtUpThroughDevelopment, int cycles, int lessTheShownCounts)
    {
        // What the owner accepted (D97, and D98 with it), as it shows with brief visits left out: where the
        // subtraction gives a withheld brief visit count as nothing, the number left out is nothing too. Three people's issues did two
        // days in Development; three more people's went from New through Review in twenty seconds. Nobody's
        // issue, or two people's, was caught up through Development. The Development line withholds both its
        // counts either way, and the number left out of a status is never more than its brief visit count.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        AddThreeIssuesThatPassedThroughReview(pipeline.Jira);
        foreach (var (person, number) in People.Team.Take(caughtUpThroughDevelopment).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        ListDevelopmentAndReview(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "\n" + DevelopmentCountWithheld + ReportText.LeftOutOfTheRow("suppressed") + "\n"
            + "- Fake Review: 3 issues had a visit shorter than 2 minutes" + ReportText.LeftOutOfTheRow("3") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime(cycles.ToString(CultureInfo.InvariantCulture)));
        await Assert.That(await QueryAsync(
            pipeline,
            LeftOutSql,
            "SELECT c.sprint_id, c.issue_count, c.issue_count - (SELECT SUM(b.brief_visit_issue_count) FROM brief_visits_per_sprint b WHERE b.sprint_id = c.sprint_id) AS less_the_shown_counts "
            + "FROM cycle_starts_with_brief_visit_per_sprint c;")).IsEqualTo(
            LeftOutHead
            + "| 41 | Fake Development | NULL | 1 | NULL | 1 |\n"
            + "| 41 | Fake Review | 3 | 0 | 3 | 0 |\n"
            + "| sprint_id | issue_count | less_the_shown_counts |\n| --- | --- | --- |\n"
            + $"| 41 | {cycles} | {lessTheShownCounts} |\n");
    }

    [Test]
    [Arguments(1, 3, 2, 1, "| 2 | suppressed | suppressed |")]
    [Arguments(3, 5, 3, 2, "| 3 | 2.0 | 6.0 |")]
    public async Task A_shown_brief_visit_count_beside_what_remains_of_its_row_gives_a_lower_bound_of_a_withheld_number_left_out(
        int withALongStayToo, int briefVisits, int remain, int atLeastLeftOut, string rowCells)
    {
        // What the owner accepted (D98, as widened on 2026-11-17), as shown. Two people's issues were caught up through Development and left out, so
        // the number left out is two and withheld. One or three more people's issues had a brief visit and a
        // stay of two days, and with one, a fourth person's issue did two days there. The brief visit count is
        // of the row before (D93, D99) and is shown; the row's issue count is of what remains and is always
        // shown. Every issue left out had a brief visit, so the count less the row's issue count is the least
        // the withheld number can be: it is not nothing. With one such issue the row that remains has two
        // people behind it and reads "suppressed" beside the shown count, which says the same.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        Scenarios.CaughtUp(pipeline.Jira, "FAKE-1", People.One);
        Scenarios.CaughtUp(pipeline.Jira, "FAKE-2", People.Two);
        var withALongStay = People.Team.Skip(2).Take(withALongStayToo);
        foreach (var (person, number) in withALongStay.Select((person, index) => (person, index + 3)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 7, 10), Workflow.Development)
                .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.New)
                .Moved(Local(2026, 1, 8, 9), Workflow.Development)
                .Moved(Local(2026, 1, 12, 9), Workflow.Done);
        }

        if (withALongStayToo == 1)
        {
            AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-11", People.Four);
        }

        ListDevelopment(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: false);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development" + Mark + " " + rowCells + "\n");
        await Assert.That(report).Contains($"\n- Fake Development: {briefVisits} issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("suppressed") + "\n");
        await Assert.That(await QueryAsync(
            pipeline,
            "SELECT b.brief_visit_issue_count, t.issue_count, b.brief_visit_issue_count - t.issue_count AS at_least_left_out, b.left_out_issue_count, b.left_out_suppressed "
            + "FROM brief_visits_per_sprint b JOIN time_in_status_per_sprint t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id;")).IsEqualTo(
            "| brief_visit_issue_count | issue_count | at_least_left_out | left_out_issue_count | left_out_suppressed |\n| --- | --- | --- | --- | --- |\n"
            + $"| {briefVisits} | {remain} | {atLeastLeftOut} | NULL | 1 |\n");
    }

    [Test]
    public async Task With_brief_visits_left_out_a_sprint_short_of_a_team_has_no_marks_and_no_brief_visit_lines_and_its_number_of_cycles_is_withheld_with_its_delivered_count()
    {
        // Four people, and no fifth: each one's issue was caught up through Development and delivered. Four
        // people are behind the number of cycles that start with a brief visit, enough for a figure; it is
        // withheld because the delivered count is, for the team.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        ListDevelopment(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The line under the heading is there, as for any sprint: it says how the report measures.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).StartsWith(
            "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n"
            + "Brief visits are left out of time in status and of cycle time in this report. A row of the time in status table marked \"adjusted\" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit."
            + " A delivered issue whose cycle starts with a brief visit has no cycle time: the cycle time table's figures are of the cycles that remain, and the footer gives the number of such issues.\n\n"
            + "Delivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n");
        await Assert.That(report).Contains("\n## Time in status\n\nsuppressed\n");
        await Assert.That(report).DoesNotContain("| Fake");
        await Assert.That(report).DoesNotContain("a visit shorter than");
        await Assert.That(report).DoesNotContain("left out of the status's time in status row");
        await Assert.That(report).EndsWith(
            "\n- Issues delivered between sprints, in the whole store: suppressed\n" + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("suppressed"));
        await Assert.That(await QueryAsync(pipeline, LeftOutSql, CycleStartsSql)).IsEqualTo(LeftOutHead + CycleStartsHead + "| 41 | NULL | 1 |\n");
    }

    [Test]
    public async Task With_both_numbers_zero_every_count_is_shown_under_exclude_and_a_count_of_nothing_is_0()
    {
        // One person's issue was caught up through Development and spent two days in Review; another's did two
        // days in Development. Nobody had a brief visit to Review. The default numbers withhold the sprint whole.
        await using var pipeline = Scenarios.SuppressionOff();
        pipeline.Jira.AddSprint41();
        Scenarios.CaughtUp(pipeline.Jira, "FAKE-1", People.One);
        AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-2", People.Two);
        ListDevelopmentAndReview(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 2\n");
        await Assert.That(report).Contains("\n| Fake Development" + Mark + " | 1 | 2.0 | 2.0 |\n| Fake Review" + Mark + " | 1 | 2.0 | 2.0 |\n");
        await Assert.That(report).EndsWith(
            "\n- Issues delivered between sprints, in the whole store: 0\n"
            + "- Fake Development: 1 issue had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("1") + "\n"
            + "- Fake Review: 0 issues had a visit shorter than 2 minutes" + ReportText.LeftOutOfTheRow("0") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("1"));
        await Assert.That(await QueryAsync(pipeline, LeftOutSql, CycleStartsSql)).IsEqualTo(
            LeftOutHead
            + "| 41 | Fake Development | 1 | 0 | 1 | 0 |\n"
            + "| 41 | Fake Review | 0 | 0 | 0 | 0 |\n"
            + CycleStartsHead + "| 41 | 1 | 0 |\n");
    }

    [Test]
    [Arguments("4", 1)]
    [Arguments("NULL", 0)]
    [Arguments("NULL", 1)]
    public async Task The_report_prints_no_number_left_out_from_a_row_the_view_marks_suppressed_and_none_from_a_row_without_one(string leftOut, int suppressed)
    {
        // The flag decides, as it does for every figure: a view that set the flag and still let a number
        // through (a fault, or a view changed by hand) must not get it into the report, and a row with no
        // number and no flag must not read as nothing left out.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: false);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync(
            $"""
            DROP VIEW brief_visits_per_sprint;
            CREATE VIEW brief_visits_per_sprint AS
            SELECT '41' AS sprint_id, '{Workflow.Development.Id}' AS status_id, 'Fake Development' AS status_name, 4 AS board_position, 5.0 AS threshold_minutes,
                   7 AS brief_visit_issue_count, 0 AS suppressed, {leftOut} AS left_out_issue_count, {suppressed} AS left_out_suppressed;
            """);

        await pipeline.RunAllAsync(["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\n- Fake Development: 7 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("suppressed") + "\n");
    }

    [Test]
    public async Task No_account_id_name_email_or_summary_reaches_the_database_the_report_or_the_console_with_a_number_left_out_shown()
    {
        // PrivacyTests' scenario and scan, with a status listed, brief visits left out and suppression off, so
        // that the line and the view's row are written with their numbers: one more issue, in the sprint,
        // caught up through Development.
        await using var pipeline = Scenarios.SuppressionOff();
        PrivacyScan.AddScenario(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-7", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(Local(2026, 1, 7, 10), Workflow.Development, by: People.Three)
            .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["query", LeftOutSql]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "\n- Fake Development: 1 issue had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("1") + "\n"
            + ReportText.CyclesThatStartWithBriefVisitAndHaveNoCycleTime("0"));
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).Contains(LeftOutHead + "| 41 | Fake Development | 1 | 0 | 1 | 0 |\n");
        await PrivacyScan.AssertNothingForbiddenWasWrittenAsync(pipeline);
    }

    /// <summary>The issue numbers a test's argument lists, as in <c>"1,3,8"</c>; none for an empty string.</summary>
    private static int[] Numbers(string list) => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];

    /// <summary>The eight issues of <see cref="AddStore"/> that had a brief visit to Development, by the number a test names one with.</summary>
    private static readonly (int Number, FakePerson? Assignee)[] BriefVisitors =
    [
        (1, People.One), (2, People.One), (3, People.Two), (4, People.Two), (5, People.Three), (6, People.Four), (7, People.Five), (8, null),
    ];

    /// <summary>
    /// The store the tests of the number left out are set in, with Development listed at five minutes and
    /// Review at two, and brief visits left out of time in status and of cycle time. Sprints 41 and 42 have a
    /// team of five, whose own issues stay in New.
    /// <list type="bullet">
    /// <item>Sprint 41, Development: the eight issues of <see cref="BriefVisitors"/> each spent twenty seconds
    /// there on Wednesday the 7th and went back to New. Each has a second issue of the same assignee, numbered
    /// fifty higher, that spent the same twenty seconds in QE, which is not listed. One of each pair then did
    /// two days in Development, from Thursday 09:00 to Monday 09:00, and went back to New: the second issue
    /// for the pairs numbered in <paramref name="leftOut"/>, whose first issue is so left out of the row, and
    /// the first issue for the others. So the brief visit count is eight, the row that remains is eight issues
    /// of two days with the same people behind them, and the time in New and in QE is the same, whoever was
    /// left out. None of them is delivered.</item>
    /// <item>Sprint 41, Review: three more people's issues went from New through Review in twenty seconds and
    /// were done. So three people are behind a count of three, all left out, and Review has no row.</item>
    /// <item>Sprint 42: three people's issues, each <see cref="Scenarios.CaughtUp"/> in sprint 42. So three
    /// people are behind a Development count of three, all left out, and nobody had a brief visit to Review.</item>
    /// </list>
    /// </summary>
    private static void AddStore(PipelineHarness pipeline, params int[] leftOut)
    {
        var jira = pipeline.Jira;
        jira.AddSprints41And42();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        var wednesday = Local(2026, 1, 7, 10);
        foreach (var (number, assignee) in BriefVisitors)
        {
            var first = jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).InSprints(41)
                .Moved(wednesday, Workflow.Development)
                .Moved(wednesday.AddSeconds(20), Workflow.New);
            var second = jira.AddIssue($"FAKE-{number + 50}", Local(2026, 1, 5, 9)).InSprints(41)
                .Moved(wednesday, Workflow.QE)
                .Moved(wednesday.AddSeconds(20), Workflow.New);
            (leftOut.Contains(number) ? second : first)
                .Moved(Local(2026, 1, 8, 9), Workflow.Development)
                .Moved(Local(2026, 1, 12, 9), Workflow.New);
            if (assignee is not null)
            {
                first.AssignedTo(assignee);
                second.AssignedTo(assignee);
            }
        }

        AddThreeIssuesThatPassedThroughReview(jira);
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 31)))
        {
            Scenarios.CaughtUp(jira, $"FAKE-{number}", person, sprintId: 42);
        }

        ListDevelopmentAndReview(pipeline);
        LeaveBriefVisitsOut(pipeline, ofCycleTime: true);
    }

    /// <summary>Three issues of the team's last three people that went from New through Review in twenty seconds on Wednesday 7 January and were done: FAKE-21 to FAKE-23.</summary>
    private static void AddThreeIssuesThatPassedThroughReview(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 21)))
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 7, 10), Workflow.Review)
                .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Done);
        }
    }

    /// <summary>
    /// Three 3-point issues of three people, delivered in sprint 41: the first was caught up through
    /// Development in twenty seconds (<see cref="Scenarios.CaughtUp"/>), with a cycle of two days, and the
    /// other two did two days in Development. So the Development row and the 3-point bucket have three people
    /// behind them, and two once the first issue is left out.
    /// </summary>
    private static void AddThreePeoplesIssuesOfWhichOneWasCaughtUp(FakeJira jira)
    {
        Scenarios.CaughtUp(jira, "FAKE-1", People.One).Estimated(3);
        Scenarios.Delivered(jira, 41, "FAKE-2", points: 3, People.Two, days: 2);
        Scenarios.Delivered(jira, 41, "FAKE-3", points: 3, People.Three, days: 2);
    }

    private static void ListDevelopment(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

    private static void ListDevelopmentAndReview(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };

    /// <summary>Leaves brief visits out of time in status and, with <paramref name="ofCycleTime"/>, of cycle time too.</summary>
    private static void LeaveBriefVisitsOut(PipelineHarness pipeline, bool ofCycleTime) =>
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = ofCycleTime ? "exclude" : "count" };

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>Three issues with two days in Development (<see cref="AddTwoDaysInDevelopment"/>), of the team's last three people: FAKE-11 to FAKE-13.</summary>
    private static void AddTwoDaysInDevelopmentForThreePeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            AddTwoDaysInDevelopment(jira, $"FAKE-{number}", person);
        }
    }

    /// <summary>An issue of sprint 41 that was in Development from Tuesday 6 January 09:00 to Thursday 09:00, and then done.</summary>
    private static FakeIssue AddTwoDaysInDevelopment(FakeJira jira, string key, FakePerson assignee) =>
        jira.AddIssue(key, Local(2026, 1, 5, 9)).AssignedTo(assignee).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
}
