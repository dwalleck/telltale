using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The brief visit count and small groups (GLOSSARY: Brief visit, Minimum group size; owner decision D62 and
/// the confirmed reading on zero; ADR-0008): the count never says more about a small group than the rest of
/// the report does, in the report and through <c>query</c>, under the default numbers and under others.
/// <para>
/// Most tests are set in one store (<see cref="AddStore"/>). Its Development row in sprint 41 is eight issues
/// that each spent the last twenty seconds of a Friday there: two each for two people, one each for three
/// more, and one nobody is assigned to. A test says which of them left Development at midnight, a visit of
/// twenty seconds, and the others stay until ten on Saturday morning. The weekend counts toward no figure, so
/// two such stores differ in who had a brief visit and in nothing else a metric view shows.
/// </para>
/// </summary>
public class BriefVisitSmallGroupTests
{
    private const string ViewHead =
        "| sprint_id | status_id | status_name | board_position | threshold_minutes | brief_visit_issue_count | suppressed |\n| --- | --- | --- | --- | --- | --- | --- |\n";

    private const string ViewSql =
        "SELECT sprint_id, status_id, status_name, board_position, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint ORDER BY sprint_id, board_position;";

    private const string ViewOfSprint41Sql =
        "SELECT sprint_id, status_id, status_name, board_position, threshold_minutes, brief_visit_issue_count, suppressed FROM brief_visits_per_sprint WHERE sprint_id = '41' ORDER BY board_position;";

    private const string DevelopmentWithheld = "- Fake Development: issues with a visit shorter than 5 minutes: suppressed";
    private const string ReviewWithheld = "- Fake Review: issues with a visit shorter than 2 minutes: suppressed";
    private const string ReviewOfThree = "- Fake Review: 3 issues had a visit shorter than 2 minutes";

    /// <summary>The count and the flag of a withheld row, as <c>query</c> prints them.</summary>
    private const string Withheld = "NULL | 1";

    /// <summary>The Development row of sprint 41 in <see cref="AddStore"/>, by the number a test names an issue with.</summary>
    private static readonly (int Number, FakePerson? Assignee)[] DevelopmentRow =
    [
        (1, People.One), (2, People.One), (3, People.Two), (4, People.Two), (5, People.Three), (6, People.Four), (7, People.Five), (8, null),
    ];

    [Test]
    [Arguments("", DevelopmentWithheld, Withheld)]
    [Arguments("1", DevelopmentWithheld, Withheld)]
    [Arguments("1,2,3,4", DevelopmentWithheld, Withheld)]
    [Arguments("8", DevelopmentWithheld, Withheld)]
    [Arguments("1,3,8", DevelopmentWithheld, Withheld)]
    [Arguments("1,3,5", "- Fake Development: 3 issues had a visit shorter than 5 minutes", "3 | 0")]
    [Arguments("1,3,5,8", "- Fake Development: 4 issues had a visit shorter than 5 minutes", "4 | 0")]
    public async Task The_count_is_shown_only_with_three_distinct_assignees_behind_it_and_an_issue_nobody_is_assigned_to_counts_toward_nobody(
        string briefVisits, string line, string countAndFlag)
    {
        // Nobody; one person; four issues of two people; an unassigned issue alone; two people and an
        // unassigned issue: each is withheld, and each reads as the others do. Three people: shown, and the
        // unassigned issue is counted among the issues though it adds nobody.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, Numbers(briefVisits));

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // The row itself is shown: five people are behind its eight issues.
        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Development | 8 | 0.0 | 0.0 |\n");
        await Assert.That(report).EndsWith("\n" + line + "\n" + ReviewOfThree + "\n");
        await Assert.That(await QueryAsync(pipeline, ViewOfSprint41Sql)).IsEqualTo(
            ViewHead
            + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | {countAndFlag} |\n"
            + $"| 41 | {Workflow.Review.Id} | Fake Review | 5 | 2 | 3 | 0 |\n");
    }

    [Test]
    public async Task No_query_over_the_metric_views_tells_a_count_of_nothing_from_a_withheld_one_or_one_withheld_count_from_another()
    {
        // Five stores whose Development count in sprint 41 is withheld: nobody had a brief visit; one issue of
        // one person; two issues of two people; four issues of two people; two people's and an unassigned one.
        // Around it are figures a reader could try to subtract from: the row's issue count, a shown count for
        // Review in the same sprint, and a shown count for Development in sprint 42.
        string[] withheld = ["", "1", "1,3", "1,2,3,4", "1,3,8"];
        var read = new List<(string Reports, string Views, string Attempts)>();
        foreach (var briefVisits in withheld.Append("1,3,5"))
        {
            await using var pipeline = new PipelineHarness();
            AddStore(pipeline, Numbers(briefVisits));
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);

            // Everything query may read: each metric view whole, found in the store so that a view added later is read too.
            var views = await pipeline.QueryAsync<string>("SELECT name FROM derived_object WHERE kind = 'view' ORDER BY name;");
            await Assert.That(views).Contains("brief_visits_per_sprint");
            await Assert.That(views).Contains("time_in_status_per_sprint");
            read.Add((
                await pipeline.ReadReportAsync(41) + await pipeline.ReadReportAsync(42),
                await QueryAsync(pipeline, [.. views.Select(view => $"SELECT * FROM {view};")]),
                await QueryAsync(
                    pipeline,
                    ViewSql,
                    "SELECT SUM(brief_visit_issue_count) AS shown, COUNT(brief_visit_issue_count) AS counts, SUM(suppressed) AS withheld FROM brief_visits_per_sprint;",
                    "SELECT b.sprint_id, b.status_name, t.issue_count, t.issue_count - b.brief_visit_issue_count AS remainder FROM brief_visits_per_sprint b "
                    + "JOIN time_in_status_per_sprint t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id ORDER BY b.sprint_id, b.board_position;")));
        }

        // What the first store gives: the withheld count is null beside the shown ones, and no sum or
        // remainder holds it.
        const string DevelopmentRowWithheld = "| 41 | 600004 | Fake Development | 4 | 5 | NULL | 1 |\n";
        await Assert.That(read[0].Attempts).IsEqualTo(
            ViewHead
            + DevelopmentRowWithheld
            + """
            | 41 | 600005 | Fake Review | 5 | 2 | 3 | 0 |
            | 42 | 600004 | Fake Development | 4 | 5 | 3 | 0 |
            | 42 | 600005 | Fake Review | 5 | 2 | NULL | 1 |
            | shown | counts | withheld |
            | --- | --- | --- |
            | 6 | 2 | 2 |
            | sprint_id | status_name | issue_count | remainder |
            | --- | --- | --- | --- |
            | 41 | Fake Development | 8 | NULL |
            | 41 | Fake Review | 11 | 8 |
            | 42 | Fake Development | 3 | 0 |
            | 42 | Fake Review | 3 | NULL |

            """.ReplaceLineEndings("\n"));
        await Assert.That(read[0].Reports).Contains("\n" + DevelopmentWithheld + "\n" + ReviewOfThree + "\n");

        // The other four give the same, to the byte: every row of every metric view, and both reports. A
        // query reads nothing but those views, so none can tell these stores apart.
        foreach (var other in read[1..withheld.Length])
        {
            await Assert.That(other.Views).IsEqualTo(read[0].Views);
            await Assert.That(other.Attempts).IsEqualTo(read[0].Attempts);
            await Assert.That(other.Reports).IsEqualTo(read[0].Reports);
        }

        // The comparison can fail: with three people behind the count, that one row differs and nothing else.
        var shown = read[^1];
        await Assert.That(read[0].Views).Contains(DevelopmentRowWithheld);
        await Assert.That(shown.Views).IsEqualTo(read[0].Views.Replace(DevelopmentRowWithheld, "| 41 | 600004 | Fake Development | 4 | 5 | 3 | 0 |\n", StringComparison.Ordinal));
        await Assert.That(shown.Reports).IsEqualTo(
            read[0].Reports.Replace(DevelopmentWithheld + "\n" + ReviewOfThree, "- Fake Development: 3 issues had a visit shorter than 5 minutes\n" + ReviewOfThree, StringComparison.Ordinal));
    }

    [Test]
    public async Task A_listed_status_with_no_time_in_status_row_in_the_sprint_has_no_line_and_no_row()
    {
        // Four people's issues were caught up through Development on a Wednesday, and passed through QE in
        // twenty seconds on a Saturday: no business time there, so the sprint has no QE row (owner decision
        // D21). Nobody was in Blocked at all. All three statuses are listed.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        var saturday = Local(2026, 1, 10, 10);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 7, 10), Workflow.Development)
                .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review)
                .Moved(saturday, Workflow.QE)
                .Moved(saturday.AddSeconds(20), Workflow.Done);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.QE.Id] = 5, [Workflow.Blocked.Id] = 5 };

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n| Fake Review | 4 | 2.6 | 10.3 |\n\n## Method\n");
        await Assert.That(report).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: suppressed
            - Fake Development: 4 issues had a visit shorter than 5 minutes

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 4 | 0 |\n");
    }

    [Test]
    public async Task A_sprint_whose_delivered_count_is_withheld_still_shows_a_brief_visit_count_that_passes_its_own_checks_and_the_count_says_nothing_of_what_was_delivered()
    {
        // Owner decisions D49 and D55. Three people's issues were caught up through Development and are still
        // in Review. What was delivered went straight from New to Done, outside every listed status: nothing;
        // two issues of two people; five issues of two people. Each delivered count is withheld.
        (FakePerson Person, int Issues)[][] deliveries = [[], [(People.One, 1), (People.Two, 1)], [(People.One, 3), (People.Two, 2)]];
        var read = new List<(string Report, string Views)>();
        foreach (var delivered in deliveries.Append([(People.One, 1), (People.Two, 1), (People.Three, 1)]))
        {
            await using var pipeline = new PipelineHarness();
            pipeline.Jira.AddSprint41();
            pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
            foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 1)))
            {
                pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                    .Moved(Local(2026, 1, 7, 10), Workflow.Development)
                    .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review);
            }

            var key = 100;
            foreach (var person in delivered.SelectMany(delivery => Enumerable.Repeat(delivery.Person, delivery.Issues)))
            {
                pipeline.Jira.AddIssue($"FAKE-{++key}", Local(2026, 1, 6, 9)).AssignedTo(person).Moved(Local(2026, 1, 7, 9), Workflow.Done);
            }

            pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
            var views = await pipeline.QueryAsync<string>("SELECT name FROM derived_object WHERE kind = 'view' ORDER BY name;");
            read.Add((await pipeline.ReadReportAsync(41), await QueryAsync(pipeline, [.. views.Select(view => $"SELECT * FROM {view};")])));
        }

        // The delivered count and the cycle time table are withheld as they were before brief visits existed,
        // and the brief visit count is shown: three people are behind it.
        await Assert.That(read[0].Report).Contains("\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n");
        await Assert.That(read[0].Report).EndsWith("\n- Fake Development: 3 issues had a visit shorter than 5 minutes\n");
        await Assert.That(read[0].Views).Contains(ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 3 | 0 |\n");
        await Assert.That(read[0].Views).Contains("| sprint_id | team_suppressed | delivered_suppressed |\n| --- | --- | --- |\n| 41 | 0 | 1 |\n");

        // Whatever was delivered, the report and every metric view are the same: with the brief visit rows
        // present, nothing tells one withheld delivered count from another, or from none.
        foreach (var other in read[1..deliveries.Length])
        {
            await Assert.That(other.Views).IsEqualTo(read[0].Views);
            await Assert.That(other.Report).IsEqualTo(read[0].Report);
        }

        // The comparison can fail: three people delivered, and the count is shown beside the same line.
        await Assert.That(read[^1].Report).Contains("\nDelivered issues: 3\n");
        await Assert.That(read[^1].Report).EndsWith("\n- Fake Development: 3 issues had a visit shorter than 5 minutes\n");
        await Assert.That(read[^1].Views).Contains("| sprint_id | team_suppressed | delivered_suppressed |\n| --- | --- | --- |\n| 41 | 0 | 0 |\n");
    }

    [Test]
    [Arguments(5, 4, "1,3,5", DevelopmentWithheld, Withheld, ReviewWithheld, Withheld)]
    [Arguments(5, 4, "1,3,5,6", "- Fake Development: 4 issues had a visit shorter than 5 minutes", "4 | 0", ReviewWithheld, Withheld)]
    [Arguments(5, 2, "1,2", DevelopmentWithheld, Withheld, ReviewOfThree, "3 | 0")]
    [Arguments(5, 2, "1,3", "- Fake Development: 2 issues had a visit shorter than 5 minutes", "2 | 0", ReviewOfThree, "3 | 0")]
    [Arguments(5, 1, "", DevelopmentWithheld, Withheld, ReviewOfThree, "3 | 0")]
    [Arguments(5, 1, "8", DevelopmentWithheld, Withheld, ReviewOfThree, "3 | 0")]
    [Arguments(5, 1, "1", "- Fake Development: 1 issue had a visit shorter than 5 minutes", "1 | 0", ReviewOfThree, "3 | 0")]
    [Arguments(5, 0, "", "- Fake Development: 0 issues had a visit shorter than 5 minutes", "0 | 0", ReviewOfThree, "3 | 0")]
    [Arguments(5, 0, "8", "- Fake Development: 1 issue had a visit shorter than 5 minutes", "1 | 0", ReviewOfThree, "3 | 0")]
    public async Task The_count_is_withheld_and_shown_by_the_configured_number_of_assignees_behind_a_figure_and_with_zero_a_count_of_nothing_is_0(
        int team, int assigneesBehindAFigure, string briefVisits, string developmentLine, string development, string reviewLine, string review)
    {
        // Three people are behind the Review count. With four needed it is withheld; with two, one person's
        // two issues are not enough; with one, a count of nothing and a count of unassigned issues have nobody
        // behind them; with zero, nobody is needed, and nothing reads 0.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, Numbers(briefVisits));
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n" + developmentLine + "\n" + reviewLine + "\n");
        await Assert.That(await QueryAsync(pipeline, ViewOfSprint41Sql)).IsEqualTo(
            ViewHead
            + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | {development} |\n"
            + $"| 41 | {Workflow.Review.Id} | Fake Review | 5 | 2 | {review} |\n");
    }

    [Test]
    public async Task With_a_team_of_six_configured_a_sprint_of_five_people_has_no_brief_visit_lines_and_no_rows()
    {
        // Three people are behind each count, enough for a figure: the team check alone withholds the sprints.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, 1, 3, 5);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 6, assigneesBehindAFigure: 3);

        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\n## Time in status\n\nsuppressed\n");
        await Assert.That(report).EndsWith("\n- Issues delivered between sprints, in the whole store: suppressed\n");
        await Assert.That(report).DoesNotContain("a visit shorter than");
        await Assert.That(await QueryAsync(pipeline, ViewSql)).IsEqualTo(ViewHead);
    }

    [Test]
    public async Task With_both_numbers_zero_every_sprint_has_its_rows_every_count_is_shown_and_a_count_of_nothing_is_0()
    {
        // Sprint 41 has two people and an issue nobody is assigned to: one person's issue and the unassigned
        // one had a brief visit to Development. Sprint 42 has one issue, unassigned, caught up through
        // Development. Nobody had a brief visit to Review. The default numbers withhold both sprints whole.
        await using var byDefault = new PipelineHarness();
        await using var off = SuppressionOff();
        foreach (var pipeline in new[] { byDefault, off })
        {
            pipeline.Jira.AddSprints41And42();
            FridayEvening(pipeline.Jira, "FAKE-1", People.One, briefly: true);
            FridayEvening(pipeline.Jira, "FAKE-2", People.Two, briefly: false);
            FridayEvening(pipeline.Jira, "FAKE-3", assignee: null, briefly: true);
            CaughtUpInSprint42(pipeline.Jira, "FAKE-4", assignee: null);
            pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };
            await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"], ["report", "--sprint", "42"]);
        }

        foreach (var sprintId in new[] { 41, 42 })
        {
            await Assert.That(await byDefault.ReadReportAsync(sprintId)).Contains("\n## Time in status\n\nsuppressed\n");
            await Assert.That(await byDefault.ReadReportAsync(sprintId)).DoesNotContain("a visit shorter than");
        }

        await Assert.That(await QueryAsync(byDefault, ViewSql)).IsEqualTo(ViewHead);

        await Assert.That(await off.ReadReportAsync(41)).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: 0
            - Fake Development: 2 issues had a visit shorter than 5 minutes
            - Fake Review: 0 issues had a visit shorter than 2 minutes

            """.ReplaceLineEndings("\n"));
        await Assert.That(await off.ReadReportAsync(42)).EndsWith(
            """
            - Issues delivered between sprints, in the whole store: 0
            - Fake Development: 1 issue had a visit shorter than 5 minutes
            - Fake Review: 0 issues had a visit shorter than 2 minutes

            """.ReplaceLineEndings("\n"));
        await Assert.That(await QueryAsync(off, ViewSql)).IsEqualTo(
            ViewHead
            + """
            | 41 | 600004 | Fake Development | 4 | 5 | 2 | 0 |
            | 41 | 600005 | Fake Review | 5 | 2 | 0 | 0 |
            | 42 | 600004 | Fake Development | 4 | 5 | 1 | 0 |
            | 42 | 600005 | Fake Review | 5 | 2 | 0 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    [Arguments("4", 1)]
    [Arguments("NULL", 0)]
    [Arguments("NULL", 1)]
    public async Task The_report_prints_no_count_from_a_row_the_view_marks_suppressed_and_none_from_a_row_without_one(string count, int suppressed)
    {
        // The flag decides, as it does for every table: a view that set the flag and still let a count through
        // (a fault, or a view changed by hand) must not get it into the report, and a row with no count and
        // no flag must not read as a count of nothing.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, 1, 3, 5);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync(
            $"""
            DROP VIEW brief_visits_per_sprint;
            CREATE VIEW brief_visits_per_sprint AS
            SELECT '41' AS sprint_id, '600004' AS status_id, 'Fake Development' AS status_name, 4 AS board_position, 5.0 AS threshold_minutes,
                   {count} AS brief_visit_issue_count, {suppressed} AS suppressed;
            """);

        await pipeline.RunAllAsync(["report", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith(
            "\n- Issues delivered between sprints, in the whole store: suppressed\n" + DevelopmentWithheld + "\n");
    }

    [Test]
    [Arguments("SELECT sprint_id, status_id, issue_id FROM time_in_status_issue;")]
    [Arguments("SELECT issue_id, sprint_id, status_id FROM brief_visit;")]
    [Arguments("SELECT b.status_name, (SELECT COUNT(*) FROM brief_visit v WHERE v.sprint_id = b.sprint_id AND v.status_id = b.status_id) FROM brief_visits_per_sprint b;")]
    [Arguments("SELECT b.status_name, COUNT(*) FROM brief_visits_per_sprint b JOIN time_in_status_issue t ON t.sprint_id = b.sprint_id AND t.status_id = b.status_id GROUP BY b.status_name;")]
    public async Task Query_refuses_to_read_the_brief_visit_table_or_the_internal_view_of_the_issues_in_a_row_by_name(string sql)
    {
        // Both hold a row per issue, and a count of them is the count the view withholds.
        await using var pipeline = new PipelineHarness();
        AddStore(pipeline, 1, 3);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunAsync("query", sql);

        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString();
        await Assert.That(error).StartsWith("This query reads only the metric views (");
        await Assert.That(error).Contains("brief_visits_per_sprint, ");
        await Assert.That(error).Contains("), and it asked for something else: ");
        await Assert.That(error.TrimEnd().Split('\n').Length).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
    }

    [Test]
    public async Task No_account_id_name_email_or_summary_reaches_the_database_the_report_or_the_console_with_a_brief_visit_count_shown()
    {
        // PrivacyTests' scenario and scan, with a status listed and suppression off, so that the line and
        // the view's row are written: one more issue, in the sprint, caught up through Development.
        await using var pipeline = SuppressionOff();
        PrivacyScan.AddScenario(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-7", Local(2026, 1, 5, 9)).AssignedTo(People.Two).InSprints(41)
            .Moved(Local(2026, 1, 7, 10), Workflow.Development, by: People.Three)
            .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Review);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };

        await pipeline.RunAllAsync(["run", "--sprint", "41"], ["query", ViewSql]);

        await Assert.That(await pipeline.ReadReportAsync(41)).EndsWith("\n- Fake Development: 1 issue had a visit shorter than 5 minutes\n");
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).Contains(
            ViewHead + $"| 41 | {Workflow.Development.Id} | Fake Development | 4 | 5 | 1 | 0 |\n");
        await PrivacyScan.AssertNothingForbiddenWasWrittenAsync(pipeline);
    }

    /// <summary>The issue numbers a test's argument lists, as in <c>"1,3,8"</c>; none for an empty string.</summary>
    private static int[] Numbers(string list) => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)];

    /// <summary>
    /// The store most tests are set in, with Development listed at five minutes and Review at two. Sprints 41
    /// and 42 have a team of five, whose own issues stay in New.
    /// <list type="bullet">
    /// <item>Sprint 41, Development: the eight issues of <see cref="DevelopmentRow"/>, each
    /// <see cref="FridayEvening"/>. Those numbered in <paramref name="briefVisits"/> had a brief visit.</item>
    /// <item>Sprint 41, Review: those eight, which stayed there over the weekend and on, and three more
    /// people's issues that went from New through Review in twenty seconds. So three people are behind a count
    /// of three.</item>
    /// <item>Sprint 42: three people's issues, each <see cref="CaughtUpInSprint42"/>. So three people are behind
    /// a Development count of three, and nobody had a brief visit to Review.</item>
    /// </list>
    /// </summary>
    private static void AddStore(PipelineHarness pipeline, params int[] briefVisits)
    {
        var jira = pipeline.Jira;
        jira.AddSprints41And42();
        jira.AddIdleTeam(Local(2026, 1, 2, 9), 41, 42);
        foreach (var (number, assignee) in DevelopmentRow)
        {
            FridayEvening(jira, $"FAKE-{number}", assignee, briefly: briefVisits.Contains(number));
        }

        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 21)))
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 7, 10), Workflow.Review)
                .Moved(Local(2026, 1, 7, 10).AddSeconds(20), Workflow.Done);
            CaughtUpInSprint42(jira, $"FAKE-{number + 10}", person);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Review.Id] = 2 };
    }

    /// <summary>
    /// An issue of sprint 41 that entered Development twenty seconds before midnight on Friday 9 January and
    /// left it for Review at midnight (<paramref name="briefly"/>: a visit of twenty seconds) or at ten on
    /// Saturday morning (a visit of ten hours), and was done on Tuesday the 13th at 10:00. Either way it spent
    /// twenty seconds of business time in Development and the same business time in Review, so nothing but the
    /// length of its visit differs. A null <paramref name="assignee"/> leaves it unassigned.
    /// </summary>
    private static FakeIssue FridayEvening(FakeJira jira, string key, FakePerson? assignee, bool briefly)
    {
        var midnight = Local(2026, 1, 10);
        var issue = jira.AddIssue(key, Local(2026, 1, 5, 9)).InSprints(41)
            .Moved(midnight.AddSeconds(-20), Workflow.Development)
            .Moved(briefly ? midnight : midnight.AddHours(10), Workflow.Review)
            .Moved(Local(2026, 1, 13, 10), Workflow.Done);
        return assignee is null ? issue : issue.AssignedTo(assignee);
    }

    /// <summary>
    /// An issue of sprint 42 that was caught up in one go on Wednesday 21 January at 10:00: twenty seconds in
    /// Development, then Review for two days before it was done. A null <paramref name="assignee"/> leaves it unassigned.
    /// </summary>
    private static FakeIssue CaughtUpInSprint42(FakeJira jira, string key, FakePerson? assignee)
    {
        var caughtUp = Local(2026, 1, 21, 10);
        var issue = jira.AddIssue(key, Local(2026, 1, 19, 11)).InSprints(42)
            .Moved(caughtUp, Workflow.Development)
            .Moved(caughtUp.AddSeconds(20), Workflow.Review)
            .Moved(Local(2026, 1, 23, 10), Workflow.Done);
        return assignee is null ? issue : issue.AssignedTo(assignee);
    }
}
