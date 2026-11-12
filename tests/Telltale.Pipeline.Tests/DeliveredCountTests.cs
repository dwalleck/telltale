using Telltale.Pipeline.Tests.Harness;
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

    /// <summary>Sprint 41 (5 to 19 January 2026) and sprint 42 (19 January to 2 February), both completed, with a half-hour gap between them.</summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 20), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 10));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 10, 30), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9, 30));
    }
}
