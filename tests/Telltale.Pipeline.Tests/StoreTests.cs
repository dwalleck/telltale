using Microsoft.Data.Sqlite;
using Telltale.Core;
using Telltale.Pipeline.Tests.Harness;
using Telltale.Reporting;
using Telltale.Storage.Sqlite;
using TUnit.Assertions.Enums;
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

    private const string DerivedDataUnreadable =
        "The store's derived data cannot be read: it has not been built yet, or another version of this tool built it. Run derive, then report again.";

    [Test]
    public async Task A_report_rendered_from_a_store_that_was_never_derived_says_to_run_derive()
    {
        // The renderer called directly, as another host would call it: no command has checked the store first.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc)).Throws<UserFacingException>().WithMessage(DerivedDataUnreadable);
        await Assert.That(async () => await SprintReport.MostRecentlyCompletedSprintAsync(store)).Throws<UserFacingException>().WithMessage(DerivedDataUnreadable);
    }

    [Test]
    [Arguments("cycle_time_per_sprint_and_bucket")]
    [Arguments("time_in_status_per_sprint")]
    public async Task A_report_rendered_from_derived_data_that_lacks_a_metric_view_says_to_run_derive(string view)
    {
        // Derived data built by a version of the tool that did not have the view yet.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await pipeline.ExecuteAsync($"DROP VIEW {view};");
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc)).Throws<UserFacingException>().WithMessage(DerivedDataUnreadable);
    }

    [Test]
    public async Task A_report_rendered_from_a_file_that_is_not_a_database_gets_the_databases_error_and_not_a_message_to_run_derive()
    {
        // Only missing derived data is cured by derive. A damaged, locked or unopenable store is another failure,
        // and the commands already have plain messages for the database's own errors.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["run", "--sprint", "41"]);
        await File.WriteAllTextAsync(pipeline.DatabasePath, new string('x', 4096));
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc)).Throws<SqliteException>().WithMessageContaining("not a database");
    }

    [Test]
    public async Task A_migration_numbered_below_one_already_applied_is_still_applied_and_a_gap_in_the_numbers_is_accepted()
    {
        // Two branches each add a migration, and the higher number can reach a store first. The store is put in
        // that state by hand: 0005 is recorded, and 0003 (the watermark table) is neither recorded nor there.
        // The scripts in the tree are 0001 to 0003 and 0005, so the gap at 0004 is part of every run here.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        await pipeline.ExecuteAsync(
            """
            DROP TABLE watermark;
            DELETE FROM schema_migration WHERE name = '0003_watermark.sql';
            """);
        var recordedBefore = await pipeline.QueryAsync<string>("SELECT name FROM schema_migration ORDER BY name;");

        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(recordedBefore).IsEquivalentTo(new[] { "0001_raw_tables.sql", "0002_issue_sprint_ids.sql", "0005_sprint_change_form.sql" }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.QueryAsync<string>("SELECT name FROM schema_migration ORDER BY name;"))
            .IsEquivalentTo(new[] { "0001_raw_tables.sql", "0002_issue_sprint_ids.sql", "0003_watermark.sql", "0005_sprint_change_form.sql" }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'watermark';")).IsEquivalentTo(new[] { "watermark" });
    }

    private static void AddSprintAndIssue(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
    }
}
