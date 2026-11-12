using System.Net;
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
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development);
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
    public async Task A_sync_goes_back_a_few_minutes_before_the_newest_update_it_has_seen()
    {
        // Jira's search index lags: an issue updated just before the newest one seen may not have been
        // in the last answer. Three minutes back is inside the overlap; a quarter of an hour is not.
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 22, 8, 45), Workflow.Development);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 22, 8, 57), Workflow.Development);
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
        var storeBefore = await pipeline.ReadStoreContentAsync();
        var reportBefore = await pipeline.ReadReportBytesAsync(42);

        pipeline.Clock.Advance(TimeSpan.FromDays(1));
        await pipeline.RunAllAsync(["sync"], ["derive"], ["report", "--sprint", "42"]);

        // The second fetch was not empty: the overlap brings the newest issue back, and it is written again.
        await Assert.That(pipeline.Jira.SearchAnswers[1]).IsEquivalentTo(new[] { "FAKE-2" });
        await Assert.That(storeBefore).Contains("FAKE-2");
        await Assert.That(await pipeline.ReadStoreContentAsync()).IsEqualTo(storeBefore);
        await Assert.That(await pipeline.ReadReportBytesAsync(42)).IsEqualTo(reportBefore);
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

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
