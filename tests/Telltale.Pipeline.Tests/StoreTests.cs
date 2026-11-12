using Microsoft.Data.Sqlite;
using Telltale.Core;
using Telltale.Core.Identity;
using Telltale.Core.Storage;
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

    [Test]
    [Arguments("UPDATE run SET objects_version = 'synthetic-older-version';")]
    [Arguments("ALTER TABLE run DROP COLUMN objects_version;")]
    [Arguments("DROP TABLE run; CREATE TABLE run (as_of TEXT NOT NULL, raw_digest TEXT NOT NULL, time_zone TEXT NOT NULL, holidays TEXT NOT NULL, objects_version TEXT NOT NULL); INSERT INTO run VALUES ('2026-02-03T18:00:00.000Z', 'synthetic-digest', 'America/Chicago', '', 'synthetic-older-version');")]
    public async Task A_report_from_derived_data_built_by_another_version_of_the_tool_is_refused_until_derive_runs_again(string howTheStoreWasLeft)
    {
        // An earlier version's views may lack a rule this version relies on, the minimum group size above all.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync(howTheStoreWasLeft);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("The derived data in the store was built by another version of this tool. Run derive first.\n");
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();

        await pipeline.RunAllAsync(["derive"], ["report", "--sprint", "41"]);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 5\n");
    }

    [Test]
    [Arguments("DROP VIEW sprint_team;", "The metric view sprint_team is not in the store. Run derive first.\n")]
    [Arguments("DROP TABLE sprint_status_time;", "The metric view delivered_per_sprint could not be read. Run derive first.\n")]
    public async Task A_report_is_refused_plainly_when_a_metric_view_is_missing_or_cannot_be_read(string damage, string message)
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"], ["derive"]);
        await pipeline.ExecuteAsync(damage);

        var exit = await pipeline.RunAsync("report", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(message);
        await Assert.That(Directory.Exists(pipeline.ReportDirectory)).IsFalse();
    }

    [Test]
    public async Task A_view_the_owner_saved_cannot_be_read_by_a_query_over_the_views()
    {
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.ExecuteAsync("CREATE VIEW owner_saved_view AS SELECT issue_id, assignee_hash FROM issue;");
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<string>("SELECT assignee_hash FROM owner_saved_view;"))
            .Throws<InvalidOperationException>().WithMessageContaining("reads only the metric views");
    }

    private const string NoDerivedData = "The store has no derived data yet. Run derive first.";

    [Test]
    public async Task A_report_rendered_from_a_store_that_was_never_derived_says_to_run_derive()
    {
        // The renderer called directly, as another host would call it: no command has checked the store first.
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        await pipeline.RunAllAsync(["backfill"]);
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc, baselineSprintIds: [])).Throws<UserFacingException>().WithMessage(NoDerivedData);
        await Assert.That(async () => await SprintReport.MostRecentlyCompletedSprintAsync(store)).Throws<UserFacingException>().WithMessage(NoDerivedData);
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

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc, baselineSprintIds: [])).Throws<UserFacingException>().WithMessage($"The metric view {view} is not in the store. Run derive first.");
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

        await Assert.That(async () => await SprintReport.RenderAsync(store, "41", TimeZoneInfo.Utc, baselineSprintIds: [])).Throws<SqliteException>().WithMessageContaining("not a database");
    }

    [Test]
    public async Task A_migration_numbered_below_one_already_applied_is_still_applied_and_a_gap_in_the_numbers_is_accepted()
    {
        // Two branches each add a migration, and the higher number can reach a store first. The store is put in
        // that state by hand: 0006 is recorded and 0005 is not. (0005 rewrites Sprint changes in the stored
        // form; TimeInStatusTableTests shows what it does to the rows.)
        // (Until ticket 06 brought 0004, the tree held 0001 to 0003 and 0005, and every run accepted that gap.)
        await using var pipeline = new PipelineHarness();
        AddSprintAndIssue(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9)).InSprints(41).SprintsChanged(Local(2026, 1, 7, 9), 41, 42);
        await pipeline.RunAllAsync(["backfill"]);
        await pipeline.ExecuteAsync(
            """
            UPDATE change SET to_value = '41, 42' WHERE field = 'sprint';
            DELETE FROM schema_migration WHERE name = '0005_sprint_change_form.sql';
            """);
        var recordedBefore = await pipeline.QueryAsync<string>("SELECT name FROM schema_migration ORDER BY name;");

        await pipeline.RunAllAsync(["derive"]);

        await Assert.That(recordedBefore).Contains("0006_collection_scope.sql");
        await Assert.That(recordedBefore).DoesNotContain("0005_sprint_change_form.sql");
        await Assert.That(await pipeline.QueryAsync<string>("SELECT name FROM schema_migration ORDER BY name;"))
            .IsEquivalentTo(recordedBefore.Append("0005_sprint_change_form.sql").Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(await pipeline.QueryAsync<string>("SELECT DISTINCT to_value FROM change WHERE field = 'sprint';")).IsEquivalentTo(new[] { "41,42" });
    }

    [Test]
    public async Task A_query_over_the_views_opens_the_file_read_only_so_it_never_creates_one()
    {
        // B14 of the final review: the guard refuses a write before the connection would, so a write no
        // longer shows that the connection is read-only. What still does: a connection that could write
        // creates a missing database file when it opens, and a read-only one cannot open it.
        await using var pipeline = new PipelineHarness();
        var store = new SqliteStore(pipeline.DatabasePath);

        await Assert.That(async () => await store.QueryViewsAsync<long>("SELECT COUNT(*) FROM delivered_per_sprint;")).Throws<SqliteException>();

        await Assert.That(File.Exists(pipeline.DatabasePath)).IsFalse();
    }

    [Test]
    public async Task The_store_itself_refuses_a_batch_added_under_another_salt_than_its_people_were_hashed_with()
    {
        // B10 of the final review: the collector checks the salt before it fetches, but the store is where
        // both fingerprints meet, so a caller that skipped the collector cannot leave people under two salts.
        await using var pipeline = new PipelineHarness();
        var store = new SqliteStore(pipeline.DatabasePath);
        await store.InitializeAsync();
        var at = Local(2026, 1, 6, 9);
        var reached = new Watermark("synthetic-collector", new CollectionScope("https://example.invalid/", FakeJira.BoardId, FakeJira.ProjectKey, new DateOnly(2025, 1, 1), "status"), UpdatedThrough: at);
        RawBatch Batch(string issueId, string salt, bool replacesEverything = false) => new(
            new RawData(
                Issues: [new RawIssue { IssueId = issueId, IssueTypeId = FakeJira.IssueTypeStoryId, Created = at, StatusId = Workflow.New.Id, ResolutionId = null, StoryPoints = null, AssigneeHash = IdentityHash.Of("fake-account-0001", salt), Updated = at, SprintIds = "" }],
                Changes: [],
                Sprints: [],
                Statuses: []),
            reached,
            IdentityHash.SaltFingerprint(salt))
        { ReplacesEverything = replacesEverything };
        await store.WriteRawAsync(Batch("1", "synthetic-salt-one"));

        await Assert.That(async () => await store.WriteRawAsync(Batch("2", "synthetic-salt-two"))).Throws<UserFacingException>().WithMessage(
            "The hashing salt in the secret store is not the one the people in the database were hashed with, so one person would count as two. "
            + "Restore the original salt, or run backfill to hash everyone again with this one.");
        await Assert.That(await pipeline.QueryAsync<string>("SELECT issue_id FROM issue;")).IsEquivalentTo(new[] { "1" });

        // The same salt is added, and a batch that replaces everything may come under another.
        await store.WriteRawAsync(Batch("3", "synthetic-salt-one"));
        await store.WriteRawAsync(Batch("4", "synthetic-salt-two", replacesEverything: true));
        await Assert.That(await pipeline.QueryAsync<string>("SELECT issue_id FROM issue;")).IsEquivalentTo(new[] { "4" });
        await Assert.That(await store.ReadSaltFingerprintAsync()).IsEqualTo(IdentityHash.SaltFingerprint("synthetic-salt-two"));
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
