using Telltale.Pipeline.Tests.Harness;
using static Telltale.Pipeline.Tests.Harness.Scenarios;
using static Telltale.Tests.Shared.TeamTime;

namespace Telltale.Pipeline.Tests;

/// <summary>
/// Pull request figures are not subject to the minimum group size (owner decision D114): the sprint report and
/// the owner's <c>query</c> show them whatever the numbers are, while the Jira figures of the same run are
/// withheld as before. No pull request result is per person.
/// </summary>
public class PullRequestFiguresNotWithheldTests
{
    private const string EveryRow = "SELECT * FROM pr_timing_per_sprint ORDER BY sprint_id, measure_position;";

    /// <summary>
    /// Sprint 41 with four Jira assignees, which the default numbers withhold whole, and one pull request by a
    /// single author, opened Tuesday 6 January at noon and reviewed two days later.
    /// </summary>
    private static void AddAFourPersonSprintAndOnePullRequest(PipelineHarness pipeline)
    {
        AddSprint41WithFourAssignees(pipeline.Jira);
        FakeGitHub.For(pipeline).AddPullRequest(1, Accounts.AuthorOne, Local(2026, 1, 6, 12)).Reviewed(Accounts.Reviewer, "COMMENTED", Local(2026, 1, 8, 12));
    }

    [Test]
    public async Task By_the_default_numbers_a_single_authors_pull_request_figures_are_shown_in_the_report_while_the_jira_figures_are_withheld()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndOnePullRequest(pipeline);

        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        var report = await pipeline.ReadReportAsync(41);
        await Assert.That(report).Contains("\nDelivered issues: suppressed\n\n## Cycle time by point bucket\n\nsuppressed\n\n## Time in status\n\nsuppressed\n");
        await Assert.That(report).Contains(
            "\n## Pull request timing\n\n| Wait from opening to | Pull requests | Median (elapsed days) | P85 (elapsed days) | Median (business days) | P85 (business days) |\n| --- | ---: | ---: | ---: | ---: | ---: |\n"
            + "| First code review | 1 | 2.0 | 2.0 | 2.0 | 2.0 |\n| First code approval | 0 | none | none | none | none |\n| Merge | 0 | none | none | none | none |\n\n- These are");
        await Assert.That(report).Contains("- Pull request figures are not subject to the minimum group size: none is withheld for the number of people behind it, and none is reported per person.\n");

        // The method note does not contradict that (review 2, finding 18): its rule is said of the Jira
        // figures, it names the tables it withholds, and it says the pull request figures are the exception.
        await Assert.That(report).Contains("\n- A figure derived from Jira is shown only where it cannot show one person's work. ");
        await Assert.That(report).Contains(" When the team is smaller than five, the delivered count, the cycle time table and the time in status table read \"suppressed\", with no rows.");
        await Assert.That(report).Contains(" The pull request figures and counts are the exception: they are not subject to the minimum group size, and none of them is withheld (see Pull request timing).\n- An assignee is");
        await Assert.That(report).DoesNotContain("- A figure is shown only");
        await Assert.That(report).DoesNotContain("both tables");
    }

    [Test]
    public async Task By_the_default_numbers_the_owner_query_gives_the_same_pull_request_figures_and_still_withholds_the_jira_ones()
    {
        await using var pipeline = new PipelineHarness();
        AddAFourPersonSprintAndOnePullRequest(pipeline);
        await pipeline.RunAllAsync(["backfill"], ["run", "--sprint", "41"]);

        // The view has no flag to set: a figure is there, or there is no wait and so no figure.
        await Assert.That(await HarnessSteps.QueryAsync(pipeline, EveryRow, "SELECT sprint_id, delivered_count, suppressed FROM delivered_per_sprint;")).IsEqualTo(
            """
            | sprint_id | measure | measure_position | host_scope | host_scope_position | coverage | sample_count | median_elapsed_days | p85_elapsed_days | median_business_days | p85_business_days |
            | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
            | NULL | first_code_review | 1 | github | 1 | read | 0 | NULL | NULL | NULL | NULL |
            | NULL | first_code_approval | 2 | github | 1 | read | 0 | NULL | NULL | NULL | NULL |
            | NULL | merge | 3 | github | 1 | read | 0 | NULL | NULL | NULL | NULL |
            | 41 | first_code_review | 1 | github | 1 | read | 1 | 2 | 2 | 2 | 2 |
            | 41 | first_code_approval | 2 | github | 1 | read | 0 | NULL | NULL | NULL | NULL |
            | 41 | merge | 3 | github | 1 | read | 0 | NULL | NULL | NULL | NULL |
            | sprint_id | delivered_count | suppressed |
            | --- | --- | --- |
            | 41 | NULL | 1 |

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task The_pull_request_figures_are_the_same_whatever_the_numbers_are()
    {
        await using var byDefault = new PipelineHarness();
        await using var off = SuppressionOff();
        foreach (var pipeline in new[] { byDefault, off })
        {
            AddAFourPersonSprintAndOnePullRequest(pipeline);
            await pipeline.RunAllAsync(["backfill"], ["derive"]);
        }

        await Assert.That(await HarnessSteps.QueryAsync(byDefault, EveryRow)).IsEqualTo(await HarnessSteps.QueryAsync(off, EveryRow));
    }
}
