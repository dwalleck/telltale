using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A configuration file whose <c>minimumGroupSize</c> cannot be right is refused when it is loaded, before
/// anything is fetched or stored, with a message that names the file and the setting (owner decision D64). A typo must not leave suppression in another state than the owner meant: a missing number, a quoted
/// one, <c>false</c> or <c>null</c> could each be read as "off" or as the default. A negative number is in
/// <see cref="OptionalSuppressionTests"/>. Any two whole numbers of zero or more are accepted, also more
/// assignees behind a figure than the team minimum: <see cref="OtherMinimumGroupSizeNumbersTests"/>.
/// </summary>
public class RefusedMinimumGroupSizeTests
{
    [Test]
    [Arguments("""{ "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 5 }""", "assigneesBehindAFigure")]
    public async Task The_setting_with_one_of_its_two_numbers_missing_is_refused(string setting, string missing)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["minimumGroupSize"] = JsonNode.Parse(setting);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.{missing} is missing: when minimumGroupSize is present, both of its numbers are required.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("""{ "team": 4.5, "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": 2.5 }""", "assigneesBehindAFigure")]
    [Arguments("""{ "team": "5", "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": "3" }""", "assigneesBehindAFigure")]
    [Arguments("""{ "team": null, "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": null }""", "assigneesBehindAFigure")]
    [Arguments("""{ "team": false, "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": true }""", "assigneesBehindAFigure")]
    [Arguments("""{ "team": 5.0, "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 1e1, "assigneesBehindAFigure": 3 }""", "team")]
    [Arguments("""{ "team": 3000000000, "assigneesBehindAFigure": 3 }""", "team")]
    public async Task A_number_that_is_not_written_as_a_whole_number_that_fits_an_integer_is_refused(string setting, string refused)
    {
        // Fractional, quoted, null or a boolean; and also 5.0, 1e1 and three thousand million, which are
        // whole numbers but not written as one the tool reads.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["minimumGroupSize"] = JsonNode.Parse(setting);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.{refused} must be written as a whole number of zero or more.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("null")]
    [Arguments("false")]
    [Arguments("0")]
    [Arguments("[0, 0]")]
    public async Task The_setting_written_as_anything_but_an_object_is_refused(string setting)
    {
        // Written by someone who meant "no minimum". Leaving the setting out means five and three, so none
        // of these may load as if it had been left out.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["minimumGroupSize"] = JsonNode.Parse(setting);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize must be an object with the two numbers team and assigneesBehindAFigure; leave the setting out for five and three.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task The_setting_with_a_key_the_tool_does_not_know_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["minimumGroupSize"] = new JsonObject { ["team"] = 0, ["assigneesBehindAFigure"] = 0, ["enabled"] = false };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.enabled is not a setting the tool knows.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("team")]
    [Arguments("assigneesBehindAFigure")]
    public async Task One_of_the_two_numbers_written_twice_is_refused(string number)
    {
        // As for every setting: with two values, the reader of the file cannot tell which one is in force.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["minimumGroupSize"] = new JsonObject { ["team"] = 0, ["assigneesBehindAFigure"] = 0 };
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace($"\"{number}\":0", $"\"{number}\":0,\"{number}\":5", StringComparison.Ordinal);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.{number} is written twice.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task With_a_team_of_zero_any_number_behind_a_figure_is_accepted_and_only_the_figure_check_applies()
    {
        // Five people in the sprint, each with one delivered issue; the 3-point bucket has four people behind
        // it and the 5-point bucket one. With no team check and four assignees needed behind a figure, the
        // sprint is reported, the first bucket shows its figures and the second keeps only its count.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        foreach (var (person, number) in People.Team.Take(5).Select((person, index) => (person, index + 1)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).Estimated(number == 5 ? 5 : 3).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }

        pipeline.Config["minimumGroupSize"] = new JsonObject { ["team"] = 0, ["assigneesBehindAFigure"] = 4 };

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        pipeline.Out.GetStringBuilder().Clear();
        await pipeline.RunAllAsync(
            ["query", "SELECT team, assignees_behind_a_figure FROM minimum_group_size;"],
            ["query", "SELECT point_bucket, issue_count, median_business_days, suppressed FROM cycle_time_per_sprint_and_bucket WHERE sprint_id = '41' ORDER BY point_bucket;"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            "\nDelivered issues: 5\n"
            + "\n## Cycle time by point bucket\n\n"
            + "| Point bucket | Issues with a cycle time | Median (business days) | P85 (business days) | Median (calendar days) | P85 (calendar days) |\n"
            + "| --- | ---: | ---: | ---: | ---: | ---: |\n"
            + "| 3 | 4 | 2.0 | 2.0 | 2.0 | 2.0 |\n"
            + "| 5 | 1 | suppressed | suppressed | suppressed | suppressed |\n");
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            """
            | team | assignees_behind_a_figure |
            | --- | --- |
            | 0 | 4 |
            | point_bucket | issue_count | median_business_days | suppressed |
            | --- | --- | --- | --- |
            | 3 | 4 | 2 | 0 |
            | 5 | 1 | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    /// <summary>The file was refused as it was loaded: the command failed, no request went to Jira, and no database was made.</summary>
    private static async Task AssertNothingWasFetchedOrStoredAsync(PipelineHarness pipeline, int exit)
    {
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }
}
