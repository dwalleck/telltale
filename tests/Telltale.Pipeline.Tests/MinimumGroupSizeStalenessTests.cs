using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Derived data built under other numbers of the minimum group size is stale (owner decision D67): the views
/// withhold by the numbers the last <c>derive</c> recorded, so until the next one <c>report</c> and
/// <c>query</c> would show, or withhold, what the configuration no longer says. Each of the two numbers is
/// checked on its own, loosened and tightened: a change of one alone must stop both commands. (Both numbers
/// changed at once, and the setting taken out, are OptionalSuppressionTests'.) Sprint 41 has four assignees.
/// </summary>
public class MinimumGroupSizeStalenessTests
{
    private const string NumbersSql = "SELECT team, assignees_behind_a_figure FROM minimum_group_size;";

    [Test]
    [Arguments(5, 3, 0, 3)]
    [Arguments(0, 3, 5, 3)]
    [Arguments(5, 3, 5, 0)]
    [Arguments(5, 0, 5, 3)]
    public async Task After_one_of_the_two_numbers_changes_report_writes_no_file_and_query_prints_no_row_until_derive_has_run(int team, int assigneesBehindAFigure, int newTeam, int newAssigneesBehindAFigure)
    {
        await using var pipeline = new PipelineHarness();
        var stale = await ReportAndQueryAfterTheNumbersChangeAsync(pipeline, (team, assigneesBehindAFigure), (newTeam, newAssigneesBehindAFigure));
        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"], ["query", NumbersSql]);

        // Whatever the refusal says, nothing derived under the old numbers is shown, and derive is the remedy.
        await Assert.That(stale.ReportExit).IsEqualTo(1);
        await Assert.That(stale.QueryExit).IsEqualTo(1);
        await Assert.That(stale.Printed).IsEmpty();
        await Assert.That(stale.ReportWritten).IsFalse();
        var refusals = pipeline.Error.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(refusals.Length).IsEqualTo(2);
        await Assert.That(refusals[0]).EndsWith(" Run derive first.");
        await Assert.That(refusals[1]).EndsWith(" Run derive first.");

        // After derive both commands answer, under the new numbers.
        await Assert.That(pipeline.Out.ToString().ReplaceLineEndings("\n")).EndsWith(
            $"| team | assignees_behind_a_figure |\n| --- | --- |\n| {newTeam} | {newAssigneesBehindAFigure} |\n");
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith("# Fake Sprint 41 ");
    }

    [Test]
    [Arguments(5, 3, 0, 3)]
    [Arguments(0, 3, 5, 3)]
    [Arguments(5, 3, 5, 0)]
    [Arguments(5, 0, 5, 3)]
    public async Task After_one_of_the_two_numbers_changes_the_refusal_of_report_and_of_query_names_the_setting(int team, int assigneesBehindAFigure, int newTeam, int newAssigneesBehindAFigure)
    {
        await using var pipeline = new PipelineHarness();

        await ReportAndQueryAfterTheNumbersChangeAsync(pipeline, (team, assigneesBehindAFigure), (newTeam, newAssigneesBehindAFigure));

        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.\n"
            + "minimumGroupSize in the configuration file changed since the derived data was built. Run derive first.\n");
    }

    /// <summary>
    /// Fills the store and derives under the first pair of numbers, writes the second pair into the
    /// configuration, and runs <c>report</c> and then <c>query</c> without a derive between. Returns how each
    /// exited, what the two printed, and whether a report file was written.
    /// </summary>
    private static async Task<(int ReportExit, int QueryExit, string Printed, bool ReportWritten)> ReportAndQueryAfterTheNumbersChangeAsync(
        PipelineHarness pipeline, (int Team, int AssigneesBehindAFigure) derivedUnder, (int Team, int AssigneesBehindAFigure) changedTo)
    {
        AddSprint41WithFourAssignees(pipeline.Jira);
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(derivedUnder.Team, derivedUnder.AssigneesBehindAFigure);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();
        pipeline.Config["minimumGroupSize"] = MinimumGroupSize(changedTo.Team, changedTo.AssigneesBehindAFigure);

        var reportExit = await pipeline.RunAsync("report", "--sprint", "41");
        var queryExit = await pipeline.RunAsync("query", NumbersSql);

        return (reportExit, queryExit, pipeline.Out.ToString(), File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-41.md")));
    }
}
