using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>Sprints that are not closed: the active one, whose window runs to the as-of time, and future ones, which have no window.</summary>
public class SprintStateTests
{
    private static readonly DateTimeOffset Now = Local(2026, 1, 28, 12);

    [Test]
    public async Task An_issue_delivered_in_the_active_sprint_counts_in_it()
    {
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 27, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var report = await pipeline.ReadReportAsync(42);
        // Not completed yet, so the title shows the planned end.
        await Assert.That(report).StartsWith("# Fake Sprint 42 (2026-01-19 to 2026-02-02)\n");
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task An_issue_moved_to_done_while_the_sync_is_running_counts_in_the_active_sprint()
    {
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);

        // Each of Jira's answers takes a minute, and the issue is moved to Done two minutes into the sync.
        pipeline.Jira.BeforeResponse = () => pipeline.Clock.Advance(TimeSpan.FromMinutes(1));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Now.AddMinutes(2), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task An_issue_jira_dates_slightly_after_this_machines_clock_still_counts_in_the_active_sprint()
    {
        // Jira's clock and the laptop's are not the same clock.
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Now.AddSeconds(30), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_future_sprint_with_planned_dates_is_refused_as_not_started()
    {
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddFutureSprint(43, "Fake Sprint 43", plannedStart: Local(2026, 2, 2, 10), plannedEnd: Local(2026, 2, 16, 9));

        var exit = await pipeline.RunAsync("run", "--sprint", "43");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Sprint 43 is not in the store, or has not started. Check the ID against the board's sprints.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-43.md"))).IsFalse();
    }

    [Test]
    public async Task A_future_sprint_takes_no_delivery_even_when_its_planned_dates_cover_one()
    {
        // The plan said sprint 43 would start on the 26th; it has not started, and sprint 42 is still running.
        await using var pipeline = new PipelineHarness(Now);
        pipeline.Jira.AddFutureSprint(43, "Fake Sprint 43", plannedStart: Local(2026, 1, 12, 9), plannedEnd: Local(2026, 2, 9, 9));
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 27, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 0L), ("42", 1L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_created_on_another_board_takes_no_delivery()
    {
        // Jira lists another board's sprint on this board when one of its issues matches the board's filter.
        await using var pipeline = new PipelineHarness(Now);
        pipeline.Jira.AddSprint(77, "Fake Other Team Sprint", Local(2026, 1, 12, 9), plannedEnd: Local(2026, 1, 26, 9), completed: Local(2026, 1, 26, 9), originBoardId: 499999);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 1L), ("42", 0L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_sprint_whose_origin_board_jira_leaves_out_is_kept()
    {
        await using var pipeline = new PipelineHarness(Now);
        pipeline.Jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10), originBoardId: null);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    /// <summary>Sprint 41 (5 to 19 January 2026, completed) and sprint 42 (from 19 January, planned to end 2 February, still active).</summary>
    private static void AddClosedAndActiveSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9));
    }
}
