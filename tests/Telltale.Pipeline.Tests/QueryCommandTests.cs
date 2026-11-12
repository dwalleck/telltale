using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>query</c> runs the owner's SQL over the metric views and prints the rows (story 45; owner decision D48).
/// It is the one path for ad hoc SQL on which the suppression rule holds: the query reads the views' rows and
/// nothing else in the store. Sprint 41 has a team of four and sprint 42 a team of five.
/// </summary>
public class QueryCommandTests
{
    [Test]
    public async Task Query_prints_the_rows_of_a_metric_view_as_a_markdown_table_and_a_withheld_figure_as_null()
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);

        var exit = await pipeline.RunAsync("query", "SELECT sprint_id, sprint_name, delivered_count, suppressed FROM delivered_per_sprint ORDER BY sprint_id;");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | sprint_id | sprint_name | delivered_count | suppressed |
            | --- | --- | --- | --- |
            | 41 | Fake Sprint 41 | NULL | 1 |
            | 42 | Fake \*Sprint\* 42 | 5 | 0 |

            """);
    }

    [Test]
    public async Task Query_prints_numbers_in_full_and_the_head_of_a_result_without_rows()
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);

        var figures = await pipeline.RunAsync("query", "SELECT point_bucket, issue_count, median_business_days / 3 AS third FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '42';");
        var nothing = await pipeline.RunAsync("query", "SELECT sprint_id, suppressed FROM sprint_team WHERE sprint_id = '99';");

        await Assert.That(figures).IsEqualTo(0);
        await Assert.That(nothing).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | point_bucket | issue_count | third |
            | --- | --- | --- |
            | 3 | 5 | 0.6666666666666666 |
            | sprint_id | suppressed |
            | --- | --- |

            """);
    }

    [Test]
    public async Task Query_reads_its_sql_from_a_file()
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);
        var file = Path.Combine(pipeline.Directory, "question.sql");
        await File.WriteAllTextAsync(file, "-- How many sprints are short of five?\nSELECT COUNT(*) AS small_sprints\nFROM sprint_team\nWHERE suppressed = 1;\n");

        var exit = await pipeline.RunAsync("query", "--file", file);

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo("| small_sprints |\n| --- |\n| 1 |\n");
    }

    [Test]
    [Arguments("SELECT issue_id, assignee_hash FROM issue;")]
    [Arguments("SELECT * FROM issue_cycle;")]
    [Arguments("SELECT sql FROM sqlite_master;")]
    [Arguments("SELECT d.sprint_id, (SELECT COUNT(DISTINCT assignee_hash) FROM issue) FROM delivered_per_sprint d;")]
    [Arguments("DELETE FROM delivered_per_sprint;")]
    [Arguments("DROP VIEW sprint_team;")]
    [Arguments("SELECT sprint_id FROM delivered_per_sprint; DELETE FROM issue;")]
    [Arguments("PRAGMA table_info(issue);")]
    [Arguments("ATTACH DATABASE 'other.db' AS other;")]
    public async Task Query_refuses_sql_that_reads_or_changes_anything_but_the_metric_views_with_a_plain_message(string sql)
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);
        var storeBefore = await pipeline.ReadStoreContentAsync();

        var exit = await pipeline.RunAsync("query", sql);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).StartsWith("This query reads only the metric views (");
        await Assert.That(pipeline.Error.ToString().TrimEnd().Split('\n').Length).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(storeBefore);
        await Assert.That(File.Exists(Path.Combine(pipeline.Directory, "other.db"))).IsFalse();
    }

    [Test]
    public async Task Query_says_plainly_what_is_wrong_with_sql_that_cannot_be_run()
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);

        var exit = await pipeline.RunAsync("query", "SELECT sprint_id FROM delivered_per_sprint WHERE;");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The query could not be run: SQLite Error 1: 'near \";\": syntax error'.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
    }

    [Test]
    public async Task Query_is_refused_like_a_report_when_the_derived_data_is_missing_stale_or_from_other_settings()
    {
        const string Sql = "SELECT sprint_id FROM delivered_per_sprint;";
        await using var pipeline = new PipelineHarness();
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        pipeline.Out.GetStringBuilder().Clear();
        var unbuilt = await pipeline.RunAsync("query", Sql);
        await pipeline.RunAllAsync(["derive"]);

        pipeline.Jira.Issues[0].Moved(Local(2026, 2, 3, 9), Workflow.Shipped);
        await pipeline.RunAllAsync(["sync"]);
        var stale = await pipeline.RunAsync("query", Sql);
        await pipeline.RunAllAsync(["derive"]);

        pipeline.Config["timeZone"] = "Europe/Berlin";
        var otherTimeZone = await pipeline.RunAsync("query", Sql);
        pipeline.Config["timeZone"] = ZoneId;
        pipeline.Config["jira"]!["boardId"] = FakeJira.BoardId + 1;
        var otherBoard = await pipeline.RunAsync("query", Sql);

        await Assert.That(new[] { unbuilt, stale, otherTimeZone, otherBoard }).IsEquivalentTo(new[] { 1, 1, 1, 1 });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "The store has no derived data yet. Run derive first.\n"
            + "The raw data changed since the derived data was built. Run derive first.\n"
            + "The time zone or the holidays in the configuration changed since the derived data was built. Run derive first.\n"
            + "jira.boardId in the configuration file changed since the store was last filled, so the store holds what the old setting asked for. Run backfill.\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
    }

    [Test]
    public async Task Query_needs_its_sql_as_an_argument_or_in_a_file_and_not_both()
    {
        await using var pipeline = new PipelineHarness();
        await FillAndDeriveAsync(pipeline);
        var file = Path.Combine(pipeline.Directory, "question.sql");
        await File.WriteAllTextAsync(file, "SELECT 1;");

        var neither = await pipeline.RunAsync("query");
        var both = await pipeline.RunAsync("query", "SELECT 1;", "--file", file);
        var missing = await pipeline.RunAsync("query", "--file", Path.Combine(pipeline.Directory, "no-such.sql"));

        await Assert.That(new[] { neither, both, missing }).IsEquivalentTo(new[] { 1, 1, 1 });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Give the SQL to run, either as the argument or in a file named with --file, and not both.\n"
            + "Give the SQL to run, either as the argument or in a file named with --file, and not both.\n"
            + $"The file {Path.Combine(pipeline.Directory, "no-such.sql")} does not exist.\n");
    }

    [Test]
    public async Task The_help_of_query_says_that_opening_the_database_file_elsewhere_bypasses_the_suppression_rule()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunAsync("query", "--help");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings(" ")).Contains("Opening the database file in another program bypasses the suppression rule");
    }

    /// <summary>Fills the store from the board of <see cref="AddBoard"/> and derives, leaving standard output empty for the query.</summary>
    private static async Task FillAndDeriveAsync(PipelineHarness pipeline)
    {
        AddBoard(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();
    }

    /// <summary>
    /// Sprint 41 (5 to 19 January 2026) with four people's delivered 3-point issues, and sprint 42 (19 January to
    /// 2 February), whose name holds markdown, with five people's: each two days in Development.
    /// </summary>
    private static void AddBoard(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake *Sprint* 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
        foreach (var (person, number) in People.Team.Select((person, index) => (person, index + 1)))
        {
            if (number < 5)
            {
                jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).Estimated(3).InSprints(41)
                    .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                    .Moved(Local(2026, 1, 8, 9), Workflow.Done);
            }

            jira.AddIssue($"FAKE-1{number}", Local(2026, 1, 19, 11)).AssignedTo(person).Estimated(3).InSprints(42)
                .Moved(Local(2026, 1, 20, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        }
    }
}
