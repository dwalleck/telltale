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

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
