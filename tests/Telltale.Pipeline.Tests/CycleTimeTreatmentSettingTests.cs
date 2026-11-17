using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The part <c>cycleTime</c> of the <c>briefVisitTreatment</c> setting as the configuration file holds it
/// (GLOSSARY: Treatment; owner decision D83): what loads and what it means, alone and beside
/// <c>timeInStatus</c>, what is refused when the file is loaded, and that a change to it needs <c>derive</c>
/// and nothing more. What leaving out does to the figures is in <see cref="BriefVisitsLeftOutOfCycleTimeTests"/>.
/// Sprint 41 runs from Monday 5 January 2026, 09:00, to Monday the 19th, 10:00. Its team is five people, each
/// with an issue that stays in New; four more 3-point issues of four people were caught up through Development
/// in twenty seconds, and three did four business days there. Development is listed with a threshold of five
/// minutes.
/// </summary>
public class CycleTimeTreatmentSettingTests
{
    /// <summary>How many issues are behind the cycle time bucket, and how many behind Development's time in status row.</summary>
    private const string CountsSql =
        "SELECT (SELECT issue_count FROM cycle_time_per_sprint_and_bucket) AS cycle_times, (SELECT issue_count FROM time_in_status_per_sprint WHERE status_name = 'Fake Development') AS in_development;";

    private const string CountsHead = "| cycle_times | in_development |\n| --- | --- |\n";

