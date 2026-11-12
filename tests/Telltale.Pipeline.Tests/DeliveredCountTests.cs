using Telltale.Pipeline.Tests.Harness;
using TUnit.Assertions.Enums;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

public class DeliveredCountTests
{
    [Test]
    public async Task Run_writes_a_report_with_the_sprint_name_dates_and_delivered_count()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
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
        await Assert.That(report).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task The_delivered_count_can_be_read_from_a_metric_view_with_ad_hoc_sql()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
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
        await Assert.That(rows).IsEquivalentTo(new[] { ("41", 1L), ("42", 2L) }, CollectionOrdering.Matching);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 2\n");
    }

    [Test]
    public async Task An_issue_finished_in_a_later_sprint_than_it_started_in_counts_in_the_later_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 0\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_move_from_done_to_shipped_does_not_change_the_delivery_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 28, 9), Workflow.Shipped);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 0\n");
    }

    [Test]
    public async Task An_issue_that_was_done_then_reopened_and_is_not_done_now_is_not_delivered()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 0\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 0\n");
    }

    [Test]
    public async Task A_reopened_issue_counts_in_the_sprint_of_its_final_move_to_done()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 9, 9), Workflow.Done)
            .Moved(Local(2026, 1, 12, 9), Workflow.Development)
            .Moved(Local(2026, 1, 22, 9), Workflow.Done);

        await pipeline.RunAsync("run", "--sprint", "41");
        await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 0\n");
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task Renaming_statuses_changes_no_figure()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

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

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Both_documented_forms_of_jiras_responses_give_the_same_count(JiraShapes shapes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        AddTwoSprints(pipeline.Jira);
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 6, 9))
            .Moved(Local(2026, 1, 7, 9), Workflow.Development)
            .Moved(Local(2026, 1, 21, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "42");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);
        await Assert.That(await pipeline.ReadReportAsync(42)).Contains("\nDelivered issues: 1\n");
    }

    [Test]
    public async Task A_sprint_that_is_not_on_the_board_is_refused_with_a_plain_message()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        var exit = await pipeline.RunAsync("run", "--sprint", "99");

        await Assert.That(exit).IsEqualTo(1);
        await Assert.That(pipeline.Error.ToString().ReplaceLineEndings("\n")).IsEqualTo("Sprint 99 is not in the store, or has not started. Check the ID against the board's sprints.\n");
    }

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed, with a half-hour gap between them.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 20), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
