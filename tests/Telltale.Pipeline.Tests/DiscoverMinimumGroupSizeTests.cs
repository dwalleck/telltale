using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>discover</c> and the <c>minimumGroupSize</c> setting (owner decision D69): the first discover writes it with
/// five and three, and a rerun leaves it as the file has it, absent included. A rerun on a file whose numbers
/// <c>run</c> would refuse stops, as for every other setting (owner decision D70).
/// </summary>
public class DiscoverMinimumGroupSizeTests
{
    /// <summary>A rerun with something to change, so the file is rewritten.</summary>
    private static readonly string[] RerunThatChangesTheTimeZone = ["discover", "--time-zone", "Europe/London"];

    [Test]
    public async Task The_first_discover_writes_the_setting_with_a_team_of_five_and_three_assignees_behind_a_figure()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["minimumGroupSize"]?.ToJsonString()).IsEqualTo("""{"team":5,"assigneesBehindAFigure":3}""");
    }

    [Test]
    public async Task A_first_discover_that_completes_a_file_made_by_hand_leaves_it_without_the_setting()
    {
        // Owner decision D69: the setting is written into a new file. In a file that exists, leaving it out is the owner's.
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, """{ "holidays": ["2026-01-19"] }""");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["holidays"]!.ToJsonString()).IsEqualTo("""["2026-01-19"]""");
        await Assert.That(written["statusRoles"]![Workflow.Done.Id]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(written["abandonedResolutionIds"]!.ToJsonString()).IsEqualTo("[]");
        await Assert.That(written.AsObject().ContainsKey("minimumGroupSize")).IsFalse();
    }

    [Test]
    public async Task The_file_discover_wrote_loads_and_its_report_is_the_one_the_same_file_gives_without_the_setting()
    {
        // Four assignees: a sprint the default numbers withhold whole, so the report shows whether they are in force.
        await using var withTheSetting = new PipelineHarness();
        await using var withoutTheSetting = new PipelineHarness();
        foreach (var pipeline in new[] { withTheSetting, withoutTheSetting })
        {
            AddSprint41WithFourAssignees(pipeline.Jira);
            await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        }

        await EditWrittenConfigAsync(withoutTheSetting, config => config.AsObject().Remove("minimumGroupSize"));

        var ranWith = await withTheSetting.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");
        var ranWithout = await withoutTheSetting.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(withTheSetting.Error.ToString() + withoutTheSetting.Error).IsEmpty();
        await Assert.That(ranWith).IsEqualTo(0);
        await Assert.That(ranWithout).IsEqualTo(0);
        await Assert.That((await ReadWrittenConfigAsync(withTheSetting)).AsObject().ContainsKey("minimumGroupSize")).IsTrue();
        await Assert.That((await ReadWrittenConfigAsync(withoutTheSetting)).AsObject().ContainsKey("minimumGroupSize")).IsFalse();
        var report = await withTheSetting.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n");
        await Assert.That(report).IsEqualTo(await withoutTheSetting.ReadReportAsync(41));
    }

    [Test]
    [Arguments(0, 0)]
    [Arguments(4, 2)]
    [Arguments(9, 1)]
    [Arguments(5, 3)]
    public async Task A_rerun_keeps_the_numbers_of_the_file_and_says_nothing_about_them(int team, int assigneesBehindAFigure)
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["minimumGroupSize"] = new JsonObject { ["team"] = team, ["assigneesBehindAFigure"] = assigneesBehindAFigure });
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["minimumGroupSize"]?.ToJsonString()).IsEqualTo($$"""{"team":{{team}},"assigneesBehindAFigure":{{assigneesBehindAFigure}}}""");
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("minimumGroupSize").And.DoesNotContain("group size");
    }

    [Test]
    public async Task A_rerun_on_a_file_without_the_setting_leaves_it_without_the_setting_and_says_nothing_about_it()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config.AsObject().Remove("minimumGroupSize"));
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written.AsObject().ContainsKey("minimumGroupSize")).IsFalse();
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("minimumGroupSize").And.DoesNotContain("group size");
    }

    [Test]
    public async Task A_rerun_with_nothing_to_change_does_not_rewrite_a_file_without_the_setting()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config.AsObject().Remove("minimumGroupSize"));
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_rerun_keeps_the_file_with_the_owners_numbers_as_its_backup_before_rewriting_it()
    {
        // The harness clock reads 3 February 2026, noon in Chicago: 18:00 UTC.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["minimumGroupSize"] = new JsonObject { ["team"] = 0, ["assigneesBehindAFigure"] = 0 });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);
        var backup = pipeline.ConfigPath + ".20260203T180000Z.bak";

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["minimumGroupSize"]?.ToJsonString()).IsEqualTo("""{"team":0,"assigneesBehindAFigure":0}""");
        await Assert.That(await File.ReadAllTextAsync(backup)).IsEqualTo(edited);
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).Contains($"The previous file is kept as {backup}.\n");
    }

    [Test]
    public async Task A_rerun_on_a_file_whose_numbers_run_would_refuse_stops_before_jira_is_called_and_leaves_the_file_and_its_backups_as_they_are()
    {
        // Owner decision D70: as for every other setting run would refuse. The first rerun leaves a backup to be left alone.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await DiscoverAsSetupAsync(pipeline, RerunThatChangesTheTimeZone);
        await EditWrittenConfigAsync(pipeline, config => config["minimumGroupSize"] = new JsonObject { ["team"] = -1, ["assigneesBehindAFigure"] = 3 });
        var before = await pipeline.ReadAllFilesAsync();
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/Paris");

        // Refused with everything else that can be refused: before the first call to Jira, and before a line
        // about a change that is then not made.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: minimumGroupSize.team must be written as a whole number of zero or more.\n");
        await Assert.That(requestsBefore).IsGreaterThan(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        var after = await pipeline.ReadAllFilesAsync();
        await Assert.That(before.Keys).IsEquivalentTo(["telltale.json", "telltale.json.20260203T180000Z.bak"]);
        await Assert.That(after.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"{file.Key}\n{file.Value}"))
            .IsEquivalentTo(before.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"{file.Key}\n{file.Value}"));
    }

    [Test]
    [Arguments("null", "minimumGroupSize must be an object with the two numbers team and assigneesBehindAFigure; leave the setting out for five and three.")]
    [Arguments("""{ "team": 5 }""", "minimumGroupSize.assigneesBehindAFigure is missing: when minimumGroupSize is present, both of its numbers are required.")]
    [Arguments("""{ "team": "5", "assigneesBehindAFigure": 3 }""", "minimumGroupSize.team must be written as a whole number of zero or more.")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": -3 }""", "minimumGroupSize.assigneesBehindAFigure must be written as a whole number of zero or more.")]
    [Arguments("""{ "team": 5, "assigneesBehindAFigure": 3, "people": 2 }""", "minimumGroupSize.people is not a setting the tool knows.")]
    public async Task A_rerun_refuses_the_setting_as_run_does_whatever_is_wrong_with_it_and_calls_jira_for_none_of_it(string setting, string refusal)
    {
        // The loader's own reading and rules, asked before Jira: the message is the one run gives for the file.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["minimumGroupSize"] = JsonNode.Parse(setting));
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    /// <summary>A discover that only prepares the test: it must succeed, so a failed setup is reported where it happens.</summary>
    private static async Task DiscoverAsSetupAsync(PipelineHarness pipeline, string[] args)
    {
        var exit = await pipeline.RunWithFilesAsTheyAreAsync(args);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
    }
}