    [Test]
    [Arguments("""{ "cycleTime": "count" }""", 7, 7)]
    [Arguments("""{ "cycleTime": "exclude" }""", 3, 7)]
    [Arguments("""{ "timeInStatus": "count", "cycleTime": "count" }""", 7, 7)]
    [Arguments("""{ "timeInStatus": "exclude", "cycleTime": "count" }""", 7, 3)]
    [Arguments("""{ "cycleTime": "exclude", "timeInStatus": "count" }""", 3, 7)]
    [Arguments("""{ "timeInStatus": "exclude", "cycleTime": "exclude" }""", 3, 3)]
    public async Task The_part_loads_alone_or_beside_the_part_for_time_in_status_and_each_part_decides_its_own_metric(string written, int cycleTimes, int inDevelopment)
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithBriefVisitsToDevelopment(pipeline);
        pipeline.Config["briefVisitTreatment"] = JsonNode.Parse(written);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead + $"| {cycleTimes} | {inDevelopment} |\n");
    }

    [Test]
    [Arguments("""{ "cycleTime": "count" }""")]
    [Arguments("""{ "timeInStatus": "count", "cycleTime": "count" }""")]
    public async Task With_count_the_report_and_every_view_are_what_they_are_with_no_setting(string written)
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

        // With no setting the seven cycles are counted, and the report says they are in the figures.
        await Assert.That(await QueryAsync(without, CountsSql)).IsEqualTo(CountsHead + "| 7 | 7 |\n");
        await Assert.That(await without.ReadReportAsync(41)).EndsWith("\n" + ReportText.CyclesThatStartWithBriefVisit("4"));
        await Assert.That(await with.ReadReportBytesAsync(41, withoutAsOf: true)).IsEqualTo(await without.ReadReportBytesAsync(41, withoutAsOf: true));
        await Assert.That(await QueryEveryMetricViewAsync(with)).IsEqualTo(await QueryEveryMetricViewAsync(without));
        await Assert.That(await with.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(await without.ReadStoreContentAsync(withoutAsOf: true));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Exclude_with_no_status_listed_is_refused_with_a_message_that_names_the_file_and_the_setting(bool anEmptyListIsWritten)
    {
        // No cycle could be left out, and the owner would be left believing figures were adjusted.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        if (anEmptyListIsWritten)
        {
            pipeline.Config["briefVisitThresholds"] = new JsonObject();
        }

        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.cycleTime is exclude, and no status is listed in briefVisitThresholds, so no cycle would be left out. List a status with the role work there, or write count.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task Exclude_with_no_listed_status_that_has_the_work_role_is_refused_with_a_message_that_names_the_file_and_the_setting()
    {
        // A cycle starts in a work status. New has the role not started and Blocked the role blocked, so with
        // only those two listed no cycle can start with a brief visit. Time in status may still leave them out.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5, [Workflow.Blocked.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "exclude" };

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.cycleTime is exclude, and no status listed in briefVisitThresholds has the role work, so no cycle could start with a brief visit and none would be left out. List a status with the role work there, or write count.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task Count_for_cycle_time_is_accepted_with_no_listed_status_that_has_the_work_role()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithBriefVisitsToDevelopment(pipeline);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.New.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude", ["cycleTime"] = "count" };

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead + "| 7 | 7 |\n");
    }

    [Test]
    public async Task The_part_written_twice_is_refused()
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };
        const string Once = "\"cycleTime\":\"exclude\"";
        pipeline.RawConfig = pipeline.Config.ToJsonString().Replace(Once, Once + ",\"cycleTime\":\"count\"", StringComparison.Ordinal);

        await Assert.That(pipeline.RawConfig).Contains(Once + ",\"cycleTime\":\"count\"");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.cycleTime is written twice.\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    [Arguments("\"exclud\"")]
    [Arguments("\"synthetic-pasted-token\"")]
    [Arguments("null")]
    [Arguments("5")]
    public async Task A_value_that_is_neither_count_nor_exclude_is_refused_and_not_repeated(string value)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        pipeline.Config["briefVisitTreatment"] = JsonNode.Parse($$"""{ "timeInStatus": "exclude", "cycleTime": {{value}} }""");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            $"The configuration file {pipeline.ConfigPath} is not valid: briefVisitTreatment.cycleTime must be written as \"count\" or \"exclude\".\n");
        await AssertNothingWasFetchedOrStoredAsync(pipeline, exit);
    }

    [Test]
    public async Task After_the_part_changes_report_and_query_refuse_until_derive_has_run_and_no_backfill_is_needed()
    {
        await using var pipeline = new PipelineHarness();
        AddSprint41WithBriefVisitsToDevelopment(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        var requests = pipeline.Jira.Handler.Requests.Count;
        pipeline.Out.GetStringBuilder().Clear();

        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["cycleTime"] = "exclude" };
        var reportExit = await pipeline.RunAsync("report", "--sprint", "41");
        var queryExit = await pipeline.RunAsync("query", CountsSql);

        await Assert.That(reportExit).IsEqualTo(1);
        await Assert.That(queryExit).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md"))).IsFalse();
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "briefVisitTreatment in the configuration file changed since the derived data was built. Run derive first.\n"
            + "briefVisitTreatment in the configuration file changed since the derived data was built. Run derive first.\n");

        // Derive alone repairs it: the treatment decides nothing about what is fetched.
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await QueryAsync(pipeline, CountsSql)).IsEqualTo(CountsHead + "| 3 | 7 |\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(ReportText.CycleTimeHeader + "| 3 | 3 | 4.0 | 4.0 | 6.0 | 6.0 |\n");
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
    /// Sprint 41 as the harness has it, with a team of five whose issues stay in New through it; four 3-point
    /// issues <see cref="Scenarios.CaughtUp"/>, one for each of four people; three 3-point issues of the last
    /// three people that were in Development from Tuesday 6 January 09:00 to Monday the 12th; and Development listed.
    /// </summary>
    private static void AddSprint41WithBriefVisitsToDevelopment(PipelineHarness pipeline)
    {
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        foreach (var (person, number) in People.Team.Take(4).Select((person, index) => (person, index + 1)))
        {
            Scenarios.CaughtUp(pipeline.Jira, $"FAKE-{number}", person).Estimated(3);
        }

        foreach (var (person, number) in People.Team.Skip(2).Select((person, index) => (person, index + 11)))
        {
            Scenarios.Delivered(pipeline.Jira, 41, $"FAKE-{number}", points: 3, person, days: 4);
        }

        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
    }
}
