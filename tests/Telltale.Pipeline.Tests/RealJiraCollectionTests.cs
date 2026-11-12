using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>What a real Jira Cloud site does at scale (ADR-0004): pages, repeated history entries, rate limits, old history.</summary>
public class RealJiraCollectionTests
{
    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Issues_spread_over_several_search_pages_are_all_collected(JiraShapes shapes)
    {
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        pipeline.Jira.SearchPageSize = 2;
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 5);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 5\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/search/jql")).IsEqualTo(3);
    }

    [Test]
    [Arguments(JiraShapes.AsSchemas)]
    [Arguments(JiraShapes.AsExamples)]
    public async Task Changelogs_spread_over_several_pages_are_all_collected(JiraShapes shapes)
    {
        // Three issues with two moves each: six history entries, two to a page. The entries come oldest first,
        // so each issue's moves are on different pages.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.Shapes = shapes;
        pipeline.Jira.ChangelogPageSize = 2;
        AddSprint41(pipeline.Jira);
        AddDeliveredIssues(pipeline.Jira, count: 3);

        await pipeline.RunAllAsync(["run", "--sprint", "41"]);

        await Assert.That(await pipeline.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
        await Assert.That(pipeline.Jira.Handler.Requests.Count(request => request.Path == "/rest/api/3/changelog/bulkfetch")).IsEqualTo(3);
    }

    [Test]
    public async Task The_same_history_entry_returned_on_two_pages_is_stored_once_and_changes_no_figure()
    {
        // ADR-0004. The same site twice: once every changelog page after the first repeats the entry before it, once not.
        await using var repeating = new PipelineHarness();
        await using var plain = new PipelineHarness();
        foreach (var pipeline in new[] { repeating, plain })
        {
            pipeline.Jira.ChangelogPageSize = 2;
            AddSprint41(pipeline.Jira);
            AddDeliveredIssues(pipeline.Jira, count: 3);
        }

        repeating.Jira.RepeatsHistoryOnNextPage = true;

        await repeating.RunAllAsync(["run", "--sprint", "41"]);
        await plain.RunAllAsync(["run", "--sprint", "41"]);

        // Six moves, each stored once (the raw table is what "stored once" is about).
        await Assert.That(await repeating.QueryAsync<long>("SELECT COUNT(*) FROM change;")).IsEquivalentTo(new[] { 6L });
        await Assert.That(await repeating.ReadStoreContentAsync()).IsEqualTo(await plain.ReadStoreContentAsync());
        await Assert.That(await repeating.ReadReportBytesAsync(41)).IsEqualTo(await plain.ReadReportBytesAsync(41));
        await Assert.That(await plain.ReadReportAsync(41)).Contains("\nDelivered issues: 3\n");
    }

    [Test]
    [Arguments(SprintListEnd.IsLastAndTotal)]
    [Arguments(SprintListEnd.IsLastOnly)]
    [Arguments(SprintListEnd.TotalOnly)]
    [Arguments(SprintListEnd.Neither)]
    public async Task A_board_whose_sprints_span_several_pages_has_all_of_them_collected(SprintListEnd sprintListSays)
    {
        // Five closed sprints, two to a page, and an issue delivered in each of the first and the last.
        await using var pipeline = new PipelineHarness();
        pipeline.Jira.SprintListSays = sprintListSays;
        pipeline.Jira.SprintPageSize = 2;
        for (var sprint = 0; sprint < 5; sprint++)
        {
            var start = Local(2025, 11, 3, 9).AddDays(14 * sprint);
            pipeline.Jira.AddSprint(41 + sprint, $"Fake Sprint {41 + sprint}", start, plannedEnd: start.AddDays(14), completed: start.AddDays(14));
        }

        pipeline.Jira.AddIssue("FAKE-1", Local(2025, 11, 4, 9))
            .Moved(Local(2025, 11, 5, 9), Workflow.Development)
            .Moved(Local(2025, 11, 6, 9), Workflow.Done);
        pipeline.Jira.AddIssue("FAKE-2", Local(2025, 12, 30, 9))
            .Moved(Local(2025, 12, 31, 9), Workflow.Development)
            .Moved(Local(2026, 1, 2, 9), Workflow.Done);

        await pipeline.RunAllAsync(["run"]);

        var delivered = await pipeline.QueryAsync<(string SprintId, long DeliveredCount)>("SELECT sprint_id, delivered_count FROM delivered_per_sprint ORDER BY sprint_id;");
        await Assert.That(delivered).IsEquivalentTo(new[] { ("41", 1L), ("42", 0L), ("43", 0L), ("44", 0L), ("45", 1L) });
        await Assert.That(await pipeline.ReadReportAsync(45)).StartsWith("# Fake Sprint 45 ");
    }

    private static void AddSprint41(FakeJira jira) =>
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));

    /// <summary>Issues FAKE-1 to FAKE-n, each created, started and finished inside sprint 41, an hour after the one before.</summary>
    private static void AddDeliveredIssues(FakeJira jira, int count)
    {
        for (var number = 1; number <= count; number++)
        {
            jira.AddIssue($"FAKE-{number}", Local(2026, 1, 6, 9))
                .Moved(Local(2026, 1, 7, 9 + number), Workflow.Development)
                .Moved(Local(2026, 1, 9, 9 + number), Workflow.Done);
        }
    }
}
