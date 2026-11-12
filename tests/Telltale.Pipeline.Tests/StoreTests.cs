using Microsoft.Data.Sqlite;
using Telltale.Pipeline.Tests.Harness;
using Telltale.Storage.Sqlite;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>What the store promises the owner who also uses the database by hand.</summary>
public class StoreTests
{
    [Test]
    public async Task A_view_the_owner_saved_survives_the_next_run()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.ExecuteAsync("CREATE VIEW owner_saved_view AS SELECT sprint_id, delivered_count FROM delivered_per_sprint;");

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM owner_saved_view;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 1L) });
    }

    [Test]
    public async Task A_query_over_the_views_cannot_change_the_store()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<long>("DELETE FROM issue_cycle RETURNING 1;")).Throws<SqliteException>().WithMessageContaining("readonly");

        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM delivered_per_sprint;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 1L) });
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
