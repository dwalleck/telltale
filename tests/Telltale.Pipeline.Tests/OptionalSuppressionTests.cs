using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The minimum group size's two numbers are a setting (owner decisions D64 to D66): <c>minimumGroupSize</c>,
/// with <c>team</c> and <c>assigneesBehindAFigure</c>. Absent, they are five and three, and
/// <see cref="SuppressionTests"/> says what is then withheld. A number of zero turns that check off.
/// Sprint 41 runs from 5 to 19 January 2026 and has four assignees in every test here: a sprint the default
/// numbers withhold whole.
/// </summary>
public class OptionalSuppressionTests
{
    /// <summary>The sprint's figures as the report shows them when nothing is withheld, from the delivered count to the method note.</summary>
    private const string EveryFigureOfSprint41 =
        "\nDelivered issues: 4\n"
        + "\n## Cycle time by point bucket\n\n"
        + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
        + "| --- | ---: | ---: | ---: | ---: | ---: |\n"
        + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
        + "\n## Time in status\n"
        + "\n| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
        + "| Fake New | 4 | 1.0 | 4.0 |\n"
        + "| Fake Development | 4 | 2.0 | 8.0 |\n"
        + "\n## Method\n";

    private const string DeliveredSql = "SELECT delivered_count, suppressed, team_suppressed FROM delivered_per_sprint WHERE sprint_id = '41';";

    private const string CycleTimeSql =
        "SELECT point_bucket, issue_count, median_business_days, p85_business_days, median_calendar_days, p85_calendar_days, suppressed FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41';";

    private const string TimeInStatusSql =
        "SELECT status_name, issue_count, median_business_days, total_business_days, suppressed FROM time_in_status_per_sprint WHERE sprint_id = '41' ORDER BY board_position;";

    [Test]
    public async Task With_both_numbers_zero_a_sprint_of_four_assignees_shows_its_delivered_count_and_both_tables_in_the_report_and_through_query()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(["query", DeliveredSql], ["query", "SELECT team, assignees_behind_a_figure FROM minimum_group_size;"], ["query", CycleTimeSql], ["query", TimeInStatusSql]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(EveryFigureOfSprint41);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- |
            | 4 | 0 | 0 |
            | team | assignees_behind_a_figure |
            | --- | --- |
            | 0 | 0 |
            | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- |
            | 3 | 4 | 2 | 2 | 2 | 2 | 0 |
            | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | Fake New | 4 | 1 | 4 | 0 |
            | Fake Development | 4 | 2 | 8 | 0 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task After_the_setting_changes_in_either_direction_report_and_query_are_refused_until_derive_runs_and_nothing_is_fetched_again()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourAssignees(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var requestsDuringBackfill = pipeline.Jira.Handler.Requests.Count;
        var withheld = await pipeline.ReadReportAsync(41);
        pipeline.Out.GetStringBuilder().Clear();

        // Turned off: the report on disk stays as it was until derive has run.
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);
        var reportAfterTurningOff = await pipeline.RunAsync("report", "--sprint", "41");
        var queryAfterTurningOff = await pipeline.RunAsync("query", DeliveredSql);
        var stillWithheld = await pipeline.ReadReportAsync(41);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        var shown = await pipeline.ReadReportAsync(41);

        // Turned back on, by taking the setting out.
        pipeline.Config.Remove("minimumGroupSize");
        var reportAfterTurningOn = await pipeline.RunAsync("report", "--sprint", "41");
        var queryAfterTurningOn = await pipeline.RunAsync("query", DeliveredSql);
        var stillShown = await pipeline.ReadReportAsync(41);
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);

