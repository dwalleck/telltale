using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The <c>briefVisitTreatment</c> setting as the configuration file holds it (GLOSSARY: Treatment; owner
/// decision D83): what loads and what it means, what is refused when the file is loaded, and that a change to
/// it needs <c>derive</c> and nothing more. Only the part <c>timeInStatus</c> exists yet. What leaving out
/// does to the figures is in <see cref="BriefVisitsLeftOutOfTimeInStatusTests"/>.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; four more issues of four people were caught up through Development in
/// twenty seconds, and three did two days there. Development is listed with a threshold of five minutes.
/// </summary>
public class BriefVisitTreatmentSettingTests
{
    private const string RowsSql =
        "SELECT status_name, issue_count FROM time_in_status_per_sprint WHERE status_name = 'Fake Development';";

    private const string RowsHead = "| status_name | issue_count |\n| --- | --- |\n";

    private const string SevenCounted = RowsHead + "| Fake Development | 7 |\n";

    private const string ThreeRemain = RowsHead + "| Fake Development | 3 |\n";

    [Test]
    [Arguments("{}")]
    [Arguments("""{ "timeInStatus": "count" }""")]
    public async Task Without_the_part_or_with_count_the_report_and_every_view_are_what_they_are_with_no_setting(string written)
    {
        await using var without = new PipelineHarness();
        await using var with = new PipelineHarness();
        foreach (var pipeline in new[] { without, with })
        {
            AddSprint41WithBriefVisitsToDevelopment(pipeline);
        }

        with.Config["briefVisitTreatment"] = JsonNode.Parse(written);

        await without.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        await with.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);

        // With no setting the seven are counted, and so they are with the setting written this way.
        await Assert.That(await QueryAsync(without, RowsSql)).IsEqualTo(SevenCounted);
        await Assert.That(await QueryAsync(with, RowsSql)).IsEqualTo(SevenCounted);
        await Assert.That(await with.ReadReportBytesAsync(41, withoutAsOf: true)).IsEqualTo(await without.ReadReportBytesAsync(41, withoutAsOf: true));
        await Assert.That(await QueryEveryMetricViewAsync(with)).IsEqualTo(await QueryEveryMetricViewAsync(without));
        await Assert.That(await with.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(await without.ReadStoreContentAsync(withoutAsOf: true));
    }

    [Test]
    public async Task With_exclude_the_file_loads_and_brief_visits_are_left_out_of_time_in_status()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithBriefVisitsToDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(ThreeRemain);
    }

    [Test]
    public async Task Exclude_with_no_status_listed_is_refused_with_a_message_that_names_the_file_and_the_setting()
    {
        // Nothing could be left out, and the owner would be left believing figures were adjusted.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task Exclude_with_an_empty_list_of_thresholds_is_refused_as_with_none()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject();
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        var exit = await pipeline.RunAsync("derive");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.timeInStatus is exclude, and no status is listed in briefVisitThresholds, so no visit would be left out. List a status there, or write count.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("cycleTime", "\"exclude\"")]
    [Arguments("cycleTime", "\"count\"")]
    [Arguments("TimeInStatus", "\"exclude\"")]
    [Arguments("synthetic-pasted-token", "\"synthetic-pasted-value\"")]
    [Arguments("", "5")]
    public async Task A_part_the_tool_does_not_know_is_refused_and_nothing_written_in_the_file_is_repeated(string part, string value)
    {
        // cycleTime is not a part yet: the tool never accepts a treatment it does not act on.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = JsonNode.Parse($$"""{ "timeInStatus": "exclude", "{{part}}": {{value}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment has a part the tool does not know. The only part is timeInStatus.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("\"exclud\"")]
    [Arguments("\"Exclude\"")]
    [Arguments("\"synthetic-pasted-token\"")]
    [Arguments("\"\"")]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("5")]
    [Arguments("[\"exclude\"]")]
    [Arguments("{ \"exclude\": true }")]
    public async Task A_value_that_is_neither_count_nor_exclude_is_refused_and_not_repeated(string value)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = JsonNode.Parse($$"""{ "timeInStatus": {{value}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.timeInStatus must be written as \"count\" or \"exclude\".\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("null")]
    [Arguments("\"exclude\"")]
    [Arguments("[]")]
    [Arguments("true")]
    public async Task A_setting_that_is_not_an_object_is_refused(string written)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = JsonNode.Parse(written);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment must be an object with the part timeInStatus; leave the setting out to count brief visits and change no figure.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task A_part_written_twice_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };
        const string Once = "\"timeInStatus\":\"exclude\"";
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace(Once, Once + ",\"timeInStatus\":\"count\"", StringComparison.Ordinal);

        await Assert.That(pipeline.RawConfig).Contains(Once + ",\"timeInStatus\":\"count\"");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.timeInStatus is written twice.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task After_the_treatment_changes_report_and_query_refuse_until_derive_has_run_and_no_backfill_is_needed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithBriefVisitsToDevelopment(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var requests = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };
        var reportExit = await pipeline.RunAsync("report", "--sprint", "41");
        var queryExit = await pipeline.RunAsync("query", RowsSql);

        await Assert.That(reportExit).IsEqualTo(1);
        await Assert.That(queryExit).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "briefVisitTreatment in the configuration file changed since the derived data was built. Run derive first.\n"
            + "briefVisitTreatment in the configuration file changed since the derived data was built. Run derive first.\n");

        // Derive alone repairs it: the treatment decides nothing about what is fetched.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await QueryAsync(pipeline, RowsSql)).IsEqualTo(ThreeRemain);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\n| Fake Development (adjusted: see the footer) | 3 | 2.0 | 6.0 |\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requests);
    }

    /// <summary>The file was refused as it was loaded: the command failed, no request went to Jira, and no database was made.</summary>
    private static async Task AssertNothingWasFetchedOrStoredAsync(PipelineHarness pipeline, int exit)
    {
        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(0);
        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    /// <summary>
    /// Sprint 41 as the harness has it, with a team of five whose issues stay in New through it; four issues
    /// <see cref="Scenarios.CaughtUp"/>, one for each of four people; three issues of the last three people
    /// that were in Development from Tuesday 6 January 09:00 to Thursday 09:00; and Development listed.
    /// </summary>
    private static void AddSprint41WithBriefVisitsToDevelopment(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person);
        }

        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            pipeline.Jira.AddIssue($"FAKE-{number}", Local(2026, 1, 5, 9)).AssignedTo(person).InSprints(41)
                .Moved(Local(2026, 1, 6, 9), Workflow.Development)
                .Moved(Local(2026, 1, 8, 9), Workflow.Done);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
    }
}
