using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// A configuration file whose <c>briefVisitTreatment</c> cannot be right is refused by every command that loads
/// the configuration, before anything is fetched or stored, with a message that names the file and the setting
/// and says what is allowed (owner decision D83; user stories 5 to 7 of the brief visit treatment spec). No
/// message repeats what was written under the setting, a part's name included: it could be a secret pasted into
/// the wrong place (owner decision D43). The forms each refusal takes are in
/// <see cref="BriefVisitTreatmentSettingTests"/> and <see cref="CycleTimeTreatmentSettingTests"/>, with
/// <c>run</c>; <c>discover</c> is in <see cref="DiscoverBriefVisitTreatmentTests"/>.
/// </summary>
public class RefusedBriefVisitTreatmentTests
{
    /// <summary>Stands for a secret pasted into the wrong place: no message may hold it.</summary>
    private const string Pasted = "synthetic-pasted-token";

    /// <summary>Every command that loads the configuration, apart from <c>discover</c>.</summary>
    public static IEnumerable<string> EveryCommand() => ["backfill", "sync", "derive", "report", "run", "query"];

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task The_setting_written_as_null_is_refused_by_every_command(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = null;

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment must be an object with the parts timeInStatus and cycleTime; leave the setting out to count brief visits and change no figure.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task The_setting_written_as_text_is_refused_by_every_command_and_the_text_is_not_repeated(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = Pasted;

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment must be an object with the parts timeInStatus and cycleTime; leave the setting out to count brief visits and change no figure.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task A_part_the_tool_does_not_know_is_refused_by_every_command_with_the_two_parts_that_are_allowed_and_not_the_name_written(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", [Pasted] = "exclude" };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment has a part the tool does not know. The parts are timeInStatus and cycleTime.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task A_value_other_than_count_or_exclude_is_refused_by_every_command_and_not_repeated(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = Pasted };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.timeInStatus must be written as \"count\" or \"exclude\".");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task A_value_of_the_wrong_type_is_refused_by_every_command(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = true };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.cycleTime must be written as \"count\" or \"exclude\".");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task A_value_that_is_not_text_json_can_hold_is_refused_by_every_command_as_any_other_value_is(string command)
    {
        // An escape for half of a character pair: no text at all, so neither label.
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "to-be-replaced" };
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace("\"to-be-replaced\"", "\"\\uD800\"", StringComparison.Ordinal);
        await Assert.That(pipeline.RawConfig).Contains("\"timeInStatus\":\"\\uD800\"");

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.timeInStatus must be written as \"count\" or \"exclude\".");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task A_part_written_twice_is_refused_by_every_command_and_neither_value_is_repeated(string command)
    {
        await using var pipeline = new PipelineHarness();
        ListDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };
        const string Once = "\"cycleTime\":\"exclude\"";
        const string Twice = Once + ",\"cycleTime\":\"" + Pasted + "\"";
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace(Once, Twice, StringComparison.Ordinal);
        await Assert.That(pipeline.RawConfig).Contains(Twice);

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.cycleTime is written twice.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task Exclude_for_time_in_status_with_no_status_listed_is_refused_by_every_command(string command)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task Exclude_for_cycle_time_with_no_status_listed_is_refused_by_every_command(string command)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.cycleTime is exclude, and no status is listed in briefVisitThresholds, so no cycle would be left out. List a status with the role work there, or write count.");
    }

    [Test]
    [MethodDataSource(nameof(EveryCommand))]
    public async Task Exclude_for_cycle_time_with_no_listed_status_that_has_the_work_role_is_refused_by_every_command(string command)
    {
        // New has the role not started, so no cycle can start with a brief visit to it.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };

        var exit = await RunAsync(pipeline, command);

        await AssertRefusedAsync(pipeline, exit, "briefVisitTreatment.cycleTime is exclude, and no status listed in briefVisitThresholds has the role work, so no cycle could start with a brief visit and none would be left out. List a status with the role work there, or write count.");
    }

    /// <summary>Sprint 41, with Development listed for brief visits: a file in which only the treatment can be refused.</summary>
    private static void ListDevelopment(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
    }

    /// <summary>Runs <paramref name="command"/> with what it cannot run without: a sprint for <c>report</c> and <c>run</c>, a statement for <c>query</c>.</summary>
    private static Task<int> RunAsync(PipelineHarness pipeline, string command) => pipeline.RunAsync(command switch
    {
        "report" or "run" => [command, "--sprint", "41"],
        "query" => [command, "SELECT sprint_id FROM delivered_per_sprint;"],
        _ => [command],
    });

    /// <summary>
    /// The file was refused as it was loaded: the command failed and said only <paramref name="refusal"/>
    /// after the file's name, no request went to Jira, and no database was made.
    /// </summary>
    private static async Task AssertRefusedAsync(PipelineHarness pipeline, int exit, string refusal)
    {
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo($"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }
}
