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
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 5L) });
    }

    [Test]
    public async Task A_query_over_the_views_cannot_change_the_store()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<long>("DELETE FROM issue_cycle RETURNING 1;")).Throws<InvalidOperationException>().WithMessageContaining("reads only the metric views");

        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM delivered_per_sprint;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 5L) });
    }

    [Test]
    [Arguments("SELECT assignee_hash FROM issue;")]
    [Arguments("SELECT COUNT(*) FROM issue;")]
    [Arguments("SELECT issue_id FROM issue_cycle WHERE delivery_sprint_id = '41';")]
    [Arguments("SELECT d.sprint_id FROM delivered_per_sprint d JOIN sprint_status_time t ON t.sprint_id = d.sprint_id;")]
    [Arguments("SELECT sprint_id FROM delivered_per_sprint WHERE EXISTS (SELECT 1 FROM issue);")]
    [Arguments("WITH delivered_per_sprint AS (SELECT assignee_hash FROM issue) SELECT * FROM delivered_per_sprint;")]
    [Arguments("SELECT name FROM sqlite_master;")]
    [Arguments("SELECT name FROM pragma_table_info('issue');")]
    public async Task A_query_over_the_views_cannot_read_a_raw_or_derived_table(string sql)
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<string>(sql))
            .Throws<InvalidOperationException>().WithMessageContaining("reads only the metric views");
    }

    [Test]
    public async Task A_query_over_the_views_reads_every_metric_view()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        var store = new SqliteStore(pipeline.DatabasePath);

        var rows = await store.QueryViewsAsync<(long Sprints, long Buckets, long Statuses)>(
            """
            SELECT (SELECT COUNT(*) FROM delivered_per_sprint WHERE sprint_id = @sprintId),
                   (SELECT COUNT(*) FROM cycle_time_per_sprint_and_bucket c WHERE c.sprint_id = @sprintId),
                   (SELECT COUNT(t.status_id) FROM time_in_status_per_sprint t WHERE t.sprint_id = @sprintId);
            """,
            new { sprintId = "41" });

        // One sprint, one point bucket, and the two statuses the issues spent business time in.
        await Assert.That(rows).IsEquivalentTo(new[] { (1L, 1L, 2L) });
    }

    [Test]
    public async Task A_query_over_the_views_can_count_a_views_rows_without_naming_a_column()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        var store = new SqliteStore(pipeline.DatabasePath);

        var rows = await store.QueryViewsAsync<long>("SELECT COUNT(*) FROM time_in_status_per_sprint;");

        // The two statuses the issues spent business time in.
        await Assert.That(rows).IsEquivalentTo(new[] { 2L });
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.ServeEachIssueOncePer(People.Team);
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9)).InSprints(41)
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
