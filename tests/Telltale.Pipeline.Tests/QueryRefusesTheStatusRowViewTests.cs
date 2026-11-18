using Telltale.Pipeline.Tests.Harness;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The internal view <c>sprint_status_row</c> says which sprint and status may have a time in status row. It
/// is not a metric view: it applies no minimum group size, and <c>query</c> refuses it as it refuses every
/// other internal view, by whatever route a statement reaches it.
/// </summary>
public class QueryRefusesTheStatusRowViewTests
{
    [Test]
    [Arguments("SELECT sprint_id, status_id FROM sprint_status_row;")]
    [Arguments("SELECT * FROM main.sprint_status_row;")]
    [Arguments("SELECT d.sprint_id, (SELECT COUNT(*) FROM sprint_status_row r WHERE r.sprint_id = d.sprint_id) FROM delivered_per_sprint d;")]
    public async Task Query_refuses_to_read_the_internal_view_of_the_rows_a_table_may_have(string sql)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AddSprint41();
        pipeline.Jira.AddIdleTeam(Local(2026, 1, 2, 9), 41);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        pipeline.Out.GetStringBuilder().Clear();

        var exit = await pipeline.RunAsync("query", sql);

        // One line that lists the metric views, and the internal view is not among them.
        await Assert.That(exit).IsEqualTo(1);
        var error = pipeline.Error.ToString();
        await Assert.That(error).StartsWith("This query reads only the metric views (");
        await Assert.That(error).Contains("), and it asked for something else: ");
        await Assert.That(error).DoesNotContain("sprint_status_row, ");
        await Assert.That(error.TrimEnd().Split('\n').Length).IsEqualTo(1);
        await Assert.That(pipeline.Out.ToString()).IsEmpty();
    }
}
