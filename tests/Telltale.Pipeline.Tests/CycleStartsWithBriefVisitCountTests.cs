using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The number of delivered issues whose cycle starts with a brief visit, end to end (GLOSSARY: Brief visit,
/// Minimum group size; owner decisions D87, D90, D91, D97 and D100): with a work status listed under
/// <c>briefVisitThresholds</c>, each sprint report's data quality footer gives the number directly after its
/// brief visit lines, and <c>query</c> reads the same number from the metric view
/// <c>cycle_starts_with_brief_visit_per_sprint</c>. No other figure changes.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; the tests add the issues that are delivered.
/// </summary>
public class CycleStartsWithBriefVisitCountTests
{
    private const string ViewSql = "SELECT sprint_id, issue_count, suppressed FROM cycle_starts_with_brief_visit_per_sprint ORDER BY sprint_id;";

    private const string ViewHead = "| sprint_id | issue_count | suppressed |\n| --- | --- | --- |\n";

    private const string Line = "- Delivered issues whose cycle starts with a brief visit, which are in the cycle time figures: ";

    [Test]
    public async Task The_footer_and_the_view_give_the_number_of_delivered_issues_whose_cycle_starts_with_a_brief_visit()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);

        // A fifth delivered issue did its two days in Development: its cycle does not start with a brief visit.
        AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-5", People.Five);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 5\n");
        await Assert.That(report).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake Development: 4 issues had a visit shorter than 5 minutes
            - Delivered issues whose cycle starts with a brief visit, which are in the cycle time figures: 4

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead + "| 41 | 4 | 0 |\n");
    }

    [Test]
    public async Task A_cycle_counts_when_the_stay_it_starts_with_is_a_brief_visit_and_for_no_other_brief_visit()
    {
        // Eight delivered issues, with Development listed under five minutes and Review not listed.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        var jira = pipeline.Jira;
        var wednesday = Local(2026, 1, 7, 10);

        // Counts: caught up, twenty seconds in Development and then two days in Review.
        Scenarios.CaughtUp(jira, "FAKE-1", People.One);

        // Does not count: two days in Review first, and twenty seconds in Development only later in the cycle.
        jira.AddIssue("FAKE-2", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Review)
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9).AddSeconds(20), Workflow.Done);

        // Does not count: the first stay is bounded by a changelog anomaly. Five seconds after the move into
        // Development a change says Review to QE, so nothing says when the issue left Development.
        jira.AddIssue("FAKE-3", Local(2026, 1, 5, 9)).AssignedTo(People.Three).InSprints(41)
            .Moved(wednesday, Workflow.Development)
            .AddHistory(wednesday.AddSeconds(5), "status", "status", Workflow.Review.Id, "Fake Review", Workflow.QE.Id, "Fake QE")
            .NowIn(Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Counts: created in Development and moved on twenty seconds later.
        jira.AddIssue("FAKE-4", wednesday, createdIn: Workflow.Development).AssignedTo(People.Four).InSprints(41)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Counts (D91): moved into Development and straight back out, and the work started three days later.
        jira.AddIssue("FAKE-5", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 6, 9).AddSeconds(10), Workflow.New)
            .Moved(Local(2026, 1, 9, 9), Workflow.Development)
            .Moved(Local(2026, 1, 13, 9), Workflow.Done);

        // Does not count: delivered without ever entering a work status, so it has no cycle.
        jira.AddIssue("FAKE-6", Local(2026, 1, 5, 9)).AssignedTo(People.One).InSprints(41)
            .Moved(Local(2026, 1, 5, 9).AddSeconds(20), Workflow.Done);

        // Does not count: its first stay in a work status is twenty seconds in Review, which is not listed,
        // though twenty seconds in Development follow.
        jira.AddIssue("FAKE-7", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(wednesday, Workflow.Review)
            .Moved(wednesday.AddSeconds(20), Workflow.Development)
            .Moved(wednesday.AddSeconds(40), Workflow.QE)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);

        // Has the flag and is in no sprint's number: caught up, and delivered after the sprint was completed.
        jira.AddIssue("FAKE-8", Local(2026, 1, 5, 9)).AssignedTo(People.Three).InSprints(41)
            .Moved(wednesday, Workflow.Development)
            .Moved(wednesday.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 20, 10), Workflow.Done);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // Seven were delivered in the sprint, and three of those count: three people's.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: 7\n");
        await Assert.That(report).EndsWith("\n" + Line + "3\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead + "| 41 | 3 | 0 |\n");
        var flagged = await pipeline.QueryAsync<string>("SELECT issue_id FROM issue_cycle WHERE starts_with_brief_visit = 1;");
        await Assert.That(flagged).IsEquivalentTo(new[] { "FAKE-1", "FAKE-4", "FAKE-5", "FAKE-8" }.Select(jira.IdOf));
    }

    [Test]
    [Arguments("", 5, 3, "NULL | 1", "suppressed")]
    [Arguments("1,2", 5, 3, "NULL | 1", "suppressed")]
    [Arguments("1,1,2,2", 5, 3, "NULL | 1", "suppressed")]
    [Arguments("1,2,0", 5, 3, "NULL | 1", "suppressed")]
    [Arguments("1,2,3", 5, 3, "3 | 0", "3")]
    [Arguments("1,2,3,0", 5, 3, "4 | 0", "4")]
    [Arguments("1,2", 5, 2, "2 | 0", "2")]
    [Arguments("0", 5, 1, "NULL | 1", "suppressed")]
    [Arguments("", 5, 1, "NULL | 1", "suppressed")]
    [Arguments("", 5, 0, "0 | 0", "0")]
    [Arguments("0", 5, 0, "1 | 0", "1")]
    public async Task The_number_is_shown_only_with_the_configured_number_of_distinct_assignees_behind_it_and_nothing_reads_as_a_withheld_number_does(
        string caughtUp, int team, int assigneesBehindAFigure, string countAndFlag, string figure)
    {
        // Three people each delivered an issue that did two days in Development, so the delivered count is
        // shown in every case. The issues caught up are listed by the person they are assigned to, 0 for
        // nobody: nothing; two people's; four issues of two people; two people's and an unassigned one; three
        // people's. An unassigned issue counts toward nobody, and is counted among the issues.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            AddTwoDaysInDevelopment(pipeline.Jira, $"FAKE-{number}", person);
        }

        var people = caughtUp.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        foreach (var (person, number) in people.Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person == 0 ? null : People.Team[person - 1]);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains($"\nDelivered issues: {3 + people.Count}\n");
        await Assert.That(report).EndsWith("\n" + Line + figure + "\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead + $"| 41 | {countAndFlag} |\n");
    }

    [Test]
    public async Task The_number_is_withheld_when_the_sprints_delivered_count_is_withheld_though_four_assignees_are_behind_it()
    {
        // Four people, and no fifth: the sprint is short of a team, so its delivered count is withheld. Each
        // one's issue was caught up through Development, so four assignees are behind the number.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n" + Line + "suppressed\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead + "| 41 | NULL | 1 |\n");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task With_no_listed_status_that_has_the_work_role_the_view_has_no_row_and_the_footer_no_line(bool newAndBlockedListed)
    {
        // A cycle starts in a work status, so with only New (not started) and Blocked listed, as with nothing
        // listed, no cycle can start with a brief visit.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithFourCaughtUpIssuesInTheFirst(pipeline.Jira);
        if (newAndBlockedListed)
        {
            pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5, [Workflow.Blocked.Id] = 5 };
        }

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);

        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead);
        await Assert.That(await pipeline.ReadReportAsync(41) + await pipeline.ReadReportAsync(42)).DoesNotContain("whose cycle starts with a brief visit");
    }

    [Test]
    public async Task With_a_work_status_listed_the_view_has_one_row_per_sprint_and_each_report_its_line_also_with_nothing_to_count()
    {
        // Review is listed and Development is not, so no cycle of sprint 41 starts with a brief visit, and in
        // sprint 42 nothing was delivered. Each sprint has its row and its line all the same.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsWithFourCaughtUpIssuesInTheFirst(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Review.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);

        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead + "| 41 | NULL | 1 |\n| 42 | NULL | 1 |\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n" + Line + "suppressed\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).EndsWith("\n" + Line + "suppressed\n");
    }

    [Test]
    public async Task The_derived_data_gains_one_flag_per_issue_and_one_per_status_and_nothing_about_a_person()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        Scenarios.CaughtUp(pipeline.Jira, "FAKE-1", People.One);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // Story 36: a flag, and no duration and no time. The other columns are the ones the table had.
        var cycleColumns = await pipeline.QueryAsync<string>("SELECT name || ' ' || type FROM pragma_table_info('issue_cycle') ORDER BY cid;");
        await Assert.That(cycleColumns).IsEquivalentTo(
            new[]
            {
                "issue_id TEXT", "cycle_start TEXT", "cycle_end TEXT", "calendar_days REAL", "business_days REAL", "point_bucket REAL", "delivered INTEGER",
                "delivery_sprint_id TEXT", "reopen_count INTEGER", "starts_with_brief_visit INTEGER",
            },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var statusColumns = await pipeline.QueryAsync<string>("SELECT name || ' ' || type FROM pragma_table_info('board_status') ORDER BY cid;");
        await Assert.That(statusColumns).IsEquivalentTo(
            new[] { "status_id TEXT", "position INTEGER", "is_done INTEGER", "is_work INTEGER" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var flags = await pipeline.QueryAsync<long>("SELECT DISTINCT starts_with_brief_visit FROM issue_cycle ORDER BY 1;");
        await Assert.That(flags).IsEquivalentTo(new[] { 0L, 1L }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var work = await pipeline.QueryAsync<string>("SELECT status_id FROM board_status WHERE is_work = 1 ORDER BY position;");
        await Assert.That(work).IsEquivalentTo(new[] { Workflow.Development.Id, Workflow.Review.Id, Workflow.QE.Id }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_second_derive_of_the_same_raw_data_and_settings_gives_the_same_number_and_the_same_flags()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var counted = await QueryAsync(pipeline, ViewSql);
        var store = await pipeline.ReadStoreContentAsync(withoutAsOf: true);

        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(counted).IsEqualTo(ViewHead + "| 41 | 4 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(counted);
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(store);
    }

    [Test]
    [Arguments("4", 1)]
    [Arguments("NULL", 0)]
    [Arguments("NULL", 1)]
    public async Task The_report_prints_no_number_from_a_row_the_view_marks_suppressed_and_none_from_a_row_without_one(string count, int suppressed)
    {
        // The flag decides, as it does for every figure: a view that set the flag and still let a number
        // through (a fault, or a view changed by hand) must not get it into the report, and a row with no
        // number and no flag must not read as a count of nothing.
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync(
            $"""
            DROP VIEW cycle_starts_with_brief_visit_per_sprint;
            CREATE VIEW cycle_starts_with_brief_visit_per_sprint AS
            SELECT '41' AS sprint_id, {count} AS issue_count, {suppressed} AS suppressed;
            """);

        await pipeline.RunAllAsync(["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n" + Line + "suppressed\n");
    }

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>
    /// Sprints 41 and 42, each with a team of five whose issues stay in New. Sprint 41 has four issues
    /// <see cref="Scenarios.CaughtUp"/> through Development, of four people; in sprint 42 nothing was delivered.
    /// </summary>
    private static void AddTwoSprintsWithFourCaughtUpIssuesInTheFirst(FakeJira jira)
    {
        jira.AddSprints41And42();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        AddFourCaughtUpIssuesOfFourPeople(jira);
    }

    /// <summary>Four issues <see cref="Scenarios.CaughtUp"/>, one for each of four people: FAKE-1 to FAKE-4.</summary>
    private static void AddFourCaughtUpIssuesOfFourPeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(jira, $"FAKE-{number}", person);
        }
    }

    /// <summary>An issue of sprint 41 that was in Development from Tuesday 6 January 09:00 to Thursday 09:00, and then done.</summary>
    private static FakeIssue AddTwoDaysInDevelopment(FakeJira jira, string key, FakePerson assignee) =>
        jira.AddIssue(key, Local(2026, 1, 5, 9)).AssignedTo(assignee).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);
}
