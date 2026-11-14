using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A configuration file whose <c>briefVisitThresholds</c> cannot be right is refused when it is loaded, before
/// anything is fetched or stored, with a message that names the file and the setting (owner decision D58; user
/// stories 5 to 7 of the brief status visits spec). A mistyped status or threshold must not show up only as a
/// footer line that never appears. No message repeats a status ID or a value: either could be a secret pasted
/// into the wrong place (owner decision D43). What is accepted is in <see cref="AcceptedBriefVisitThresholdsTests"/>.
/// </summary>
public class RefusedBriefVisitThresholdsTests
{
    /// <summary>A status ID in Jira's form that the harness's <c>statusRoles</c> does not list.</summary>
    private const string StatusWithoutARole = "699999";

    [Test]
    [Arguments(StatusWithoutARole)]
    [Arguments("synthetic-pasted-token")]
    [Arguments("")]
    public async Task A_status_that_is_not_in_the_status_roles_is_refused_and_not_repeated(string listed)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [listed] = 2 };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task A_status_the_owner_removed_from_the_status_roles_while_it_is_still_listed_is_refused_like_any_unknown_status()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.QE.Id] = 2 };
        pipeline.Config["statusRoles"]!.AsObject().Remove(Workflow.QE.Id);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task A_status_with_the_done_role_is_refused_and_not_repeated()
    {
        // A done status has no time in status row, so its line could never appear.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5, [Workflow.Shipped.Id] = 2 };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: a status with the role done cannot be listed, and one is. A done status has no time in status row.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("0")]
    [Arguments("0.0")]
    [Arguments("-0.0")]
    [Arguments("-5")]
    [Arguments("-0.5")]
    [Arguments("\"5\"")]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("false")]
    [Arguments("\"five\"")]
    [Arguments("\"synthetic-pasted-token\"")]
    [Arguments("[5]")]
    [Arguments("{ \"minutes\": 5 }")]
    public async Task A_threshold_that_is_not_written_as_a_number_greater_than_zero_is_refused_and_not_repeated(string threshold)
    {
        // Zero or less; and a quoted number, null, a boolean, text, a list and an object, which are not numbers.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = JsonNode.Parse($$"""{ "{{Workflow.Review.Id}}": 2, "{{Workflow.Development.Id}}": {{threshold}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("1e300")]
    [Arguments("1e999")]
    [Arguments("15372286729")]
    public async Task A_threshold_too_large_to_be_a_length_of_time_is_refused(string threshold)
    {
        // The longest time the tool can hold is a little over 15,372,286,728 minutes, some 29,000 years.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = JsonNode.Parse($$"""{ "{{Workflow.Development.Id}}": {{threshold}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too large.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("1e-9")]
    [Arguments("1e-300")]
    public async Task A_threshold_that_rounds_to_no_time_at_all_is_refused(string threshold)
    {
        // The tool measures time in steps of a ten-millionth of a second. A threshold below one step would be
        // a threshold of nothing: no visit is shorter than it, and the footer would still print the number.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = JsonNode.Parse($$"""{ "{{Workflow.Development.Id}}": {{threshold}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too small: it rounds to no time at all.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("null")]
    [Arguments("false")]
    [Arguments("5")]
    [Arguments("[]")]
    [Arguments("\"synthetic-pasted-token\"")]
    public async Task The_setting_written_as_anything_but_an_object_is_refused(string setting)
    {
        // Written by someone who meant "check nothing". Leaving the setting out, or an empty object, says that.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = JsonNode.Parse(setting);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds must be an object of status IDs, each with its threshold in minutes; leave the setting out to check no status.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task A_status_listed_twice_is_refused_and_not_repeated()
    {
        // As for every setting: with two values, the reader of the file cannot tell which one is in force.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        var once = $"\"{Workflow.Development.Id}\":5";
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace(once, $"{once},\"{Workflow.Development.Id}\":2", StringComparison.Ordinal);

        await Assert.That(pipeline.RawConfig).Contains($"{once},\"{Workflow.Development.Id}\":2");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitThresholds lists a status twice.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    /// <summary>The file was refused as it was loaded:the command failed, no request went to Jira, and no database was made.</summary>
    private static async Task AssertNothingWasFetchedOrStoredAsync(PipelineHarness pipeline, int exit)
    {
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }
}
