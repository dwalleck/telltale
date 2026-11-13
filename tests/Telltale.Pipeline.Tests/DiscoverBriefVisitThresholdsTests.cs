using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// <c>discover</c> and the <c>briefVisitThresholds</c> setting (user story 9 of the brief status visits spec):
/// discover never writes the setting and never removes it, so a rerun keeps it as the owner wrote it. A rerun
/// on a file whose setting <c>run</c> would refuse stops before Jira is called and leaves the file untouched,
/// as for every other setting (owner decision D70).
/// </summary>
public class DiscoverBriefVisitThresholdsTests
{
    /// <summary>A rerun with something to change, so the file is rewritten.</summary>
    private static readonly string[] RerunThatChangesTheTimeZone = ["discover", "--time-zone", "Europe/London"];

    /// <summary>A status ID in Jira's form that the fake board does not have, so discover gives it no role.</summary>
    private const string StatusWithoutARole = "699999";

    [Test]
    public async Task The_first_discover_does_not_write_the_setting()
    {
        await using var pipeline = new PipelineHarness();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written.AsObject().ContainsKey("statusRoles")).IsTrue();
        await Assert.That(written.AsObject().ContainsKey("briefVisitThresholds")).IsFalse();
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("briefVisitThresholds").And.DoesNotContain("brief");
    }

    [Test]
    [Arguments("""{"600004":5}""")]
    [Arguments("""{"600005":2,"600004":0.5,"600001":1e1,"600007":3.0}""")]
    [Arguments("{}")]
    public async Task A_rerun_keeps_the_setting_as_the_owner_wrote_it_and_says_nothing_about_it(string setting)
    {
        // Out of board order, a fraction, and numbers written in other forms: all stay as they were typed.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["briefVisitThresholds"] = JsonNode.Parse(setting));
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written["briefVisitThresholds"]?.ToJsonString()).IsEqualTo(setting);
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("briefVisitThresholds").And.DoesNotContain("brief");
    }

    [Test]
    public async Task A_rerun_on_a_file_without_the_setting_leaves_it_without_the_setting()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(written.AsObject().ContainsKey("briefVisitThresholds")).IsFalse();
        await Assert.That(pipeline.Out.ToString()).DoesNotContain("briefVisitThresholds").And.DoesNotContain("brief");
    }

    [Test]
    public async Task A_rerun_with_nothing_to_change_does_not_rewrite_a_file_that_has_the_setting()
    {
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync("discover");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    [Arguments($$"""{ "{{StatusWithoutARole}}": 5 }""", "briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.")]
    [Arguments("""{ "600008": 5 }""", "briefVisitThresholds: a status with the role done cannot be listed, and one is. A done status has no time in status row.")]
    [Arguments("""{ "600004": 0 }""", "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.")]
    [Arguments("""{ "600004": -2 }""", "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.")]
    [Arguments("""{ "600004": "5" }""", "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.")]
    [Arguments("""{ "600004": null }""", "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.")]
    [Arguments("""{ "600004": 1e300 }""", "briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too large.")]
    [Arguments("""{ "600004": 1e-9 }""", "briefVisitThresholds: every threshold must be a length of time the tool can measure, and one is too small: it rounds to no time at all.")]
    [Arguments("null", "briefVisitThresholds must be an object of status IDs, each with its threshold in minutes; leave the setting out to check no status.")]
    public async Task A_rerun_refuses_the_setting_as_run_does_before_jira_is_called_and_leaves_the_file_and_its_backups_as_they_are(string setting, string refusal)
    {
        // Owner decision D70. The first rerun leaves a backup, to be left alone by the one that is refused.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await DiscoverAsSetupAsync(pipeline, RerunThatChangesTheTimeZone);
        await EditWrittenConfigAsync(pipeline, config => config["briefVisitThresholds"] = JsonNode.Parse(setting));
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
    public async Task A_first_discover_that_completes_a_file_made_by_hand_keeps_the_setting_when_the_roles_it_proposes_allow_it()
    {
        // The file has no roles yet, so whether a listed status has one is known only once Jira has been read.
        await using var pipeline = new PipelineHarness();
        await File.WriteAllTextAsync(pipeline.ConfigPath, $$"""{ "briefVisitThresholds": { "{{Workflow.Development.Id}}": 5 } }""");

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var written = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(written["statusRoles"]![Workflow.Development.Id]!.GetValue<string>()).IsEqualTo("work");
        await Assert.That(written["briefVisitThresholds"]?.ToJsonString()).IsEqualTo($$"""{"{{Workflow.Development.Id}}":5}""");
    }

    [Test]
    [Arguments(StatusWithoutARole, "briefVisitThresholds: every status listed must be a status in statusRoles, and one is not.")]
    [Arguments("600008", "briefVisitThresholds: a status with the role done cannot be listed, and one is. A done status has no time in status row.")]
    public async Task A_first_discover_that_completes_a_file_made_by_hand_does_not_write_a_setting_run_would_refuse_under_the_roles_it_proposes(string listed, string refusal)
    {
        await using var pipeline = new PipelineHarness();
        var byHand = $$"""{ "briefVisitThresholds": { "{{listed}}": 5 } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, byHand);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(byHand);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_first_discover_on_a_file_made_by_hand_refuses_a_threshold_run_would_refuse_before_jira_is_called()
    {
        // A threshold is refused whatever the roles turn out to be, so it does not wait for Jira.
        await using var pipeline = new PipelineHarness();
        var byHand = $$"""{ "briefVisitThresholds": { "{{Workflow.Development.Id}}": 0 } }""";
        await File.WriteAllTextAsync(pipeline.ConfigPath, byHand);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(DiscoverTests.FirstDiscover);

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: "
            + "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.\n");
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
