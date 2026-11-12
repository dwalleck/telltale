using System.Net;
using System.Text.Json.Nodes;
using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary><c>backfill</c> fetches everything once; <c>sync</c> fetches what changed since.</summary>
public class IncrementalSyncTests
{
    [Test]
    public async Task After_a_backfill_a_sync_picks_up_an_issue_changed_in_jira_and_the_report_reflects_it()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        var inProgress = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");

        inProgress.Moved(Local(2026, 1, 30, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task A_sync_fetches_only_the_issues_updated_since_the_last_fetch()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 9, 9))
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);
        var changing = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 22, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);

        changing.Moved(Local(2026, 1, 30, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(pipeline.Jira.SearchAnswers).Count().IsEqualTo(2);
        await Assert.That(pipeline.Jira.SearchAnswers[0]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2" });
        await Assert.That(pipeline.Jira.SearchAnswers[1]).IsEquivalentTo(new[] { "FAKE-2" });
    }

    [Test]
    [Arguments("Etc/GMT+12")]
    [Arguments("America/Chicago")]
    [Arguments("Etc/UTC")]
    [Arguments("Pacific/Kiritimati")]
    public async Task A_sync_goes_back_far_enough_before_the_newest_update_for_any_jira_account_time_zone(string accountTimeZone)
    {
        // Owner decision D26. Jira reads the date in the JQL in the account's time zone, which the tool does
        // not know, and its search index lags. So a sync asks from two days before the newest update it has
        // seen: an issue updated a day before that is fetched again wherever the account is, from twelve
        // hours behind UTC to fourteen ahead; one updated five days before is not.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.AccountTimeZone = TimeZoneInfo.FindSystemTimeZoneById(accountTimeZone);
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 9, 9))
            .Moved(Local(2026, 1, 17, 9), Workflow.Development);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 22, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);

        await pipeline.RunAllAsync(["sync"]);

        await Assert.That(pipeline.Jira.SearchAnswers[1]).IsEquivalentTo(new[] { "FAKE-2", "FAKE-3" });
    }

    [Test]
    public async Task A_backfill_fetches_everything_again_including_what_a_sync_passes_over()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 29, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);

        // An issue the store lacks whose last update is older than anything a sync asks for, as after a
        // project is added to projectKeys.
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);
        var afterSync = await pipeline.ReadReportAsync(42);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(afterSync).Contains("\nDelivered issues: 1\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task A_sync_with_nothing_changed_leaves_the_store_and_the_report_identical()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One).Estimated(3)
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 23, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "42"]);
        // Identical apart from the as-of time, which is a day later the second time (owner decision D33).
        var storeBefore = await pipeline.ReadStoreContentAsync(withoutAsOf: true);
        var reportBefore = await pipeline.ReadReportBytesAsync(42, withoutAsOf: true);

        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        // The second fetch was not empty: the overlap brings the newest issues back, and they are written again.
        await Assert.That(pipeline.Jira.SearchAnswers[1]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2" });
        await Assert.That(storeBefore).Contains("FAKE-2");
        await Assert.That(await pipeline.ReadStoreContentAsync(withoutAsOf: true)).IsEqualTo(storeBefore);
        await Assert.That(await pipeline.ReadReportBytesAsync(42, withoutAsOf: true)).IsEqualTo(reportBefore);
    }

    [Test]
    public async Task A_sync_that_fails_part_way_does_not_move_the_watermark_so_the_next_one_fetches_the_same_changes()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var first = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        var second = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);

        // Two changes days apart, so a watermark moved to the later one would pass over the earlier.
        first.Moved(Local(2026, 1, 26, 9), Workflow.Done);
        second.Moved(Local(2026, 1, 30, 9), Workflow.Done);
        var jiraIsFailing = true;
        pipeline.Jira.Handler.Override(HttpMethod.Post, "/rest/api/3/changelog/bulkfetch", _ => jiraIsFailing,
            StubHttpHandler.Json("{}", HttpStatusCode.InternalServerError));
        var failed = await pipeline.RunAsync("sync");

        jiraIsFailing = false;
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(failed).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Jira answered 500 (InternalServerError) to POST rest/api/3/changelog/bulkfetch.\n");
        await Assert.That(pipeline.Jira.SearchAnswers[^1]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2" });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task A_batch_that_fails_while_it_is_written_leaves_nothing_behind_and_does_not_move_the_watermark()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var first = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        var second = pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);
        var storeBefore = await pipeline.ReadStoreContentAsync();

        // The database refuses a write late in the batch, after the issues and their changes have gone in.
        first.Moved(Local(2026, 1, 26, 9), Workflow.Done);
        second.Moved(Local(2026, 1, 30, 9), Workflow.Done);
        await pipeline.ExecuteAsync("CREATE TRIGGER synthetic_failure BEFORE INSERT ON status BEGIN SELECT RAISE(ABORT, 'synthetic write failure'); END;");
        var failed = await pipeline.RunAsync("sync");
        await pipeline.ExecuteAsync("DROP TRIGGER synthetic_failure;");
        var storeAfterFailure = await pipeline.ReadStoreContentAsync();

        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(failed).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("synthetic write failure");
        await Assert.That(storeAfterFailure).IsEqualTo(storeBefore);
        await Assert.That(pipeline.Jira.SearchAnswers[^1]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2" });
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    [Arguments("projectKeys")]
    [Arguments("backfillStart")]
    [Arguments("boardId")]
    public async Task A_sync_is_refused_when_the_setting_that_decides_what_is_fetched_changed_since_the_store_was_filled(string setting)
    {
        // The watermark only says how far the old projects, board and start date were fetched. Trusting it
        // after one of them changes would pass over every older issue the new setting brings in.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["backfill"]);
        var requestsBefore = pipeline.Jira.Handler.Requests.Count;

        pipeline.Config["jira"]![setting] = setting switch
        {
            "projectKeys" => new JsonArray(FakeJira.ProjectKey, "MORE"),
            "backfillStart" => "2024-01-01",
            _ => FakeJira.BoardId + 1,
        };
        var sync = await pipeline.RunAsync("sync");
        var run = await pipeline.RunAsync("run", "--sprint", "42");

        const string Refusal = "boardId, projectKeys or backfillStart in the configuration file changed since the store was last filled, so a sync could miss issues. Run backfill.\n";
        await Assert.That(sync).IsEqualTo(1);
        await Assert.That(run).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(Refusal + Refusal);
        await Assert.That(pipeline.Jira.Handler.Requests.Count).IsEqualTo(requestsBefore);
    }

    [Test]
    public async Task After_a_backfill_under_the_changed_setting_sync_works_again()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        var issue = pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
        await pipeline.RunAllAsync(["backfill"]);
        pipeline.Config["jira"]!["projectKeys"] = new JsonArray(FakeJira.ProjectKey, "MORE");

        await pipeline.RunAllAsync(["backfill"]);
        issue.Moved(Local(2026, 1, 22, 9), Workflow.Done);
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_backfill_run_again_after_the_board_changed_gives_the_same_store_as_one_backfill_of_the_changed_board()
    {
        // Every row is fetched and written a second time: issues with new field values, changelog entries
        // old and new, the sprints and the renamed status.
        await using var twice = new PipelineHarness();
        BuildBoard(twice.Jira);
        await twice.RunAllAsync(["backfill"], ["derive"]);
        ChangeBoard(twice.Jira);
        await twice.RunAllAsync(["backfill"], ["derive"]);

        await using var once = new PipelineHarness();
        BuildBoard(once.Jira);
        ChangeBoard(once.Jira);
        await once.RunAllAsync(["backfill"], ["derive"]);

        await Assert.That(twice.Jira.SearchAnswers[1]).IsEquivalentTo(new[] { "FAKE-1", "FAKE-2", "FAKE-3" });
        var store = await twice.ReadStoreContentAsync();
        await Assert.That(store).Contains("FAKE-3");
        await Assert.That(store).Contains("Fake Renamed");
        await Assert.That(store).IsEqualTo(await once.ReadStoreContentAsync());

        static void BuildBoard(FakeJira jira)
        {
            AddTwoSprints(jira);
            jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9)).AssignedTo(People.One).Estimated(3)
                .Moved(Local(2026, 1, 21, 9), Workflow.Development);
            jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
                .Moved(Local(2026, 1, 21, 9), Workflow.Development)
                .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        }

        static void ChangeBoard(FakeJira jira)
        {
            jira.Issues[0].AssignedTo(People.Two).Estimated(5).Resolved("700001").Moved(Local(2026, 1, 28, 9), Workflow.Done);
            jira.AddIssue("FAKE-3", Local(2026, 1, 27, 9));
            jira.RenameStatus(Workflow.Review, "Fake Renamed");
        }
    }

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