        await Assert.That(new[] { reportAfterTurningOff, queryAfterTurningOff, reportAfterTurningOn, queryAfterTurningOn }).IsEquivalentTo(new[] { 1, 1, 1, 1 });
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            string.Concat(Enumerable.Repeat("minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.\n", 4)));
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("delivered_count");
        await Assert.That(withheld).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(stillWithheld).IsEqualTo(withheld);
        await Assert.That(shown).Contains(EveryFigureOfSprint41);
        await Assert.That(stillShown).IsEqualTo(shown);
        await Assert.That(await pipeline.ReadReportAsync(41)).IsEqualTo(withheld);
        await Assert.That(requestsDuringBackfill).IsGreaterThan(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsDuringBackfill);
    }

    [Test]
    public async Task Without_the_setting_and_with_it_written_as_five_and_three_the_report_and_every_view_are_the_same_and_the_sprint_is_withheld()
    {
        await using var absent = new PipelineHarness();
        await using var written = new PipelineHarness();
        written.Config["minimumGroupSize"] = MinimumGroupSize(team: 5, assigneesBehindAFigure: 3);
        foreach (var pipeline in new[] { absent, written })
        {
            AddSprint41WithFourAssignees(pipeline.Jira);
            await pipeline.RunAllAsync(["run", "--sprint", "41"]);
            pipeline.Out.GetStringBuilder().Clear();
            await pipeline.RunAllAsync(["query", "SELECT team, assignees_behind_a_figure FROM minimum_group_size;"], ["query", DeliveredSql], ["query", CycleTimeSql], ["query", TimeInStatusSql]);
        }

        // What the rule withholds of this sprint today (SuppressionTests), through query.
        await Assert.That(absent.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | team | assignees_behind_a_figure |
            | --- | --- |
            | 5 | 3 |
            | delivered_count | suppressed | team_suppressed |
            | --- | --- | --- |
            | NULL | 1 | 1 |
            | point_bucket | issue_count | median_business_days | p85_business_days | median_calendar_days | p85_calendar_days | suppressed |
            | --- | --- | --- | --- | --- | --- | --- |
            | NULL | NULL | NULL | NULL | NULL | NULL | 1 |
            | status_name | issue_count | median_business_days | total_business_days | suppressed |
            | --- | --- | --- | --- | --- |
            | NULL | NULL | NULL | NULL | 1 |

            """.ReplaceLineEndings("\n"));
        await Assert.That(await absent.ReadReportAsync(41)).Contains(
            "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n\nsuppressed\n\n## Method\n");

        // Both runs read the same clock, so the as-of time is the same too; it is left out because "identical" never includes it.
        await Assert.That(written.Out.ToString()).IsEqualTo(absent.Out.ToString());
        await Assert.That(await written.ReadReportBytesAsync(41, withoutAsOf: true)).IsEqualTo(await absent.ReadReportBytesAsync(41, withoutAsOf: true));
        await Assert.That(await written.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(await absent.ReadStoreContentAsync(withoutAsOf: true));
    }

    [Test]
    [Arguments("baseline_cycle_time_per_bucket")]
    [Arguments("baseline_sprint")]
    [Arguments("baseline_time_in_status")]
    [Arguments("cycle_time_per_sprint_and_bucket")]
    [Arguments("data_quality")]
    [Arguments("delivered_per_sprint")]
    [Arguments("minimum_group_size")]
    [Arguments("sprint_team")]
    [Arguments("time_in_status_per_sprint")]
    public async Task A_metric_view_has_the_same_columns_with_both_numbers_zero_as_with_the_default_numbers(string view)
    {
        await using var byDefault = new PipelineHarness();
        await using var off = new PipelineHarness();
        off.Config["minimumGroupSize"] = MinimumGroupSize(team: 0, assigneesBehindAFigure: 0);
        foreach (var pipeline in new[] { byDefault, off })
        {
            AddSprint41WithFourAssignees(pipeline.Jira);
            await pipeline.RunAllAsync(["backfill"], ["derive"]);
            pipeline.Out.GetStringBuilder().Clear();

            // A result without rows is printed as its head: the view's columns, in order.
            await pipeline.RunAllAsync(["query", $"SELECT * FROM {view} LIMIT 0;"]);
        }

        await Assert.That(byDefault.Out.ToString()).StartsWith("| ");
        await Assert.That(off.Out.ToString()).IsEqualTo(byDefault.Out.ToString());
    }

    [Test]
    [Arguments(-1, 0, "team")]
    [Arguments(0, -1, "assigneesBehindAFigure")]
    [Arguments(5, -3, "assigneesBehindAFigure")]
    public async Task A_negative_number_is_refused_and_the_message_names_the_file_and_the_setting(int team, int assigneesBehindAFigure, string refused)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.{refused} must be written as a whole number of zero or more.\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    [Test]
    [Arguments(4, 2, "every figure is shown")]
    [Arguments(4, 3, "the bucket figures are withheld")]
    [Arguments(5, 2, "the sprint is withheld")]
    public async Task Each_of_the_two_numbers_is_applied_to_its_own_check(int team, int assigneesBehindAFigure, string expected)
    {
        // Four assignees in the sprint, two behind each point bucket, four behind each status row and the
        // delivered count. So a team number of four passes the sprint and five does not, and a figure number
        // of two passes a bucket and three does not: a check that read the other number gives another report.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, points, number) in new[] { (People.One, 3, 1), (People.Two, 3, 2), (People.Three, 5, 3), (People.Four, 5, 4) })
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).Estimated(points).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }

        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(team, assigneesBehindAFigure);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        const string CycleTimeHead =
            "\n## Cycle time by point bucket\n\n"
            + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
            + "| --- | ---: | ---: | ---: | ---: | ---: |\n";
        const string TimeInStatus =
            "\n## Time in status\n"
            + "\n| Status | Issues | Median (business days) | Total (business days) |\n| --- | ---: | ---: | ---: |\n"
            + "| Fake New | 4 | 1.0 | 4.0 |\n"
            + "| Fake Development | 4 | 2.0 | 8.0 |\n"
            + "\n## Method\n";
        var figures = expected switch
        {
            "every figure is shown" =>
                "\nDelivered issues: 4\n" + CycleTimeHead + "| 3 | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n| 5 | 2 | 2.0 | 2.0 | 2.0 | 2.0 |\n" + TimeInStatus,
            "the bucket figures are withheld" =>
                "\nDelivered issues: 4\n" + CycleTimeHead
                + "| 3 | 2 | suppressed | suppressed | suppressed | suppressed |\n| 5 | 2 | suppressed | suppressed | suppressed | suppressed |\n" + TimeInStatus,
            _ => "\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n\nsuppressed\n\n## Method\n",
        };
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(figures);
    }
}
