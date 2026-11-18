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
    [Arguments("timeInStatus", "briefVisitTreatment.timeInStatus is written twice.")]
    [Arguments("synthetic-pasted-token", UnknownPart)]
    public async Task A_rerun_on_a_file_with_a_key_written_twice_under_the_setting_says_what_run_says_and_prints_no_part_of_the_key(string key, string refusal)
    {
        // Owner decision D105. discover's own reader stops at a key written twice, and its message would
        // quote the key; a key typed under this setting could be a secret, so here the setting's reader speaks.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = new JsonObject { [key] = "exclude" };
        });
        var once = $"\"{key}\":\"exclude\"";
        var twice = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace(once, $"{once},{once}", StringComparison.Ordinal);
        await Assert.That(twice).Contains($"{once},{once}");
        await File.WriteAllTextAsync(pipeline.ConfigPath, twice);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: {refusal}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(twice);
        await Assert.That((await pipeline.ReadAllFilesAsync()).Keys).IsEquivalentTo(["telltale.json"]);
    }

    [Test]
    public async Task A_key_written_twice_under_another_setting_is_still_refused_by_discovers_own_reader()
    {
        // Elsewhere than under briefVisitTreatment it is discover's own reader that refuses a key written
        // twice. What discover then says, and that it quotes no part of the key (owner decision D108), is
        // pinned whole in DiscoverKeyWrittenTwiceTests.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };
        });
        var once = $"\"{Workflow.Development.Id}\":5";
        var twice = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace(once, $"{once},{once}", StringComparison.Ordinal);
        await Assert.That(twice).Contains($"{once},{once}");
        await File.WriteAllTextAsync(pipeline.ConfigPath, twice);

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString().ReplaceLineEndings("\n");
        await Assert.That(error).StartsWith($"The configuration file {pipeline.ConfigPath} cannot be read (");
        await Assert.That(error).EndsWith("), so it was left as it is. Correct it or move it away, then run discover again.\n");
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(twice);
    }

    [Test]
    public async Task A_rerun_refuses_a_value_that_is_not_text_json_can_hold_as_run_does_before_jira_is_called()
    {
        // An escape for half of a character pair: no text at all, so neither label. It is refused as any other value is.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
            config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "to-be-replaced" };
        });
        var unpaired = (await File.ReadAllTextAsync(pipeline.ConfigPath)).Replace("\"to-be-replaced\"", "\"\\uD800\"", StringComparison.Ordinal);
        await Assert.That(unpaired).Contains("\"timeInStatus\":\"\\uD800\"");
        await File.WriteAllTextAsync(pipeline.ConfigPath, unpaired);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: {TimeInStatusValue}\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: {TimeInStatusValue}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(unpaired);
    }

    [Test]
    public async Task With_two_faults_in_the_file_discover_and_run_each_report_the_first_one_they_find()
    {
        // Owner decision D104. Development's threshold is zero and the treatment has a part the tool does not
        // know. discover checks setting by setting and stops at the threshold; run reads the whole file first
        // and stops at the part. Each message is true of the file, and nothing is fetched or written.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config =>
        {
            config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 0 };
            config["briefVisitTreatment"] = new JsonObject { ["synthetic-pasted-token"] = "exclude" };
        });
        var edited = await File.ReadAllTextAsync(pipeline.ConfigPath);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);
        var ran = await pipeline.RunWithFilesAsTheyAreAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(ran).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"discover did not write {pipeline.ConfigPath}, because run would refuse it: The configuration file {pipeline.ConfigPath} is not valid: "
            + "briefVisitThresholds: every threshold must be written as a number of minutes greater than zero, and one is not.\n"
            + $"The configuration file {pipeline.ConfigPath} is not valid: {UnknownPart}\n");
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
        await Assert.That(await File.ReadAllTextAsync(pipeline.ConfigPath)).IsEqualTo(edited);
    }

    [Test]
    public async Task A_rerun_keeps_a_setting_written_first_in_the_file_with_comments_and_a_trailing_comma()
    {
        // The file as an owner may lay it out by hand. The setting is kept as the value it is, and in its
        // place; the comments go with every rewrite, as discover says.
        await using var pipeline = new PipelineHarness();
        await DiscoverAsSetupAsync(pipeline, DiscoverTests.FirstDiscover);
        await EditWrittenConfigAsync(pipeline, config => config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 });
        var written = await File.ReadAllTextAsync(pipeline.ConfigPath);
        await Assert.That(written).StartsWith("{");
        const string ByHand =
            """
            {
              /* What is done with brief visits. */
              "briefVisitTreatment": {
                // Left out of both.
                "cycleTime": "exclude",
                "timeInStatus": "exclude",
              },

            """;
        await File.WriteAllTextAsync(pipeline.ConfigPath, ByHand + written[1..]);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunWithFilesAsTheyAreAsync(RerunThatChangesTheTimeZone);

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var rewritten = await ReadWrittenConfigAsync(pipeline);
        await Assert.That(rewritten["timeZone"]!.GetValue<string>()).IsEqualTo("Europe/London");
        await Assert.That(rewritten.AsObject().First().Key).IsEqualTo("briefVisitTreatment");
        await Assert.That(rewritten["briefVisitTreatment"]?.ToJsonString()).IsEqualTo("""{"cycleTime":"exclude","timeInStatus":"exclude"}""");
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
