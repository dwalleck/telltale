using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The brief visit count, end to end (GLOSSARY: Brief visit; owner decisions D58 to D62): the owner lists a
/// status and a threshold in minutes under <c>briefVisitThresholds</c>, and after <c>derive</c> the report's
/// data quality footer says how many of the issues behind that status's time in status row had a visit shorter
/// than the threshold. <c>query</c> reads the same count from the metric view <c>brief_visits_per_sprint</c>.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; the tests add the issues that pass through the work statuses.
/// </summary>
public class BriefVisitCountTests
{
    private const string ViewSql =
        "SELECT sprint_id, status_id, status_name, board_position, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint ORDER BY sprint_id, board_position;";

    private const string ViewHead =
        "| sprint_id | status_id | status_name | board_position | threshold_minutes | brief_visit_issue_count | suppressed |\n| --- | --- | --- | --- | --- | --- | --- |\n";

    [Test]
    public async Task The_footer_and_the_view_give_the_number_of_issues_in_the_status_row_that_had_a_brief_visit()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);

        // A fifth issue did its two days in Development: it is in the row, and it had no brief visit.
        AddTwoDaysInDevelopment(pipeline.Jira, "FAKE-5", People.Five);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development | 5 | 0.0 | 2.0 |\n");
        await Assert.That(report).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake Development: 4 issues had a visit shorter than 5 minutes

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 4 | 0 |\n");
    }

    [Test]
    public async Task Each_listed_status_has_its_own_line_in_board_order_and_a_count_of_nothing_reads_as_a_withheld_one()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);

        // Listed out of board order. The four issues stayed two days in Review: nobody had a brief visit there.
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Review.Id] = 2, [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake Development: 4 issues had a visit shorter than 5 minutes
            - Fake Review: issues with a visit shorter than 2 minutes: suppressed

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead
            + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 4 | 0 |\n"
            + $"| 41 | {Workflow.Review.Id} | Fake Review | 5 | 2 | NULL | 1 |\n");
    }

    [Test]
    public async Task An_issue_that_is_not_in_the_status_row_is_not_counted_so_the_count_is_never_larger_than_the_rows()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);

        // Four people's issues passed through Development in twenty seconds on a Saturday: no business time
        // there, so none of them is in the Development row (owner decision D21).
        var saturday = Local(2026, 1, 10, 10);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(saturday, Workflow.Development)
                .Moved(saturday.AddSeconds(20), Workflow.Review)
                .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        }

        // The row is three other issues, of three people, two days in Development each.
        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            AddTwoDaysInDevelopment(pipeline.Jira, $"FAKE-{number}", person);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development | 3 | 2.0 | 6.0 |\n");
        await Assert.That(report).EndsWith("\n- Fake Development: issues with a visit shorter than 5 minutes: suppressed\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | NULL | 1 |\n");
    }

    [Test]
    public async Task The_count_is_withheld_when_fewer_than_three_assignees_are_behind_the_counted_issues()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfTwoPeopleInARowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The row itself is shown: five assignees are behind its seven issues.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development | 7 | 0.0 | 6.0 |\n");
        await Assert.That(report).EndsWith("\n- Fake Development: issues with a visit shorter than 5 minutes: suppressed\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | NULL | 1 |\n");
    }

    [Test]
    public async Task The_view_takes_the_number_of_assignees_a_count_needs_from_the_configured_minimum_group_size()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfTwoPeopleInARowOfFive(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["minimumGroupSize"] = Scenarios.MinimumGroupSize(team: 5, assigneesBehindAFigure: 2);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The count the default numbers withhold (the test above) is shown when a figure needs two assignees.
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 4 | 0 |\n");
    }

    [Test]
    public async Task The_count_is_withheld_when_the_status_row_has_its_median_withheld()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        CaughtUp(pipeline.Jira, "FAKE-1", People.One);
        CaughtUp(pipeline.Jira, "FAKE-2", People.Two);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development | 2 | suppressed | suppressed |\n");
        await Assert.That(report).EndsWith("\n- Fake Development: issues with a visit shorter than 5 minutes: suppressed\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | NULL | 1 |\n");
    }

    [Test]
    public async Task A_sprint_that_fails_the_team_check_has_no_brief_visit_lines_and_no_rows_in_the_view()
    {
        // Four people, and no fifth: each one's issue was caught up through Development.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n## Time in status\n\nsuppressed\n");
        await Assert.That(report).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n");
        await Assert.That(report).DoesNotContain("had a visit");
        await Assert.That(report).DoesNotContain("with a visit");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead);
    }

    [Test]
    public async Task The_derived_table_holds_an_issue_a_sprint_and_a_status_and_nothing_else()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        CaughtUp(pipeline.Jira, "FAKE-1", People.One);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // Story 33: no duration, no time, no number of visits, and nothing about a person.
        var columns = await pipeline.QueryAsync<string>("SELECT name FROM pragma_table_info('brief_visit') ORDER BY cid;");
        await Assert.That(columns).IsEquivalentTo(new[] { "issue_id", "sprint_id", "status_id" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var rows = await pipeline.QueryAsync<(string IssueId, string SprintId, string StatusId)>("SELECT issue_id, sprint_id, status_id FROM brief_visit;");
        await Assert.That(rows).IsEquivalentTo(new[] { (pipeline.Jira.IdOf("FAKE-1"), "41", Workflow.Development.Id) });
    }

    [Test]
    public async Task A_second_derive_of_the_same_raw_data_and_settings_gives_the_same_brief_visit_data()
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

        await Assert.That(counted).IsEqualTo(ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 4 | 0 |\n");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(counted);
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(store);
    }

    [Test]
    public async Task After_a_threshold_changes_report_and_query_refuse_until_derive_has_run_and_no_backfill_is_needed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithItsTeam(pipeline.Jira);
        AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 6 };
        var reportExit = await pipeline.RunAsync("report", "--sprint", "41");
        var queryExit = await pipeline.RunAsync("query", ViewSql);

        await Assert.That(reportExit).IsEqualTo(1);
        await Assert.That(queryExit).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "briefVisitThresholds in the configuration file changed since the derived data was built. Run derive first.\n"
            + "briefVisitThresholds in the configuration file changed since the derived data was built. Run derive first.\n");

        // Derive alone repairs it: the thresholds decide nothing about what is fetched.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n- Fake Development: 4 issues had a visit shorter than 6 minutes\n");
    }

    [Test]
    public async Task Listing_a_status_changes_no_other_figure_no_anomaly_and_nothing_derive_says_on_the_console()
    {
        // The same Jira twice: once with no setting, once with Development listed. One issue has a changelog
        // anomaly, so the footer's anomaly count has something to count.
        await using var without = new PipelineHarness();
        await using var with = new PipelineHarness();
        foreach (var pipeline in new[] { without, with })
        {
            AddSprint41WithItsTeam(pipeline.Jira);
            AddFourCaughtUpIssuesOfFourPeople(pipeline.Jira);
            pipeline.Jira.AddIssue("FAKE-9", Local(2026, 1, 5, 9)).AssignedTo(People.Five).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .NowIn(Workflow.Review);
        }

        with.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await without.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        await with.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // With no setting: no line, no row, and the footer ends where it always did.
        var reportWithout = await without.ReadReportAsync(41);
        await Assert.That(reportWithout).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n");
        await Assert.That(await QueryAsync(without, ViewSql)).IsEqualTo(ViewHead);

        // With the setting: the same report with one line more, and the console said the same. The method note
        // differs in the two places that are about brief visits (ticket 05): its sentence on the footer's two
        // counts calls them the first two, and it ends with one paragraph on brief visits.
        var reportWith = await with.ReadReportAsync(41);
        var paragraphs = reportWith.Split('\n');
        await Assert.That(paragraphs.Count(paragraph => paragraph.StartsWith("- A brief visit is ", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(string.Join('\n', paragraphs.Where(paragraph => !paragraph.StartsWith("- A brief visit is ", StringComparison.Ordinal))).Replace("The footer's first two counts read", "The footer's two counts read", StringComparison.Ordinal))
            .IsEqualTo(reportWithout + "- Fake Development: 4 issues had a visit shorter than 5 minutes\n");
        await Assert.That(with.Error.ToString()).IsEqualTo(without.Error.ToString());
        await Assert.That(without.Error.ToString()).Contains("Changelog anomalies: 1 across 1 issue");

        // Every table and view but the three that hold the setting and the brief visits is the same, row for
        // row: the anomalies, the footer's counts and every figure of every other view.
        string[] aboutBriefVisits = ["brief_visit", "brief_visits_per_sprint", "run"];
        var storeWithout = BlocksOf(await without.ReadStoreContentAsync());
        var storeWith = BlocksOf(await with.ReadStoreContentAsync());
        await Assert.That(storeWith.Keys.Order()).IsEquivalentTo(storeWithout.Keys.Order(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(storeWith.Keys).Contains("time_in_status_per_sprint");
        foreach (var name in storeWithout.Keys.Except(aboutBriefVisits))
        {
            await Assert.That(storeWith[name]).IsEqualTo(storeWithout[name]);
        }

        await Assert.That(storeWithout["brief_visit"]).IsEqualTo("brief_visit\n\n");
        await Assert.That(storeWith["anomaly"]).Contains(with.Jira.IdOf("FAKE-9"));
    }

    /// <summary>Sprint 41 as the harness has it, and a team of five whose issues stay in New through it.</summary>
    private static void AddSprint41WithItsTeam(FakeJira jira)
    {
        jira.AddSprint41();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
    }

    /// <summary>Four issues <see cref="CaughtUp"/>, one for each of four people: FAKE-1 to FAKE-4.</summary>
    private static void AddFourCaughtUpIssuesOfFourPeople(FakeJira jira)
    {
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            CaughtUp(jira, $"FAKE-{number}", person);
        }
    }

    /// <summary>
    /// A Development row of seven issues and five assignees: four issues <see cref="CaughtUp"/>, two each for
    /// two people, and three more people's issues that did two days in Development.
    /// </summary>
    private static void AddFourCaughtUpIssuesOfTwoPeopleInARowOfFive(FakeJira jira)
    {
        foreach (var (person, number) in new[] { (People.One, 1), (People.Two, 2), (People.One, 3), (People.Two, 4) })
        {
            CaughtUp(jira, $"FAKE-{number}", person);
        }

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

    /// <summary>The store's content (<see cref="PipelineHarness.ReadStoreContentAsync"/>) as one block per table or view, by its name; a block starts with the name.</summary>
    private static Dictionary<string, string> BlocksOf(string storeContent) =>
        ("\n" + storeContent).Split("\n## ", StringSplitOptions.RemoveEmptyEntries).ToDictionary(block => block[..block.IndexOf('\n')], block => block + "\n", StringComparer.Ordinal);

    /// <summary>
    /// An issue of sprint 41 that was caught up in one go: in New (a not started status) until Wednesday
    /// 7 January 10:00, then moved to Development and, twenty seconds later, to Review, where it stayed for two
    /// days before it was done. So its one visit to Development lasted twenty seconds.
    /// </summary>
    private static FakeIssue CaughtUp(FakeJira jira, string key, FakePerson assignee)
    {
        var caughtUp = Local(2026, 1, 7, 10);
        return jira.AddIssue(key, Local(2026, 1, 5, 9)).AssignedTo(assignee).InSprints(41)
            .Moved(caughtUp, Workflow.Development)
            .Moved(caughtUp.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 9, 10), Workflow.Done);
    }
}
