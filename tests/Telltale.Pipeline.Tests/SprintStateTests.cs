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
        // Not completed yet, so the title gives no end: the figures run to the as-of time, not to the planned end.
        await Assert.That(report).StartsWith("# Fake Sprint 42 (from 2026-01-19)\n");
        await Assert.That(report).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_sprint_ends_at_its_completion_not_at_its_planned_end()
    {
        // Sprint 41 was planned to end at 9:00 on 19 January and was completed at 10:00.
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 19, 9, 30), Workflow.Done);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task An_active_sprint_past_its_planned_end_still_takes_deliveries_and_its_title_gives_no_end()
    {
        // Sprint 42 was planned to end on 2 February and is still running on the 5th.
        await using var pipeline = new PipelineHarness(Local(2026, 2, 5, 12));
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 2, 4, 9), Workflow.Done);

        await pipeline.RunAllAsync(["run", "--sprint", "42"]);

        var report = await pipeline.ReadReportAsync(42);
        await Assert.That(report).StartsWith("# Fake Sprint 42 (from 2026-01-19)\n");
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
    public async Task A_closed_sprint_with_no_completion_time_ends_at_its_planned_end()
    {
        // Owner decision D7. Without it the sprint's window would run to now and take every later delivery.
        await using var pipeline = new PipelineHarness(Now);
        pipeline.Jira.AddClosedSprintWithoutCompletion(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9));
        pipeline.Jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 19, 9, 30), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);

        // FAKE-1 is inside sprint 41; FAKE-2 falls between its planned end and sprint 42's start; FAKE-3 is in sprint 42.
        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 1L), ("42", 1L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(41)).StartsWith("# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n");
    }

    [Test]
    public async Task A_closed_sprint_with_neither_a_completion_time_nor_a_planned_end_stops_the_run_and_is_named()
    {
        // Nothing says when this sprint ended, and guessing would move deliveries between sprints.
        await using var pipeline = new PipelineHarness(Now);
        pipeline.Jira.AddClosedSprintWithoutCompletion(41, "Fake Sprint 41", Local(2026, 1, 5, 9));
        pipeline.Jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9));
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo(
            "Sprint 41 is closed, but Jira gives it neither a completion time nor a planned end, so when it ended is unknown. No figures were derived.\n");
        await Assert.That(File.Exists(Path.Combine(pipeline.ReportDirectory, "sprint-42.md"))).IsFalse();
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
    public async Task Sprints_stored_for_a_board_that_is_no_longer_the_configured_one_take_no_delivery()
    {
        // The first run used the wrong board ID; its sprint is still in the store after the ID is corrected.
        const long wrongBoardId = 499999;
        await using var pipeline = new PipelineHarness(Now);
        AddClosedAndActiveSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 9), Workflow.Done);
        pipeline.Jira.Handler.On(HttpMethod.Get, $"/rest/agile/1.0/board/{wrongBoardId}/sprint", StubHttpHandler.Json(
            """{"isLast":true,"values":[{"id":77,"state":"closed","name":"Fake Other Team Sprint","startDate":"2026-01-01T15:00:00.000Z","endDate":"2026-01-29T15:00:00.000Z","completeDate":"2026-01-29T15:00:00.000Z","originBoardId":499999}]}"""));
        pipeline.Config["jira"]!["boardId"] = wrongBoardId;
        await pipeline.RunAsync("run", "--sprint", "77");
        await Assert.That(await pipeline.ReadReportAsync(77)).Contains("\nDelivered issues: 1\n");

        // A sync is refused once the board ID has changed (the watermark was for the other board), so the owner backfills.
        pipeline.Config["jira"]!["boardId"] = FakeJira.BoardId;
        await pipeline.RunAllAsync(["backfill"], ["derive"], ["report", "--sprint", "41"]);
        var reportOfTheOtherBoardsSprint = await pipeline.RunAsync("report", "--sprint", "77");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
        await Assert.That(reportOfTheOtherBoardsSprint).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString()).Contains("Sprint 77 is not in the store, or has not started.");
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
