using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.HarnessSteps;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The treatment in force, through <c>query</c> (GLOSSARY: Treatment; owner decision D103): a metric view gives
/// the treatment the derived data was built under, one value for time in status and one for cycle time, so
/// that a figure read through <c>query</c> can be told from the same figure under the other treatment.
/// </summary>
public class TreatmentInForceTests
{
    private const string TreatmentSql = "SELECT * FROM brief_visit_treatment;";

    private const string TreatmentHead = "| time_in_status | cycle_time |\n| --- | --- |\n";

    [Test]
    [Arguments(null, null, "| count | count |")]
    [Arguments("exclude", null, "| exclude | count |")]
    [Arguments(null, "exclude", "| count | exclude |")]
    [Arguments("exclude", "exclude", "| exclude | exclude |")]
    [Arguments("count", "count", "| count | count |")]
    public async Task Query_gives_the_treatment_the_derived_data_was_built_under_one_value_for_each_metric(string? timeInStatus, string? cycleTime, string row)
    {
        // A part left out of the setting, and the setting left out, mean count.
        await using var pipeline = new PipelineHarness();
        Scenarios.AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        var setting = new JsonObject();
        if (timeInStatus is not null)
        {
            setting["timeInStatus"] = timeInStatus;
        }

        if (cycleTime is not null)
        {
            setting["cycleTime"] = cycleTime;
        }

        if (setting.Count > 0)
        {
            pipeline.Config["briefVisitTreatment"] = setting;
        }

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(await QueryAsync(pipeline, TreatmentSql)).IsEqualTo(TreatmentHead + row + "\n");
    }

    [Test]
    public async Task The_view_gives_what_the_last_derive_recorded_and_not_what_the_configuration_file_says_now()
    {
        await using var pipeline = new PipelineHarness();
        Scenarios.AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Config["briefVisitThresholds"] = new JsonObject { [Workflow.Development.Id] = 5 };
        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        // The file changes and derive does not run: the store still holds figures measured under count, and
        // the view read straight from the store says so. (The query command itself refuses until derive runs.)
        pipeline.Config["briefVisitTreatment"] = new JsonObject { ["timeInStatus"] = "exclude" };

        var stored = await pipeline.QueryAsync<string>("SELECT time_in_status || ' and ' || cycle_time FROM brief_visit_treatment;");
        await Assert.That(string.Join("; ", stored)).IsEqualTo("count and count");
        await Assert.That(await pipeline.RunAsync("query", TreatmentSql)).IsEqualTo(1);
    }

    [Test]
    public async Task With_no_status_listed_the_view_has_its_row_and_both_values_are_count()
    {
        await using var pipeline = new PipelineHarness();
        Scenarios.AddSprint41WithFourAssignees(pipeline.Jira);

        await pipeline.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(await QueryAsync(pipeline, TreatmentSql)).IsEqualTo(TreatmentHead + "| count | count |\n");
    }
}
