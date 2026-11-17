using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The treatment <c>exclude</c> for time in status, end to end (GLOSSARY: Treatment; owner decisions D83, D84,
/// D86, D92, D93 and D99): the owner writes <c>briefVisitTreatment</c> with <c>timeInStatus</c> as
/// <c>exclude</c>, and after <c>derive</c> the time of every brief visit to a listed status is left out of each
/// issue's time in that status. An issue with no other business time there leaves the status's row.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; the tests add the issues that pass through the work statuses. Development
/// is listed with a threshold of five minutes unless a test says otherwise.
/// </summary>
public class BriefVisitsLeftOutOfTimeInStatusTests
{
    /// <summary>The time in status rows of the statuses after New, in which the idle team sits. Days are rounded to a thousandth: twenty seconds are 0.0002 of a day.</summary>
    private const string RowsSql =
        "SELECT sprint_id, status_name, issue_count, ROUND(median_business_days, 3) AS median_business_days, ROUND(total_business_days, 3) AS total_business_days, suppressed FROM time_in_status_per_sprint WHERE status_name <> 'Fake New' ORDER BY sprint_id, board_position;";

    private const string RowsHead =
        "| sprint_id | status_name | issue_count | median_business_days | total_business_days | suppressed |\n| --- | --- | --- | --- | --- | --- |\n";

    private const string BriefVisitsSql =
        "SELECT sprint_id, status_name, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint ORDER BY sprint_id, board_position;";

    private const string BriefVisitsHead =
        "| sprint_id | status_name | threshold_minutes | brief_visit_issue_count | suppressed |\n| --- | --- | --- | --- | --- |\n";

    [Test]
    public async Task An_issue_whose_only_time_in_a_listed_status_is_a_brief_visit_is_not_in_the_status_row()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);

        // Four people's issues went through Development in twenty seconds; three more did two days there.
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"]);

        // Counted, the seven are in the row and the four pull its median to nothing. Left out, the row is the
        // three with a believable stay. Review is not listed: its row is the same.
        await Assert.That(counted).IsEqualTo(
            RowsHead
            + "| 41 | Fake Development | 7 | 0 | 6.001 | 0 |\n"
            + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(
            RowsHead
            + "| 41 | Fake Development | 3 | 2 | 6 | 0 |\n"
            + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n");
    }

    [Test]
    public async Task An_issue_with_a_brief_visit_and_a_longer_stay_in_the_status_stays_in_the_row_with_the_brief_visit_left_out()
    {
        // Three people's issues were each in Development for six hours on Tuesday (a quarter of a business
        // day, and brief under a threshold of twelve hours), back in New overnight, and then in Development
        // from Wednesday 09:00 to Friday 09:00.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 6, 15), Workflow.New)
                .Moved(Local(2026, 1, 7, 9), Workflow.Development)
                .Moved(Local(2026, 1, 9, 9), Workflow.Done);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 720 };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"]);

        // The count is the same; the median and the total are of the two days that remain of each issue.
        await Assert.That(counted).IsEqualTo(RowsHead + "| 41 | Fake Development | 3 | 2.25 | 6.75 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(RowsHead + "| 41 | Fake Development | 3 | 2 | 6 | 0 |\n");
    }

    [Test]
    public async Task The_baseline_median_is_of_what_remains_in_every_baseline_sprint_and_a_baseline_sprint_left_with_no_row_does_not_withhold_it()
    {
        // Sprints 41 and 42 are the baseline. In sprint 41 four people's issues were caught up through
        // Development and nobody else was there. In sprint 42 three people's issues did three days there.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprints41And42();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 21)))
        {
            Scenarios.Delivered(pipeline.Jira, 42, $"FAKE-{number}", points: 3, person, days: 3);
        }

        pipeline.Config["baselineSprintIds"] = new JsonArray("41", "42");
        ListDevelopment(pipeline);
        const string BaselineSql = "SELECT status_name, ROUND(median_business_days, 3) AS median_business_days, suppressed FROM baseline_time_in_status WHERE status_name = 'Fake Development';";
        const string BaselineHead = "| status_name | median_business_days | suppressed |\n| --- | --- | --- |\n";

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql, BaselineSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "42"]);

        // Counted: seven issues are pooled, and the fourth of them is one caught up in twenty seconds.
        await Assert.That(counted).IsEqualTo(
            RowsHead
            + "| 41 | Fake Development | 4 | 0 | 0.001 | 0 |\n"
            + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n"
            + "| 42 | Fake Development | 3 | 3 | 9 | 0 |\n"
            + BaselineHead
            + "| Fake Development | 0 | 0 |\n");

        // Left out: sprint 41 has no Development row, which withholds nothing, and the pool is sprint 42's three.
        await Assert.That(await QueryAsync(pipeline, RowsSql, BaselineSql)).IsEqualTo(
            RowsHead
            + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n"
            + "| 42 | Fake Development | 3 | 3 | 9 | 0 |\n"
            + BaselineHead
            + "| Fake Development | 3 | 0 |\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\n| Fake Development (adjusted: see the footer) | 3 | 3.0 | 9.0 | 3.0 |\n");
    }

    [Test]
    public async Task A_brief_visit_to_a_status_that_is_not_listed_changes_nothing()
    {
        // The four issues spent twenty seconds in Development. Only Review is listed, and nobody was brief there.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Review.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(counted).IsEqualTo(
            RowsHead
            + "| 41 | Fake Development | 4 | 0 | 0.001 | 0 |\n"
            + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(counted);
    }

    [Test]
    public async Task A_short_stay_bounded_by_a_changelog_anomaly_is_not_left_out()
    {
        // Three people's issues were in Development for twenty seconds by the changelog, but the change that
        // took each out says it came from Review: a change is missing, and the stay's real length is unknown.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        var wednesday = Local(2026, 1, 7, 10);
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(wednesday, Workflow.Development)
                .AddHistory(wednesday.AddSeconds(20), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.QE.Id, "Fake QE")
                .NowIn(Workflow.QE);
        }

        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(counted).StartsWith(RowsHead + "| 41 | Fake Development | 3 | 0 | 0.001 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(counted);
    }

    [Test]
    public async Task A_long_stay_of_which_the_sprint_counts_only_seconds_is_not_left_out_because_the_visit_is_judged_before_it_is_clipped()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);

        // In Development from the Friday before the sprint until twenty seconds after it starts on Monday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(Local(2026, 1, 2, 10), Workflow.Development)
            .Moved(Local(2026, 1, 5, 9).AddSeconds(20), Workflow.Done);

        // In Development for two days, and added to the sprint twenty seconds before leaving it.
        foreach (var (person, number) in new[] { (People.Two, 2), (People.Three, 3) })
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .SprintsChanged(Local(2026, 1, 8, 9).AddSeconds(-20), 41)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }

        ListDevelopment(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"]);

        // Sixty seconds in all, of three stays that were each far longer than the threshold.
        await Assert.That(counted).IsEqualTo(RowsHead + "| 41 | Fake Development | 3 | 0 | 0.001 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(counted);
    }

    [Test]
    public async Task The_team_of_a_sprint_does_not_change_when_a_persons_only_time_in_it_was_left_out()
    {
        // Four people's issues stay in New through the sprint. The fifth person's one issue was a member of
        // the sprint for ten seconds, in the middle of a visit of twenty seconds to Development.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 81)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 2, 9)).AssignedTo(person).InSprints(41);
        }

        var wednesday = Local(2026, 1, 7, 10);
        pipeline.Jira.AddIssue("FAKE-5", Local(2026, 1, 5, 9)).AssignedTo(People.Five)
            .Moved(wednesday, Workflow.Development)
            .SprintsChanged(wednesday.AddSeconds(5), 41)
            .SprintsChanged(wednesday.AddSeconds(15))
            .Moved(wednesday.AddSeconds(20), Workflow.Review);
        ListDevelopment(pipeline);
        const string TeamSql = "SELECT sprint_id, team_suppressed FROM sprint_team;";
        const string TeamOfFive = "| sprint_id | team_suppressed |\n| --- | --- |\n| 41 | 0 |\n";

        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, TeamSql, RowsSql);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        // Counted, the fifth person's issue is the Development row. Left out, the row is gone and the team is
        // still five: the tables are shown. New is the table's one row: four issues, each there for the sprint's
        // ten business days and an hour.
        await Assert.That(counted).IsEqualTo(TeamOfFive + RowsHead + "| 41 | Fake Development | 1 | NULL | NULL | 1 |\n");
        await Assert.That(await QueryAsync(pipeline, TeamSql, RowsSql)).IsEqualTo(TeamOfFive + RowsHead);
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains(ReportText.TimeInStatusHeader + "| Fake New | 4 | 10.0 | 40.2 |\n" + ReportText.MethodHeading);
    }

    [Test]
    public async Task A_derive_that_leaves_brief_visits_out_changes_no_raw_table()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["backfill"]);
        var before = BlocksOf(await pipeline.ReadStoreContentAsync());

        await pipeline.RunAllAsync(["derive"]);

        // Before the derive the store holds the raw tables and nothing derived. derived_object is the list of
        // what a rebuild created, so it is the one table of these that a derive fills.
        var after = BlocksOf(await pipeline.ReadStoreContentAsync());
        await Assert.That(before.Keys).Contains("issue");
        await Assert.That(before.Keys).Contains("change");
        await Assert.That(before.Keys).DoesNotContain("sprint_status_time");
        await Assert.That(after.Keys).Contains("sprint_status_time");
        foreach (var name in before.Keys.Except(["derived_object"]))
        {
            await Assert.That(after[name]).IsEqualTo(before[name]);
        }
    }

    [Test]
    public async Task Leaving_out_changes_no_anomaly_and_nothing_derive_or_run_says_on_the_console()
    {
        // The same Jira twice, with Development listed: once counted, once left out. One issue has a changelog
        // anomaly, so the anomaly count has something to count.
        await using var counted = new PipelineHarness();
        await using var leftOut = new PipelineHarness();
        foreach (var pipeline in new[] { counted, leftOut })
        {
            AddSprint41WithItsTeam(pipeline.Jira);
            AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
            AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
            pipeline.Jira.AddIssue("FAKE-9", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .NowIn(Workflow.Review);
            ListDevelopment(pipeline);
        }

        LeaveBriefVisitsOutOfTimeInStatus(leftOut);

        await counted.RunAllAsync(["backfill"], ["derive"], ["run", "--sprint", "41"]);
        await leftOut.RunAllAsync(["backfill"], ["derive"], ["run", "--sprint", "41"]);

        // The console says the same under either treatment, and the anomalies are the same.
        var storeLeftOut = BlocksOf(await leftOut.ReadStoreContentAsync());
        await Assert.That(leftOut.Error.ToString()).IsEqualTo(counted.Error.ToString());
        await Assert.That(leftOut.Out.ToString().Replace(leftOut.Directory, counted.Directory, StringComparison.Ordinal)).IsEqualTo(counted.Out.ToString());
        await Assert.That(counted.Error.ToString()).Contains("Changelog anomalies: 1 across 1 issue");
        var storeCounted = BlocksOf(await counted.ReadStoreContentAsync());
        await Assert.That(storeLeftOut["anomaly"]).IsEqualTo(storeCounted["anomaly"]);
        await Assert.That(storeLeftOut["anomaly"]).Contains(leftOut.Jira.IdOf("FAKE-9"));
        await Assert.That(storeLeftOut["data_quality"]).IsEqualTo(storeCounted["data_quality"]);

        // And the two did measure differently.
        await Assert.That(storeLeftOut["time_in_status_per_sprint"]).IsNotEqualTo(storeCounted["time_in_status_per_sprint"]);
    }

    [Test]
    public async Task A_second_derive_of_the_same_raw_data_and_settings_gives_the_same_figures()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var rows = await QueryAsync(pipeline, RowsSql);
        var store = await pipeline.ReadStoreContentAsync(withoutAsOf: true);

        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(rows).Contains("| 41 | Fake Development | 3 | 2 | 6 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(rows);
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(store);
    }

    [Test]
    [Arguments("SELECT sprint_id, status_id, issue_id FROM time_in_status_issue_recorded;")]
    [Arguments("SELECT sprint_id, status_id, COUNT(*) FROM time_in_status_issue_recorded GROUP BY sprint_id, status_id;")]
    [Arguments("SELECT b.status_name, (SELECT COUNT(*) FROM time_in_status_issue_recorded t WHERE t.sprint_id = b.sprint_id AND t.status_id = b.status_id) FROM brief_visits_per_sprint b;")]
    public async Task Query_refuses_to_read_the_internal_view_of_the_issues_that_were_in_a_row_before_anything_was_left_out(string sql)
    {
        // The view holds a row per issue. A count of them less the row's shown issue count is the number of
        // issues left out of the row, which no metric view gives yet.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunAsync("query", sql);

        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString();
        await Assert.That(error).StartsWith("This query reads only the metric views (");
        await Assert.That(error).Contains("), and it asked for something else: ");
        await Assert.That(error).DoesNotContain("time_in_status_issue_recorded, ");
        await Assert.That(error.TrimEnd().Split('\n').Length).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
    }

    [Test]
    public async Task A_status_whose_every_issue_was_left_out_has_no_row_and_its_brief_visit_count_is_still_of_the_row_before()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // Nobody is left in Development, so it has no row, as a status nobody spent time in has none. The count
        // that explains it is of the four that were in the row before (D93).
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(RowsHead + "| 41 | Fake Review | 4 | 2 | 7.999 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, BriefVisitsSql)).IsEqualTo(BriefVisitsHead + "| 41 | Fake Development | 5 | 4 | 0 |\n");
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).DoesNotContain("| Fake Development");
        // The number of cycles that start with a brief visit is what it is while brief visits are counted (D87).
        await Assert.That(report).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("4") + "\n" + ReportText.CyclesThatStartWithBriefVisit("4"));
    }

    [Test]
    public async Task The_brief_visit_count_is_withheld_by_the_check_of_the_row_before_so_it_is_shown_beside_a_row_that_reads_suppressed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);

        // Before: five issues of five people. Three were caught up, so two issues of two people remain.
        foreach (var (person, number) in People.Team.Take(3).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-11", People.Four);
        AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-12", People.Five);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The row that remains has two assignees behind it and withholds its figures. The count has three
        // behind it, and the row it is measured against had five (D99).
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(
            RowsHead
            + "| 41 | Fake Development | 2 | NULL | NULL | 1 |\n"
            + "| 41 | Fake Review | 3 | 2 | 5.999 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, BriefVisitsSql)).IsEqualTo(BriefVisitsHead + "| 41 | Fake Development | 5 | 3 | 0 |\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n- Fake Development: 3 issues had a visit shorter than 5 minutes" + ReportText.LeftOutOfTheRow("3") + "\n" + ReportText.CyclesThatStartWithBriefVisit("3"));
    }

    [Test]
    public async Task The_report_says_so_in_one_line_under_its_heading_and_marks_the_row_of_every_listed_status_also_with_nothing_left_out()
    {
        // Nobody had a brief visit: three people's issues did two days in Development, and nobody was in Review.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var counted = await pipeline.ReadReportAsync(41);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var leftOut = await pipeline.ReadReportAsync(41);

        // Counted: no line and no mark.
        await Assert.That(counted).StartsWith(Heading + "Delivered issues: ");
        await Assert.That(counted).Contains("\n| Fake Development | 3 | 2.0 | 6.0 |\n");
        await Assert.That(counted).DoesNotContain(Mark);

        // Left out: the line, and the mark on the listed status's row, which holds the figures it held. New is
        // not listed and has no mark.
        await Assert.That(leftOut).StartsWith(Heading + LineUnderTheHeading + "Delivered issues: ");
        await Assert.That(leftOut).Contains("\n| Fake New | 8 | ");
        await Assert.That(leftOut).Contains("\n| Fake Development (adjusted: see the footer) | 3 | 2.0 | 6.0 |\n");

        // Nothing else differs but the method note's sentences that leaving out makes untrue (bvt-D10): the
        // report with those five put back as they read under count, and the line, the mark and what the
        // footer line goes on to say taken away, is the report under count. The footer line of the listed
        // status that has a row goes on to the number left out of it, which is nothing and so reads as a
        // withheld number does.
        await Assert.That(leftOut).Contains("\n- Fake Development: issues with a visit shorter than 5 minutes: suppressed" + ReportText.LeftOutOfTheRow("suppressed") + "\n");
        var putBack = leftOut.Replace(LineUnderTheHeading, "").Replace(Mark, "").Replace(ReportText.LeftOutOfTheRow("suppressed"), "");
        foreach (var (leftOutWording, countedWording) in MethodNoteSentences)
        {
            await Assert.That(counted).Contains(countedWording);
            await Assert.That(counted).DoesNotContain(leftOutWording);
            await Assert.That(putBack).Contains(leftOutWording);
            putBack = putBack.Replace(leftOutWording, countedWording);
        }

        await Assert.That(putBack).IsEqualTo(counted);
    }

    [Test]
    public async Task The_line_under_the_heading_follows_the_line_on_a_lowered_minimum_group_size()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddTwoDaysInDevelopmentForThreePeople(pipeline.Jira);
        ListDevelopment(pipeline);
        LeaveBriefVisitsOutOfTimeInStatus(pipeline);
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith(
            Heading
            + "Small-group suppression is off: a figure in this report may rest on the work of one or two people.\n\n"
            + LineUnderTheHeading
            + "Delivered issues: ");
    }

    private const string Heading = "# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n\n";

    private const string LineUnderTheHeading =
        "Brief visits are left out of time in status in this report. A row of that table marked \"adjusted\" is of a status listed for brief visits: its figures are of what remains, and the footer gives the number of its issues that had a brief visit.\n\n";

    private const string Mark = " (adjusted: see the footer)";

    /// <summary>
    /// The sentences of the method note that leaving out makes untrue: one of its paragraph on time in status,
    /// on what the total sums, and four of its paragraph on brief visits. Each is given with its wording when
    /// brief visits are left out of time in status and its wording while they are counted, which is what it
    /// was before a treatment could be chosen. Under the default numbers.
    /// </summary>
    private static readonly (string LeftOut, string Counted)[] MethodNoteSentences =
    [
        (" Total business days sum that time across all contributing issues, including repeated visits and leaving out brief visits to a status listed for them (see the paragraph on brief visits below); this is accumulated issue time,",
            " Total business days sum that time across all contributing issues, including repeated visits; this is accumulated issue time,"),
        (" For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues that were in the status's time in status row before anything was left out had a brief visit during the time the row counts.",
            " For each status the configuration file lists (briefVisitThresholds), the footer gives how many of the issues in the status's time in status row had a brief visit during the time the row counts."),
        (" The time of every brief visit to a listed status is left out of the issue's time in that status (briefVisitTreatment in the configuration file), and an issue left with no business time in the status is not in its row: the row's count, median and total, and the baseline median, are of what remains. No other figure was changed because of a brief visit, and no duration was estimated or replaced.",
            " Brief visits are counted and nothing more: no duration was excluded or corrected because of one, and every other figure is what it would be without the count."),
        (" A count reads \"suppressed\" when its row in the time in status table would read \"suppressed\" for its figures with nothing left out, or when fewer than three distinct assignees are among the issues counted.",
            " A count reads \"suppressed\" when its row in the time in status table reads \"suppressed\" for its figures, or when fewer than three distinct assignees are among the issues counted."),
        (" A listed status that had no row in the time in status table before anything was left out has no line; one whose every issue was left out has a line and no row, and a sprint whose team is smaller than five has no line for any listed status.",
            " A listed status with no row in the time in status table has no line, and a sprint whose team is smaller than five has no line for any listed status."),
    ];

    /// <summary>The store's content (<see cref="PipelineHarness.ReadStoreContentAsync"/>) as one block per table or view, by its name; a block starts with the name.</summary>
    private static Dictionary<string, string> BlocksOf(string storeContent) =>
        ("\n" + storeContent).Split("\n## ", StringSplitOptions.RemoveEmptyEntries).ToDictionary(block => block[..block.IndexOf('\n')], block => block + "\n", StringComparer.Ordinal);

    private static void ListDevelopment(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

    private static void LeaveBriefVisitsOutOfTimeInStatus(PipelineHarness pipeline) =>
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>Four issues <see cref="Scenarios.CaughtUp"/>, one for each of four people: FAKE-1 to FAKE-4.</summary>
    private static void AddFourCaughtUpIssuesOfFourPeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(jira, $"FAKE-{number}", person);
        }
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
