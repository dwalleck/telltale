using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>discover</c> and the <c>briefVisitTreatment</c> setting (user story 9 of the brief visit treatment spec;
/// owner decision D83): discover never writes the setting and never changes or removes it, so a rerun keeps it
/// as the file has it. A rerun on a file whose setting <c>run</c> would refuse writes nothing and gives the
/// message <c>run</c> gives. It does so where the thresholds are checked: before Jira is called, except for
/// what only the roles discover proposes can decide (owner decisions D70, D73).
/// </summary>
public class DiscoverBriefVisitTreatmentTests
{
    /// <summary>A rerun with something to change, so the file is rewritten.</summary>
    private static readonly string[] RerunThatChangesTheTimeZone = ["discover", "--time-zone", "Europe/London"];

    private const string NotAnObject = "briefVisitTreatment must be an object with the parts timeInStatus and cycleTime; leave the setting out to count brief visits and change no figure.";

    private const string UnknownPart = "briefVisitTreatment has a part the tool does not know. The parts are timeInStatus and cycleTime.";

    private const string TimeInStatusValue = "briefVisitTreatment.timeInStatus must be written as \"count\" or \"exclude\".";

    private const string CycleTimeValue = "briefVisitTreatment.cycleTime must be written as \"count\" or \"exclude\".";

    private const string TimeInStatusWithNothingListed = "briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.";

    private const string CycleTimeWithNothingListed = "briefVisitTreatment.cycleTime is exclude, and no status is listed in briefVisitThresholds, so no cycle would be left out. List a status with the role work there, or write count.";

    private const string CycleTimeWithNoWorkStatusListed = "briefVisitTreatment.cycleTime is exclude, and no status listed in briefVisitThresholds has the role work, so no cycle could start with a brief visit and none would be left out. List a status with the role work there, or write count.";

    [Test]
    public async Task The_first_discover_does_not_write_the_setting()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written.AsObject().ContainsKey("statusRoles")).IsTrue();
        await Assert.That(written.AsObject().ContainsKey("briefVisitTreatment")).IsFalse();
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("brief");
    }

    [Test]
    [Arguments("{}")]
    [Arguments("""{"timeInStatus":"count"}""")]
    [Arguments("""{"timeInStatus":"exclude"}""")]
    [Arguments("""{"cycleTime":"exclude","timeInStatus":"count"}""")]
    [Arguments("""{"timeInStatus":"exclude","cycleTime":"exclude"}""")]
    public async Task A_rerun_keeps_the_setting_as_the_file_has_it_and_says_nothing_about_it(string setting)
    {
        // One part, both, neither, and the parts in the other order: all stay as they were typed.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = JsonNode.Parse(setting);
        });
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["briefVisitTreatment"]?.ToJsonString()).IsEqualTo(setting);
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("brief");
    }

    [Test]
    public async Task A_rerun_on_a_file_without_the_setting_leaves_it_without_the_setting()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 });

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written.AsObject().ContainsKey("briefVisitTreatment")).IsFalse();
    }

    [Test]
    public async Task A_rerun_with_nothing_to_change_does_not_rewrite_a_file_that_has_the_setting()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "exclude" };
        });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    [Arguments(null, "null", NotAnObject)]
    [Arguments("""{ "600004": 5 }""", "\"exclude\"", NotAnObject)]
    [Arguments("""{ "600004": 5 }""", "[\"synthetic-pasted-token\"]", NotAnObject)]
    [Arguments("""{ "600004": 5 }""", """{ "synthetic-pasted-token": "exclude" }""", UnknownPart)]
    [Arguments("""{ "600004": 5 }""", """{ "timeInStatus": "synthetic-pasted-token" }""", TimeInStatusValue)]
    [Arguments("""{ "600004": 5 }""", """{ "timeInStatus": "exclude", "cycleTime": 5 }""", CycleTimeValue)]
    [Arguments(null, """{ "timeInStatus": "exclude" }""", TimeInStatusWithNothingListed)]
    [Arguments("{}", """{ "timeInStatus": "exclude" }""", TimeInStatusWithNothingListed)]
    [Arguments(null, """{ "cycleTime": "exclude" }""", CycleTimeWithNothingListed)]
    [Arguments("""{ "600001": 5, "600007": 5 }""", """{ "timeInStatus": "exclude", "cycleTime": "exclude" }""", CycleTimeWithNoWorkStatusListed)]
    public async Task A_rerun_refuses_the_setting_as_run_does_before_jira_is_called_and_leaves_the_file_and_its_backups_as_they_are(string? thresholds, string setting, string refusal)
    {
        // 600004 is Development (work); 600001 is New (not started) and 600007 Blocked (blocked). The first
        // rerun leaves a backup, to be left alone by the one that is refused.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await DiscoverAsSetupAsync(pipeline, RerunThatChangesTheTimeZone);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            if (thresholds is not null)
            {
                config["briefVisitThresholds"] = JsonNode.Parse(thresholds);
            }

            config["briefVisitTreatment"] = JsonNode.Parse(setting);
        });
        var before = await pipeline.ReadAllFilesAsync();
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover", "--time-zone", "Europe/Paris");
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        // Refused with everything else that can be refused: before the first call to Jira, and before a line
        // about a change that is then not made. The message is the one run gives for the file.
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(requestsBefore).IsGreaterThan(0);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        var after = await pipeline.ReadAllFilesAsync();
        await Assert.That(before.Keys).IsEquivalentTo(["telltale.json", "telltale.json.20260203T180000Z.bak"]);
        await Assert.That(after.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"{file.Key}\n{file.Value}"))
            .IsEquivalentTo(before.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => $"{file.Key}\n{file.Value}"));
    }

    [Test]
    public async Task A_rerun_on_a_file_with_a_part_written_twice_stops_before_jira_is_called_and_leaves_the_file_as_it_is()
    {
        // discover's own reader stops at a key written twice, before any setting is checked (owner decision
        // D74). The key it names here is one of the tool's two part names, never a value.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };
        });
        const string Once = "\"timeInStatus\":\"exclude\"";
        var twice = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace(Once, Once + ",\"timeInStatus\":\"synthetic-pasted-token\"", StringComparison.Ordinal);
        await Assert.That(twice).Contains(Once + ",\"timeInStatus\":\"synthetic-pasted-token\"");
        await File.WriteAllTextAsync(pipeline.ConfigPath, twice);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        var error = pipeline.Error.ToString().ReplaceLineEndings("\n");
        await Assert.That(error).StartsWith($"The configuration file {pipeline.ConfigPath} cannot be read (");
        await Assert.That(error).EndsWith(
            "), so it was left as it is. Correct it or move it away, then run discover again.\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.timeInStatus is written twice.\n");
        await Assert.That(error).DoesNotContain("synthetic-pasted-token");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(twice);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_first_discover_that_completes_a_file_made_by_hand_keeps_the_setting_when_the_roles_it_proposes_allow_it()
    {
        // The file has no roles yet, so whether a listed status has the work role is known only once Jira has been read.
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, $$"""{ "briefVisitThresholds": { "{{Workflow.Development.Id}}": 5 }, "briefVisitTreatment": { "cycleTime": "exclude" } }""");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["statusRoles"]![Workflow.Development.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(written["briefVisitTreatment"]?.ToJsonString()).IsEqualTo("""{"cycleTime":"exclude"}""");
    }

    [Test]
    public async Task A_first_discover_that_completes_a_file_made_by_hand_does_not_write_a_setting_run_would_refuse_under_the_roles_it_proposes()
    {
        // New is listed, and discover proposes the role not started for it: no listed status has the work role.
        await using var pipeline = new PipelineHarness();
        var byHand = $$"""{ "briefVisitThresholds": { "{{Workflow.New.Id}}": 5 }, "briefVisitTreatment": { "cycleTime": "exclude" } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, byHand);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {CycleTimeWithNoWorkStatusListed}\n");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(byHand);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    [Arguments("""{ "briefVisitTreatment": null }""", NotAnObject)]
    [Arguments("""{ "briefVisitTreatment": { "cycleTime": "exclude" } }""", CycleTimeWithNothingListed)]
    [Arguments("""{ "briefVisitThresholds": { "600001": 5 }, "briefVisitTreatment": { "cycleTime": "synthetic-pasted-token" } }""", CycleTimeValue)]
    public async Task A_first_discover_on_a_file_made_by_hand_refuses_what_no_role_could_repair_before_jira_is_called(string byHand, string refusal)
    {
        // What the setting is, and whether any status is listed, do not depend on the roles discover proposes.
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, byHand);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(byHand);
    }

    /// <summary>A discover that only prepares the test: it must succeed, so a failed setup is reported where it happens.</summary>
    private static async Task DiscoverAsSetupAsync(PipelineHarness pipeline, string[] args)
    {
        var exit = await pipeline.RunWithFilesAsTheyAreAsync(args);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
    }
}
