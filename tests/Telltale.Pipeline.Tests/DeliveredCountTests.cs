using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

public class DeliveredCountTests
{
    [Test]
    public async Task Run_writes_a_report_with_the_sprint_name_dates_and_delivered_count()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 8, 9), Workflow.Development)
            .Moved(Local(2026, 1, 14, 11), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        var report = await pipeline.ReadReportAsync(41);
        // The sprint started at 20:00 on 5 January in the team's zone, which is already 6 January in UTC.
        await Assert.That(report).StartsWith("# Fake Sprint 41 (2026-01-05 to 2026-01-19)\n");
        await Assert.That(report).Contains("\nDelivered issues: 5\n");
    }

    [Test]
    public async Task The_delivered_count_can_be_read_from_a_metric_view_with_ad_hoc_sql()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 20, 9))
            .Moved(Local(2026, 1, 21, 9), Workflow.Development)
            .Moved(Local(2026, 1, 23, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "42");

        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 4L), ("42", 5L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 5\n");
    }

    [Test]
    public async Task An_issue_finished_in_a_later_sprint_than_it_started_in_counts_in_the_later_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task A_move_from_done_to_shipped_does_not_change_the_delivery_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 28, 9), Workflow.Shipped);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 4\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    public async Task An_issue_that_was_done_then_reopened_and_is_not_done_now_is_not_delivered()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    public async Task A_reopened_issue_counts_in_the_sprint_of_its_final_move_to_done()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task An_issue_whose_history_ends_in_done_but_which_is_not_in_a_done_status_now_is_not_delivered()
    {
        // The issue's current status decides, not the last entry of its changelog.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .NowIn(Workflow.Development);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    public async Task An_issue_created_in_a_done_status_and_never_moved_into_done_is_not_delivered()
    {
        // Owner decision D9. Without a transition into done there is no delivery moment, so no sprint to count it in.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 8, 9), createdIn: Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 8, 9), createdIn: Workflow.Done)
            .Moved(Local(2026, 1, 22, 9), Workflow.Shipped);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 3L), ("42", 3L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task An_issue_in_a_done_status_whose_history_ends_outside_done_is_not_delivered_even_with_an_earlier_move_into_done()
    {
        // Owner decision D8. The changelog says the issue left Done on the 12th; Jira says it is in Done now.
        // The earlier move into Done (in sprint 41) was undone, and nothing dates the present one.
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .NowIn(Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(exit).IsEqualTo(0);
        var rows = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>(
            "SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 3L), ("42", 3L) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task Renaming_statuses_changes_no_figure()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);

        // The names now say the opposite of what the statuses are. Only the IDs carry the roles.
        pipeline.Jira.RenameStatus(Workflow.Done, "Fake New");
        pipeline.Jira.RenameStatus(Workflow.New, "Fake Done");
        pipeline.Jira.RenameStatus(Workflow.Development, "Fake Shipped");
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 15), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);
        pipeline.Jira.AddIssue("FAKE-3", Local(2026, 1, 6, 9));

        await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Both_documented_forms_of_jiras_responses_give_the_same_count(JiraShapes shapes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);
        pipeline.Jira.AddTeam(41, 42);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 4\n");
    }

    [Test]
    public async Task A_sprint_that_is_not_on_the_board_is_refused_with_a_plain_message()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprintsTheFirstFromMondayEvening(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "99");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Sprint 99 is not in the store, or has not started. Check the ID against the board's sprints.\n");
    }

    /// <summary>The harness's two sprints, both completed, with sprint 41 starting on Monday 5 January at 20:00 instead of 09:00.</summary>
    private static void AddTwoSprintsTheFirstFromMondayEvening(FakeJira jira)
    {
        jira.AddSprint41(start: Local(2026, 1, 5, 20));
        jira.AddSprint42();
    }
}
