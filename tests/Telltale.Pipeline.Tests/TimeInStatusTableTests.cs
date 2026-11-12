using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// The report's time in status table: per status, how many issues spent time in it during the sprint
/// and the median business days they spent, counting only time inside the sprint window and while
/// the issue was a member of the sprint (GLOSSARY: Time in status).
/// </summary>
public class TimeInStatusTableTests
{
    [Test]
    public async Task The_report_shows_the_issue_count_and_median_business_days_for_each_status_issues_were_in_during_the_sprint()
    {
        await using var pipeline = new PipelineHarness();
        AddTwoSprints(pipeline.Jira);

        // In the sprint from before it started. New until Tuesday 09:00, Development until Thursday 09:00.
        pipeline.Jira.AddIssue("FAKE-1", Local(2026, 1, 2, 9)).InSprints(41)
            .Moved(Local(2026, 1, 6, 9), Workflow.Development)
            .Moved(Local(2026, 1, 8, 9), Workflow.Done);

        var exit = await pipeline.RunAsync("run", "--sprint", "41");

        await Assert.That(pipeline.Error.ToString()).IsEmpty();
        await Assert.That(exit).IsEqualTo(0);

        // New counts from the sprint's start on Monday 09:00, not from the issue's creation the Friday before.
        await Assert.That(await pipeline.ReadReportAsync(41)).Contains(
            """

            ## Time in status

            | Status | Issues | Median business days |
            | --- | ---: | ---: |
            | Fake New | 1 | 1.0 |
            | Fake Development | 1 | 2.0 |

            """.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Sprint 41: Monday 5 January 2026 09:00 to Monday 19 January 09:00, completed.
    /// Sprint 42: Monday 19 January 12:00 to Monday 2 February 09:00, completed.
    /// </summary>
    private static void AddTwoSprints(FakeJira jira)
    {
        jira.AddSprint(41, "Fake Sprint 41", Local(2026, 1, 5, 9), plannedEnd: Local(2026, 1, 19, 9), completed: Local(2026, 1, 19, 9));
        jira.AddSprint(42, "Fake Sprint 42", Local(2026, 1, 19, 12), plannedEnd: Local(2026, 2, 2, 9), completed: Local(2026, 2, 2, 9));
    }
}
